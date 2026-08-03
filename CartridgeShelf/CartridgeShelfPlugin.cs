using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

using CartridgeShelf.Core;
using CartridgeShelf.Database;
using CartridgeShelf.Detectors;
using CartridgeShelf.Services;

using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;

namespace CartridgeShelf
{
    public class CartridgeShelfPlugin : GenericPlugin
    {
        private readonly ILogger logger;

        private CartridgeWatcher watcher;

        private Timer watcherStartRetryTimer;

        private const int WatcherStartRetryDelayMs = 2000;

        private CartridgeDatabase cartridgeDatabase;

        private UserLibrary userLibrary;

        private GameSlotManager slotManager;

        private string userAdditionsPath;

        private string userLibraryPath;

        private readonly HashSet<string> declinedEmulatorSetup =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly List<ICartridgeDetector> detectors =
            new List<ICartridgeDetector>();

        // Vrai seulement pour la toute première évaluation d'état au
        // démarrage (CheckNow appelé depuis TryStartWatcher, sur le
        // thread UI pendant OnApplicationStarted). À ce moment-là, on ne
        // veut jamais de dialogue bloquant (SelectString etc.) qui
        // interromprait le démarrage de Playnite -- quelle que soit la
        // raison pour laquelle une cartouche semble présente à cet
        // instant. Repasse à false dès le premier événement traité ;
        // toute insertion/retrait ultérieur pendant la session redevient
        // interactif normalement.
        private bool isStartupCheck = true;


        public override Guid Id =>
            Guid.Parse("a1312fcb-7107-4168-95ba-181dd6069299");


        public CartridgeShelfPlugin(IPlayniteAPI api)
            : base(api)
        {
            logger = LogManager.GetLogger();

            logger.Info("CartridgeShelf : plugin chargé, attente du démarrage complet de Playnite.");
        }


        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);

            try
            {
                InitializeDetectors();
                InitializeDatabases();
                InitializeSlotManager();
                InitializeUriHandler();
                InitializeWatcher();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "CartridgeShelf : erreur pendant l'initialisation.");
            }
        }


        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            watcherStartRetryTimer?.Dispose();
            watcherStartRetryTimer = null;

            watcher?.Stop();

            base.OnApplicationStopped(args);
        }


        private void InitializeDetectors()
        {
            // Ajouter ici un détecteur par lecteur de cartouches supporté
            // (GB Operator, etc.) au fur et à mesure.
            detectors.Add(new SnOperatorDetector());
        }


        private void InitializeDatabases()
        {
            string pluginFolder = Path.GetDirectoryName(GetType().Assembly.Location);

            cartridgeDatabase = new CartridgeDatabase();

            cartridgeDatabase.Load(
                Path.Combine(pluginFolder, "Database", "Snes.csv"),
                PlatformId.SuperNintendo
            );

            // Base complémentaire propre à cette installation : les jeux
            // ajoutés au fil de l'eau via le dialogue "cartouche inconnue"
            // (voir OnCartridgeInserted). Vit dans le dossier de données du
            // plugin, pas dans le dossier de l'extension -- jamais touchée
            // par une mise à jour ou une réinstallation du plugin.
            userAdditionsPath = Path.Combine(GetPluginUserDataPath(), "UserSnesAdditions.csv");

            if (File.Exists(userAdditionsPath))
            {
                cartridgeDatabase.Load(userAdditionsPath, PlatformId.SuperNintendo);
            }

            userLibrary = new UserLibrary();

            userLibraryPath = Path.Combine(GetPluginUserDataPath(), "UserLibrary.csv");

            if (!File.Exists(userLibraryPath))
            {
                string bundledTemplate = Path.Combine(pluginFolder, "Database", "UserLibrary.csv");

                if (File.Exists(bundledTemplate))
                {
                    Directory.CreateDirectory(GetPluginUserDataPath());
                    File.Copy(bundledTemplate, userLibraryPath);
                }
            }

            userLibrary.Load(userLibraryPath);
        }


        private void InitializeSlotManager()
        {
            string pluginFolder = Path.GetDirectoryName(GetType().Assembly.Location);

            string noCartridgeCover = Path.Combine(pluginFolder, "Assets", "no_cartridge.png");

            string coverFolder = Path.Combine(GetPluginUserDataPath(), "Covers");

            LibretroThumbnailsClient libretroThumbnails = new LibretroThumbnailsClient();

            string steamGridDbCredentialsPath = Path.Combine(GetPluginUserDataPath(), "SteamGridDbCredentials.csv");

            if (!File.Exists(steamGridDbCredentialsPath))
            {
                string bundledTemplate = Path.Combine(pluginFolder, "Database", "SteamGridDbCredentials.csv");

                if (File.Exists(bundledTemplate))
                {
                    Directory.CreateDirectory(GetPluginUserDataPath());
                    File.Copy(bundledTemplate, steamGridDbCredentialsPath);
                }
            }

            SteamGridDbClient steamGridDb = SteamGridDbClient.LoadFromFile(steamGridDbCredentialsPath);

            slotManager = new GameSlotManager(
                PlayniteApi,
                logger,
                Id,
                new CoverDownloader(coverFolder, libretroThumbnails, steamGridDb),
                new PlatformResolver(PlayniteApi),
                noCartridgeCover
            );

            slotManager.ShowNoCartridge();
        }


        /// <summary>
        /// Rend le configurateur atteignable depuis la tuile elle-même, via
        /// l'action de lancement posée par GameSlotManager sur une cartouche
        /// non configurée. Sans cela, le seul moyen de configurer un jeu est
        /// de retirer puis réinsérer la cartouche pendant la session : une
        /// cartouche déjà présente au démarrage de Playnite, ou dont la
        /// configuration a été annulée une fois, restait inatteignable.
        /// </summary>
        private void InitializeUriHandler()
        {
            PlayniteApi.UriHandler.RegisterSource("cartridgeshelf", args =>
            {
                try
                {
                    // playnite://cartridgeshelf/configure/<checksum>
                    if (args.Arguments == null ||
                        args.Arguments.Length < 2 ||
                        !string.Equals(args.Arguments[0], "configure", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    ConfigureFromUri(args.Arguments[1]);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "CartridgeShelf : erreur pendant le traitement de l'URI.");
                }
            });
        }


        /// <summary>
        /// Configure la cartouche désignée par son checksum, puis rafraîchit
        /// la tuile pour qu'elle devienne immédiatement jouable.
        /// </summary>
        private void ConfigureFromUri(string checksum)
        {
            CartridgeEntry entry = cartridgeDatabase.Find(checksum);

            if (entry == null)
            {
                logger.Info($"CartridgeShelf : URI de configuration pour un checksum inconnu ({checksum}).");

                return;
            }

            GameIdentity identity = new GameIdentity
            {
                Checksum = checksum,
                Platform = string.Equals(checksum, CartridgeState.CurrentChecksum, StringComparison.OrdinalIgnoreCase)
                    ? CartridgeState.CurrentPlatform
                    : PlatformId.SuperNintendo
            };

            // Une annulation precedente ne doit pas bloquer une demande
            // explicite de l'utilisateur.
            declinedEmulatorSetup.Remove(checksum);

            // Meme precaution que pour le prompt d'insertion : les dialogues
            // exigent le thread UI.
            RunOnUiThread(() =>
            {
                UserLibraryEntry result = PromptForEmulatorSetup(identity, entry.Title);

                if (result == null)
                {
                    declinedEmulatorSetup.Add(checksum);
                }

                slotManager.ShowGame(identity, entry, result);
            });
        }


        private void InitializeWatcher()
        {
            // TODO : gérer plusieurs détecteurs si un jour plusieurs lecteurs
            // de cartouches sont supportés simultanément. Pour l'instant,
            // un seul watcher sur le premier détecteur disponible.
            ICartridgeDetector detector = detectors.Count > 0 ? detectors[0] : null;

            if (detector == null)
            {
                logger.Info("CartridgeShelf : aucun détecteur enregistré.");

                return;
            }

            watcher = new CartridgeWatcher(
                detector,
                onCartridgeInserted: OnCartridgeInserted,
                onCartridgeRemoved: OnCartridgeRemoved
            );

            TryStartWatcher();
        }


        /// <summary>
        /// Tente de démarrer le watcher. Un échec au tout premier essai
        /// (device pas encore énuméré par Windows au démarrage à froid,
        /// ou Playback pas encore fermé) ne doit PAS désactiver la
        /// détection pour le reste de la session -- sans quoi une
        /// cartouche déjà insérée au lancement de Playnite (notamment en
        /// plein écran, lancé directement) reste bloquée sur
        /// no_cartridge indéfiniment, faute de Timer de poll jamais créé.
        /// On réessaie donc périodiquement tant que ça échoue.
        /// </summary>
        private void TryStartWatcher()
        {
            bool deviceAvailable = watcher.Start();

            if (!deviceAvailable)
            {
                logger.Info(
                    $"CartridgeShelf : lecteur non disponible pour l'instant, nouvelle tentative dans {WatcherStartRetryDelayMs}ms."
                );

                watcherStartRetryTimer?.Dispose();
                watcherStartRetryTimer = new Timer(
                    _ => TryStartWatcher(),
                    null,
                    WatcherStartRetryDelayMs,
                    Timeout.Infinite
                );

                return;
            }

            watcherStartRetryTimer?.Dispose();
            watcherStartRetryTimer = null;

            watcher.CheckNow();
        }


        private void OnCartridgeRemoved()
        {
            try
            {
                isStartupCheck = false;

                CartridgeState.Clear();

                RunOnUiThread(() => slotManager.ShowNoCartridge());
            }
            catch (Exception ex)
            {
                logger.Error(ex, "CartridgeShelf : erreur pendant le traitement du retrait de cartouche.");
            }
        }


        /// <summary>
        /// Le watcher (CartridgeWatcher) poll sur un thread d'arrière-plan
        /// (System.Threading.Timer), pas sur le thread UI. Les mises à jour
        /// visuelles du jeu (Name, CoverImage) via GameSlotManager doivent
        /// donc être marshalées sur le thread UI -- sans ça, l'écriture en
        /// base réussit silencieusement (d'où les logs "affichage de ..."
        /// sans erreur) mais l'UI (notamment le rendu Fullscreen) peut ne
        /// jamais refléter le changement.
        /// </summary>
        private void RunOnUiThread(Action action)
        {
            Dispatcher dispatcher = Application.Current?.Dispatcher;

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();

                return;
            }

            dispatcher.Invoke(action);
        }


        private void OnCartridgeInserted(CartridgeInfo info)
        {
            try
            {
                bool isStartup = isStartupCheck;
                isStartupCheck = false;

                ICartridgeDetector detector = detectors.Count > 0 ? detectors[0] : null;

                if (detector == null)
                {
                    return;
                }

                GameIdentity identity = detector.ToIdentity(info);

                if (string.IsNullOrEmpty(identity.Checksum))
                {
                    logger.Info("CartridgeShelf : cartouche présente mais checksum introuvable.");

                    return;
                }

                CartridgeEntry entry = cartridgeDatabase.Find(identity.Checksum);

                if (entry == null)
                {
                    logger.Info($"CartridgeShelf : checksum inconnu de la base : {identity.Checksum}");

                    // Jamais de dialogue interactif lors de la
                    // vérification initiale au démarrage -- on affiche
                    // juste "non identifiée", l'utilisateur pourra la
                    // retirer/réinsérer plus tard pendant la session pour
                    // déclencher le prompt normalement.
                    entry = isStartup ? null : PromptForNewGame(identity);

                    if (entry == null)
                    {
                        RunOnUiThread(() => slotManager.ShowUnknownCartridge(identity));

                        return;
                    }
                }

                UserLibraryEntry userEntry = userLibrary.Find(identity.Checksum);

                if (userEntry == null && !isStartup && !declinedEmulatorSetup.Contains(identity.Checksum))
                {
                    // Important : les dialogues WPF (ShowMessage, SelectFile)
                    // exigent le thread UI (STA). OnCartridgeInserted tourne
                    // sur le thread d'arrière-plan du watcher -- sans ce
                    // marshaling, les dialogues échouent silencieusement
                    // (pas d'exception, retour immédiat comme si annulés).
                    RunOnUiThread(() =>
                    {
                        UserLibraryEntry promptResult = PromptForEmulatorSetup(identity, entry.Title);

                        if (promptResult == null)
                        {
                            // L'utilisateur a annulé l'un des sélecteurs de
                            // fichier -- on ne redemande plus pour cette
                            // cartouche pendant le reste de la session, pour
                            // ne pas harceler à chaque insertion.
                            declinedEmulatorSetup.Add(identity.Checksum);
                        }

                        CartridgeState.CartridgeInserted = true;
                        CartridgeState.CurrentTitle = entry.Title;
                        CartridgeState.CurrentChecksum = identity.Checksum;
                        CartridgeState.CurrentPlatform = identity.Platform;

                        slotManager.ShowGame(identity, entry, promptResult);
                    });

                    return;
                }

                CartridgeState.CartridgeInserted = true;
                CartridgeState.CurrentTitle = entry.Title;
                CartridgeState.CurrentChecksum = identity.Checksum;
                CartridgeState.CurrentPlatform = identity.Platform;

                RunOnUiThread(() => slotManager.ShowGame(identity, entry, userEntry));
            }
            catch (Exception ex)
            {
                logger.Error(ex, "CartridgeShelf : erreur pendant le traitement de la cartouche insérée.");
            }
        }


        /// <summary>
        /// Demande le nom du jeu à l'utilisateur via une boîte de dialogue
        /// Playnite, l'ajoute en mémoire à la base et le persiste dans
        /// UserSnesAdditions.csv pour les prochains démarrages. Retourne
        /// l'entrée créée, ou null si l'utilisateur annule / laisse vide.
        /// </summary>
        private CartridgeEntry PromptForNewGame(GameIdentity identity)
        {
            try
            {
                var titleResult = PlayniteApi.Dialogs.SelectString(
                    $"Unrecognized cartridge (id {identity.Checksum}). What is the name of this game?",
                    "CartridgeShelf: new cartridge",
                    string.Empty
                );

                if (titleResult == null || !titleResult.Result || string.IsNullOrWhiteSpace(titleResult.SelectedString))
                {
                    return null;
                }

                string title = titleResult.SelectedString.Trim();

                var regionResult = PlayniteApi.Dialogs.SelectString(
                    $"What region is \"{title}\"?",
                    "CartridgeShelf: cartridge region",
                    "Japan"
                );

                string region = (regionResult != null && regionResult.Result)
                    ? regionResult.SelectedString.Trim()
                    : string.Empty;

                cartridgeDatabase.Add(identity.Checksum, title, region, identity.Platform);

                AppendUserAddition(identity.Checksum, title, region);

                logger.Info($"CartridgeShelf : nouveau jeu ajouté par l'utilisateur : {identity.Checksum} = {title} ({region})");

                return cartridgeDatabase.Find(identity.Checksum);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "CartridgeShelf : erreur pendant la saisie du nom du jeu.");

                return null;
            }
        }


        private void AppendUserAddition(string checksum, string title, string region)
        {
            try
            {
                Directory.CreateDirectory(GetPluginUserDataPath());

                bool isNewFile = !File.Exists(userAdditionsPath);

                using (StreamWriter writer = new StreamWriter(userAdditionsPath, append: true, System.Text.Encoding.UTF8))
                {
                    if (isNewFile)
                    {
                        writer.WriteLine("# Jeux ajoutés depuis Playnite (cartouches non reconnues par Snes.csv).");
                        writer.WriteLine("# Format : checksum;titre;tags;region");
                    }

                    writer.WriteLine($"{checksum};{title};;{region}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "CartridgeShelf : erreur pendant l'enregistrement de UserSnesAdditions.csv.");
            }
        }


        /// <summary>
        /// Propose, via une question Oui/Non (ChooseItemWithSearch n'est
        /// disponible qu'en mode Desktop d'après le SDK Playnite -- en
        /// Fullscreen elle ne fonctionne pas, ce qui faisait disparaître
        /// silencieusement cette étape), le choix entre émulateur+ROM et
        /// exécutable autonome. Enregistre le résultat dans
        /// UserLibrary.csv et le retourne, ou null si l'utilisateur
        /// annule l'une des sélections.
        /// </summary>
        private UserLibraryEntry PromptForEmulatorSetup(GameIdentity identity, string title)
        {
            try
            {
                MessageBoxResult choice = PlayniteApi.Dialogs.ShowMessage(
                    $"\"{title}\" was just identified.\n\n"
                    + "Does this game use a separate emulator and ROM file? "
                    + "(Yes = Emulator + ROM, No = single standalone executable / native PC game)",
                    "CartridgeShelf: set up launching",
                    MessageBoxButton.YesNo
                );

                if (choice == MessageBoxResult.Yes)
                {
                    return PromptForEmulatorAndRom(identity, title);
                }

                return PromptForStandaloneExecutable(identity, title);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "CartridgeShelf : erreur pendant la configuration de l'émulateur.");

                return null;
            }
        }


        /// <summary>
        /// Cas des jeux recompilés/homebrews packagés en un seul .exe
        /// autonome (pas d'émulateur ni de ROM séparés). RomPath reste vide
        /// et ArgumentsTemplate est une chaîne vide (pas de jeton {ROM} à
        /// substituer) : le fichier choisi est lancé tel quel, sans argument.
        /// </summary>
        private UserLibraryEntry PromptForStandaloneExecutable(GameIdentity identity, string title)
        {
            PlayniteApi.Dialogs.ShowMessage(
                $"Select the standalone executable for \"{title}\".",
                "CartridgeShelf: standalone executable"
            );

            string exePath = PlayniteApi.Dialogs.SelectFile("Executable|*.exe");

            if (string.IsNullOrWhiteSpace(exePath))
            {
                return null;
            }

            UserLibraryEntry entry = new UserLibraryEntry
            {
                Checksum = identity.Checksum,
                RomPath = string.Empty,
                EmulatorPath = exePath,
                ArgumentsTemplate = string.Empty
            };

            userLibrary.Add(entry);

            AppendUserLibraryEntry(entry);

            logger.Info(
                $"CartridgeShelf : exécutable autonome configuré pour {title} ({identity.Checksum}) -> {exePath}"
            );

            return entry;
        }


        /// <summary>
        /// Cas classique : émulateur séparé + fichier ROM.
        /// </summary>
        private UserLibraryEntry PromptForEmulatorAndRom(GameIdentity identity, string title)
        {
            PlayniteApi.Dialogs.ShowMessage(
                $"To be able to launch \"{title}\" directly from Playnite, please provide:\n"
                + "  1. the SNES emulator to use (its .exe file)\n"
                + "  2. the ROM file for this game on your disk\n\n"
                + "Two file selection windows will open one after another. "
                + "You can cancel (Cancel button) if you don't want to set up launching "
                + "right now -- CartridgeShelf won't ask again for this cartridge.",
                "CartridgeShelf: set up launching"
            );

            string emulatorPath = PlayniteApi.Dialogs.SelectFile("Emulator executable|*.exe");

            if (string.IsNullOrWhiteSpace(emulatorPath))
            {
                return null;
            }

            PlayniteApi.Dialogs.ShowMessage(
                $"Now, select the ROM file for \"{title}\" on your disk.",
                "CartridgeShelf: ROM file"
            );

            string romPath = PlayniteApi.Dialogs.SelectFile(
                "ROM or archive|*.sfc;*.smc;*.fig;*.bin;*.zip;*.rar;*.7z|SNES ROM|*.sfc;*.smc;*.fig;*.bin|Archive|*.zip;*.rar;*.7z|All files|*.*"
            );

            if (string.IsNullOrWhiteSpace(romPath))
            {
                return null;
            }

            UserLibraryEntry entry = new UserLibraryEntry
            {
                Checksum = identity.Checksum,
                RomPath = romPath,
                EmulatorPath = emulatorPath,
                ArgumentsTemplate = "{ROM}"
            };

            userLibrary.Add(entry);

            AppendUserLibraryEntry(entry);

            logger.Info(
                $"CartridgeShelf : émulateur configuré pour {title} ({identity.Checksum}) -> {emulatorPath}"
            );

            return entry;
        }


        private void AppendUserLibraryEntry(UserLibraryEntry entry)
        {
            try
            {
                Directory.CreateDirectory(GetPluginUserDataPath());

                bool isNewFile = !File.Exists(userLibraryPath);

                using (StreamWriter writer = new StreamWriter(userLibraryPath, append: true, System.Text.Encoding.UTF8))
                {
                    if (isNewFile)
                    {
                        writer.WriteLine(
                            "# Format : checksum;chemin_rom;chemin_emulateur;arguments (optionnel, {ROM} = chemin de la rom)"
                        );
                    }

                    writer.WriteLine($"{entry.Checksum};{entry.RomPath};{entry.EmulatorPath};{entry.ArgumentsTemplate}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "CartridgeShelf : erreur pendant l'enregistrement de UserLibrary.csv.");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

using DiscShelf.Analyzer;
using DiscShelf.Core;
using DiscShelf.Database;
using DiscShelf.Detectors;
using DiscShelf.Services;

using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;

namespace DiscShelf
{
    public class DiscShelfPlugin : GenericPlugin
    {
        private readonly ILogger logger;

        private DiscWatcher watcher;

        private Timer watcherStartRetryTimer;

        private const int WatcherStartRetryDelayMs = 2000;

        private DiscDatabase discDatabase;

        private UserLibrary userLibrary;

        private GameSlotManager slotManager;

        private readonly List<IDiscDetector> detectors =
            new List<IDiscDetector>();


        public override Guid Id =>
            Guid.Parse("7c3e2d1a-9f61-4c7d-8b4e-123456789abc");


        public DiscShelfPlugin(IPlayniteAPI api)
            : base(api)
        {
            logger = LogManager.GetLogger();

            logger.Info("DiscShelf : plugin chargé, attente du démarrage complet de Playnite.");
        }


        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);

            try
            {
                InitializeDetectors();
                InitializeDatabases();
                InitializeSlotManager();
                InitializeWatcher();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DiscShelf : erreur pendant l'initialisation.");
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
            // Ajouter ici un détecteur par plateforme supportée
            // (Xbox, GameCube, etc.) au fur et à mesure.
            detectors.Add(new PlaystationDetector());
            detectors.Add(new PlayStation2Detector());
            detectors.Add(new PlayStation3Detector());
            detectors.Add(new NeoGeoCdDetector());
            detectors.Add(new SaturnDetector());
            detectors.Add(new MegaCdDetector());
            detectors.Add(new DreamcastDetector());
        }


        private void InitializeDatabases()
        {
            string pluginFolder = Path.GetDirectoryName(GetType().Assembly.Location);

            discDatabase = new DiscDatabase();

            discDatabase.Load(
                Path.Combine(pluginFolder, "Database", "Playstation.csv"),
                PlatformId.PlayStation
            );

            discDatabase.Load(
                Path.Combine(pluginFolder, "Database", "PlayStation2.csv"),
                PlatformId.PlayStation2,
                ','
            );

            discDatabase.Load(
                Path.Combine(pluginFolder, "Database", "PlayStation3.txt"),
                PlatformId.PlayStation3,
                '='
            );

            discDatabase.Load(
                Path.Combine(pluginFolder, "Database", "NeoGeoCD.csv"),
                PlatformId.NeoGeoCD,
                ','
            );

            discDatabase.Load(
                Path.Combine(pluginFolder, "Database", "Saturn.csv"),
                PlatformId.SegaSaturn,
                ','
            );

            discDatabase.Load(
                Path.Combine(pluginFolder, "Database", "MegaCD.csv"),
                PlatformId.MegaCD,
                ','
            );

            discDatabase.Load(
                Path.Combine(pluginFolder, "Database", "Dreamcast.csv"),
                PlatformId.Dreamcast,
                ','
            );

            userLibrary = new UserLibrary();

            // La bibliothèque perso vit dans le dossier de données utilisateur
            // du plugin (persistant entre mises à jour), pas dans le dossier
            // d'installation.
            string userLibraryPath = Path.Combine(GetPluginUserDataPath(), "UserLibrary.csv");

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

            string noDiscCover = Path.Combine(pluginFolder, "Assets", "no_disc.png");

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
                noDiscCover
            );

            // Affiche tout de suite un état par défaut ; sera corrigé dès
            // que le watcher aura évalué l'état réel du lecteur.
            slotManager.ShowNoDisc();
        }


        private void InitializeWatcher()
        {
            watcher = new DiscWatcher(
                onDiscInserted: OnDiscInserted,
                onDiscRemoved: OnDiscRemoved
            );

            TryStartWatcher();
        }


        /// <summary>
        /// Comme pour CartridgeShelf : un échec au tout premier essai
        /// (lecteur pas encore prêt au démarrage à froid) ne doit pas
        /// désactiver la détection pour le reste de la session. On
        /// réessaie périodiquement tant que ça échoue.
        /// </summary>
        private void TryStartWatcher()
        {
            bool readerAvailable = watcher.Start();

            if (!readerAvailable)
            {
                logger.Info(
                    $"DiscShelf : lecteur non disponible pour l'instant, nouvelle tentative dans {WatcherStartRetryDelayMs}ms."
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

            // Évalue immédiatement l'état réel (disque déjà présent ou non)
            // sans attendre le premier passage du timer.
            watcher.CheckNow();
        }


        private void OnDiscRemoved()
        {
            try
            {
                DiscState.Clear();

                RunOnUiThread(() => slotManager.ShowNoDisc());
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DiscShelf : erreur pendant le traitement du retrait de disque.");
            }
        }


        /// <summary>
        /// Le watcher poll sur un thread d'arrière-plan, pas sur le
        /// thread UI. Les mises à jour visuelles du jeu (via
        /// GameSlotManager) doivent être marshalées sur le thread UI --
        /// sans ça, l'écriture en base peut réussir silencieusement sans
        /// que le rendu Fullscreen ne s'actualise (bug identifié et
        /// corrigé sur CartridgeShelf, même architecture).
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


        private void OnDiscInserted()
        {
            try
            {
                string drivePath = watcher.CurrentDrive + "\\";

                DiscAnalyzer analyzer = new DiscAnalyzer();

                DiscInfo disc = analyzer.Analyze(drivePath);

                if (disc.Files.Count == 0)
                {
                    logger.Info("DiscShelf : disque signalé présent mais illisible pour l'instant.");

                    return;
                }

                IDiscDetector detector = detectors.Find(d => d.CanHandle(disc));

                if (detector == null)
                {
                    logger.Info("DiscShelf : aucun détecteur compatible avec ce disque.");

                    DiscState.Clear();

                    RunOnUiThread(() => slotManager.ShowNoDisc());

                    return;
                }

                GameIdentity identity = detector.Detect(disc);

                if (string.IsNullOrEmpty(identity.Serial))
                {
                    logger.Info("DiscShelf : détecteur compatible mais serial introuvable.");

                    RunOnUiThread(() => slotManager.ShowUnknownDisc(identity));

                    return;
                }

                DiscEntry entry;

                if (identity.Platform == PlatformId.NeoGeoCD)
                {
                    if (identity.Serial.StartsWith("DATE:", StringComparison.OrdinalIgnoreCase))
                    {
                        string dateOnly = identity.Serial.Substring(5);

                        entry = discDatabase.FindByDatePrefix(dateOnly, identity.Platform);
                    }
                    else
                    {
                        entry = discDatabase.FindByLabelSuffix(identity.Serial, identity.Platform);
                    }
                }
                else
                {
                    entry = discDatabase.Find(identity.Serial);

                    if (entry == null && identity.Platform == PlatformId.Dreamcast)
                    {
                        entry = discDatabase.FindStrippingPublisherPrefix(identity.Serial, identity.Platform);
                    }
                }

                if (entry == null)
                {
                    logger.Info($"DiscShelf : serial inconnu de la base : {identity.Serial}");

                    RunOnUiThread(() => slotManager.ShowUnknownDisc(identity));

                    return;
                }

                UserLibraryEntry userEntry = userLibrary.Find(identity.Serial);

                DiscState.DiscInserted = true;
                DiscState.CurrentTitle = entry.Title;
                DiscState.CurrentSerial = identity.Serial;
                DiscState.CurrentPlatform = identity.Platform;

                RunOnUiThread(() => slotManager.ShowGame(identity, entry, userEntry));
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DiscShelf : erreur pendant l'analyse du disque inséré.");
            }
        }
    }
}

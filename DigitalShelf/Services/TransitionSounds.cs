using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

using Playnite.SDK;
using Playnite.SDK.Data;

namespace DigitalShelf.Services
{
    /// <summary>
    /// Sons joués de part et d'autre du changement de vue : un son à l'appui
    /// sur le bouton, un autre à l'apparition de la page d'arrivée.
    ///
    /// Les fichiers vivent dans le dossier audio du thème Fullscreen actif,
    /// pas dans le plugin : ils appartiennent à l'habillage, et l'utilisateur
    /// doit pouvoir les remplacer sans toucher au plugin.
    /// </summary>
    public class TransitionSounds
    {
        /// <summary>
        /// Son de l'appui sur le bouton, identique dans les deux sens de
        /// bascule.
        /// </summary>
        public const string PressSoundFile = "Main Menu FX 1.wav";

        /// <summary>
        /// Son de l'apparition de la page d'arrivée, lui aussi identique dans
        /// les deux sens : les sons marquent les deux temps de la transition,
        /// pas la destination.
        /// </summary>
        public const string OpenSoundFile = "Main Menu FX 2.wav";

        /// <summary>
        /// Son de déplacement d'une jaquette à l'autre. Repris à l'extension
        /// Playnite Sounds, dont les sons d'interface ont été désactivés parce
        /// qu'ils se superposaient à ceux de la transition. On utilise le WAV
        /// déjà présent dans le thème plutôt que navigation.mp3 : un son court
        /// et fréquent doit être préchargé en mémoire, sans décodage.
        /// </summary>
        public const string NavigationSoundFile = "navigation.wav";

        /// <summary>
        /// Fenêtre pendant laquelle le son de navigation est ignoré. Un
        /// changement de vue déplace la sélection, ce qui déclencherait un son
        /// de navigation par-dessus ceux de la transition -- précisément la
        /// superposition qu'on cherche à supprimer.
        /// </summary>
        private DateTime navigationMutedUntil = DateTime.MinValue;

        /// <summary>
        /// Durée du silence couvrant le démarrage de Playnite, pendant lequel
        /// la sélection se met en place sans que l'utilisateur ait navigué.
        /// </summary>
        private const int StartupMuteMs = 5000;

        /// <summary>
        /// Fichier de réglage, à la racine du plugin, permettant de changer
        /// les trois sons sans recompiler. Trouver le bon son relève de
        /// l'essai à l'oreille : autant que ce soit une ligne à éditer plutôt
        /// qu'un cycle de compilation.
        /// </summary>
        public const string SettingsFileName = "sounds.txt";

        private readonly ILogger logger;

        private readonly string pluginFolder;

        private string navigationFile = NavigationSoundFile;

        private string pressFile = PressSoundFile;

        private string openFile = OpenSoundFile;

        private readonly Dictionary<string, SoundPlayer> players =
            new Dictionary<string, SoundPlayer>(StringComparer.OrdinalIgnoreCase);

        private readonly string audioFolder;

        private readonly IPlayniteAPI api;


        public TransitionSounds(IPlayniteAPI api, ILogger logger, string pluginFolder)
        {
            this.api = api;
            this.logger = logger;
            this.pluginFolder = pluginFolder;

            audioFolder = ResolveThemeAudioFolder(api);

            // Le silence de démarrage est armé ici, à la construction du
            // plugin, et non dans OnApplicationStarted : Playnite pose sa
            // première sélection avant cet événement, ce qui laissait passer
            // un son de navigation isolé au lancement.
            MuteNavigationFor(StartupMuteMs);

            LoadSettings();

            Preload();
        }


        /// <summary>
        /// Lit les éventuels remplacements dans sounds.txt. Format « clé =
        /// fichier », une par ligne ; toute clé absente garde sa valeur par
        /// défaut.
        /// </summary>
        private void LoadSettings()
        {
            string path = Path.Combine(pluginFolder, SettingsFileName);

            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    string trimmed = line.Trim();

                    if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                    {
                        continue;
                    }

                    string[] parts = trimmed.Split(new[] { '=' }, 2);

                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    if (value.Length == 0)
                    {
                        continue;
                    }

                    if (key.Equals("navigation", StringComparison.OrdinalIgnoreCase))
                    {
                        navigationFile = value;
                    }
                    else if (key.Equals("press", StringComparison.OrdinalIgnoreCase))
                    {
                        pressFile = value;
                    }
                    else if (key.Equals("open", StringComparison.OrdinalIgnoreCase))
                    {
                        openFile = value;
                    }
                }

                logger.Info(
                    $"DigitalShelf : sons réglés sur navigation={navigationFile}, press={pressFile}, open={openFile}."
                );
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DigitalShelf : lecture de sounds.txt impossible, valeurs par défaut conservées.");
            }
        }


        /// <summary>
        /// Son de l'appui sur le bouton, quelle que soit la vue quittée.
        /// </summary>
        public void PlayPress()
        {
            Play(pressFile);
        }


        /// <summary>
        /// Son de l'apparition de la page d'arrivée, quelle que soit la vue
        /// atteinte.
        /// </summary>
        public void PlayOpen()
        {
            Play(openFile);
        }


        /// <summary>
        /// Son de déplacement dans la rangée de jaquettes, ignoré pendant une
        /// transition et hors du mode Fullscreen.
        /// </summary>
        public void PlayNavigation()
        {
            if (DateTime.UtcNow < navigationMutedUntil)
            {
                return;
            }

            // Ce son appartient à l'habillage Fullscreen : le jouer en mode
            // Desktop n'a pas de sens, l'interface y ayant sa propre logique
            // sonore. OnGameSelected, lui, se déclenche dans les deux modes.
            if (!IsFullscreen)
            {
                return;
            }

            Play(navigationFile);
        }


        private bool IsFullscreen
        {
            get
            {
                try
                {
                    return api?.ApplicationInfo != null &&
                           api.ApplicationInfo.Mode == ApplicationMode.Fullscreen;
                }
                catch
                {
                    return false;
                }
            }
        }


        /// <summary>
        /// Ignore les sons de navigation pendant la durée indiquée. Appelé au
        /// début d'une bascule et au démarrage de Playnite, deux moments où la
        /// sélection bouge sans que l'utilisateur n'ait navigué.
        /// </summary>
        public void MuteNavigationFor(int milliseconds)
        {
            navigationMutedUntil = DateTime.UtcNow.AddMilliseconds(milliseconds);
        }


        private void Play(string fileName)
        {
            try
            {
                if (players.TryGetValue(fileName, out SoundPlayer player) && player != null)
                {
                    player.Play();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"DigitalShelf : lecture de {fileName} impossible.");
            }
        }


        /// <summary>
        /// Charge les deux sons en mémoire au démarrage. Sans ce préchargement,
        /// la première lecture attendrait un accès disque, précisément au
        /// moment où le son doit répondre à l'appui sur le bouton.
        /// </summary>
        private void Preload()
        {
            if (string.IsNullOrEmpty(audioFolder))
            {
                return;
            }

            foreach (string fileName in new[] { pressFile, openFile, navigationFile })
            {
                string path = Path.Combine(audioFolder, fileName);

                if (!File.Exists(path))
                {
                    logger.Warn($"DigitalShelf : son introuvable ({path}), la bascule restera silencieuse.");

                    continue;
                }

                try
                {
                    SoundPlayer player = new SoundPlayer(path);

                    player.Load();

                    players[fileName] = player;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"DigitalShelf : chargement de {fileName} impossible.");
                }
            }

            logger.Info($"DigitalShelf : {players.Count} son(s) de transition chargé(s) depuis {audioFolder}.");
        }


        /// <summary>
        /// Retrouve le dossier audio du thème Fullscreen actif en lisant
        /// l'identifiant de thème dans fullscreenConfig.json, plutôt que de
        /// coder un chemin en dur : un changement de thème est ainsi suivi
        /// automatiquement.
        /// </summary>
        private string ResolveThemeAudioFolder(IPlayniteAPI api)
        {
            try
            {
                string configPath = Path.Combine(api.Paths.ConfigurationPath, "fullscreenConfig.json");

                if (!File.Exists(configPath))
                {
                    logger.Warn("DigitalShelf : fullscreenConfig.json introuvable, sons désactivés.");

                    return null;
                }

                if (!Serialization.TryFromJsonFile(configPath, out FullscreenConfigTheme config) ||
                    config == null ||
                    string.IsNullOrEmpty(config.Theme))
                {
                    logger.Warn("DigitalShelf : thème Fullscreen non identifiable, sons désactivés.");

                    return null;
                }

                return Path.Combine(
                    api.Paths.ConfigurationPath, "Themes", "Fullscreen", config.Theme, "audio"
                );
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DigitalShelf : dossier audio du thème introuvable.");

                return null;
            }
        }


        /// <summary>
        /// Seul champ de fullscreenConfig.json qui nous intéresse.
        /// </summary>
        private class FullscreenConfigTheme
        {
            public string Theme { get; set; }
        }
    }
}

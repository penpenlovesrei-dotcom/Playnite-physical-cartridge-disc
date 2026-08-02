using System;
using System.IO;
using System.Net;

using DiscShelf.Core;

using Playnite.SDK;

namespace DiscShelf.Services
{
    public class CoverDownloader
    {
        private readonly ILogger logger;

        private readonly string coverFolder;

        private readonly LibretroThumbnailsClient libretroThumbnails;

        private readonly SteamGridDbClient steamGridDb;


        public CoverDownloader(
            string coverFolder,
            LibretroThumbnailsClient libretroThumbnails,
            SteamGridDbClient steamGridDb)
        {
            this.coverFolder = coverFolder;
            this.libretroThumbnails = libretroThumbnails;
            this.steamGridDb = steamGridDb;

            logger = LogManager.GetLogger();

            if (!Directory.Exists(this.coverFolder))
            {
                Directory.CreateDirectory(this.coverFolder);
            }
        }


        /// <summary>
        /// Retourne le chemin local de la jaquette pour ce serial, en la
        /// téléchargeant si besoin. Ordre d'essai :
        ///   1. libretro-thumbnails par titre exact (gratuit, aucune
        ///      authentification, mais matching moins précis qu'un vrai
        ///      serial -- dépend de la correspondance entre notre titre
        ///      et la convention No-Intro/Redump utilisée par le dépôt).
        ///   2. SteamGridDB par nom en dernier repli (aucune notion de
        ///      plateforme, risque de faux positifs entre consoles).
        /// Retourne null si aucune jaquette n'a pu être obtenue.
        /// </summary>
        public string GetCover(string serial, PlatformId platform, string title)
        {
            if (string.IsNullOrWhiteSpace(serial))
            {
                return null;
            }

            string safeName = MakeSafeFileName(serial);

            string localFile = Path.Combine(coverFolder, safeName + ".jpg");

            if (File.Exists(localFile))
            {
                return localFile;
            }

            string remoteUrl = TryLibretroThumbnails(title, platform);

            if (remoteUrl == null)
            {
                remoteUrl = TrySteamGridDb(title, platform);
            }

            if (string.IsNullOrEmpty(remoteUrl))
            {
                return null;
            }

            try
            {
                using (WebClient client = new WebClient())
                {
                    client.DownloadFile(remoteUrl, localFile);
                }

                logger.Info($"CoverDownloader : jaquette téléchargée pour {serial}.");

                return localFile;
            }
            catch (Exception ex)
            {
                logger.Info(
                    $"CoverDownloader : téléchargement de la jaquette échoué pour {serial} ({ex.Message})."
                );

                return null;
            }
        }


        private string TryLibretroThumbnails(string title, PlatformId platform)
        {
            if (libretroThumbnails == null || string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            string searchTitle = CleanTitleForSearch(title, platform);

            return libretroThumbnails.GetBoxArtUrl(searchTitle, platform);
        }


        private string TrySteamGridDb(string title, PlatformId platform)
        {
            if (steamGridDb == null || !steamGridDb.IsConfigured || string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            // Note : SteamGridDB n'a aucune notion de plateforme/console --
            // tout y est mélangé (PC, consoles, remasters...), contrairement à
            // libretro-thumbnails (essayé en priorité juste avant, borné par
            // dépôt/système). Les faux positifs entre plateformes ici sont donc
            // une limite structurelle de cette source, pas un bug.
            string searchTitle = CleanTitleForSearch(title, platform);

            logger.Info($"CoverDownloader : repli sur SteamGridDB pour \"{searchTitle}\".");

            return steamGridDb.GetCoverUrlByName(searchTitle);
        }


        /// <summary>
        /// Beaucoup de titres (No-Intro/GoodTools) contiennent une ou plusieurs
        /// variantes après "~" (ex. "Dunk Dream ~ Street Slam ~ Street Hoop").
        /// On ne tronque que pour NeoGeoCD, en prenant la partie APRÈS le
        /// DERNIER "~" -- c'est généralement le titre occidental le plus
        /// connu (ex. "Real Bout Fatal Fury 2" plutôt que "Real Bout Garou
        /// Densetsu 2"), donc le plus susceptible d'être trouvé par les
        /// sources de jaquettes. Sur PS1, pas de troncature : certains
        /// titres utilisent aussi "~" mais chaque partie reste pertinente
        /// pour la recherche (ex. "Puzzle Bobble ~ Bust-a-Move").
        /// </summary>
        private static string CleanTitleForSearch(string title, PlatformId platform)
        {
            if (platform != PlatformId.NeoGeoCD)
            {
                return title.Trim();
            }

            string[] parts = title.Split('~');

            return parts[parts.Length - 1].Trim();
        }


        private static string MakeSafeFileName(string serial)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                serial = serial.Replace(c, '_');
            }

            return serial;
        }
    }
}

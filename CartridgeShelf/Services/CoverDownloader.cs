using System;
using System.IO;
using System.Net;

using CartridgeShelf.Core;

using Playnite.SDK;

namespace CartridgeShelf.Services
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
        /// Retourne le chemin local de la jaquette pour ce checksum, en la
        /// téléchargeant si besoin. Essaie d'abord libretro-thumbnails
        /// (gratuit, aucune authentification, mais matching par titre exact
        /// -- moins fiable ici que sur DiscShelf car Snes.csv n'est pas en
        /// convention No-Intro), puis SteamGridDB en repli. Retourne null
        /// si aucune jaquette n'a pu être obtenue.
        /// </summary>
        public string GetCover(string checksum, PlatformId platform, string title, string region)
        {
            if (string.IsNullOrWhiteSpace(checksum))
            {
                return null;
            }

            string safeName = MakeSafeFileName(checksum);

            string localFile = Path.Combine(coverFolder, safeName + ".jpg");

            if (File.Exists(localFile))
            {
                return localFile;
            }

            string cleanTitle = CleanTitleForSearch(title);

            string remoteUrl = TryLibretroThumbnails(cleanTitle, platform, region);

            if (remoteUrl == null)
            {
                remoteUrl = TrySteamGridDb(cleanTitle, platform);
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

                logger.Info($"CoverDownloader : jaquette téléchargée pour {checksum}.");

                return localFile;
            }
            catch (Exception ex)
            {
                logger.Info(
                    $"CoverDownloader : téléchargement de la jaquette échoué pour {checksum} ({ex.Message})."
                );

                return null;
            }
        }


        private string TryLibretroThumbnails(string title, PlatformId platform, string region)
        {
            if (libretroThumbnails == null || string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            // libretro-thumbnails nomme ses fichiers "Titre (Region)" en
            // anglais (convention No-Intro) -- Snes.csv stocke la région en
            // français ("Japon", etc.), d'où la traduction.
            string englishRegion = TranslateRegion(region);

            string searchTitle = string.IsNullOrEmpty(englishRegion)
                ? title
                : $"{title} ({englishRegion})";

            return libretroThumbnails.GetBoxArtUrl(searchTitle, platform);
        }


        private static string TranslateRegion(string region)
        {
            if (string.IsNullOrWhiteSpace(region))
            {
                return null;
            }

            switch (region.Trim().ToLowerInvariant())
            {
                case "japon": return "Japan";
                case "usa": return "USA";
                case "europe": return "Europe";
                default: return region.Trim();
            }
        }


        private string TrySteamGridDb(string title, PlatformId platform)
        {
            if (steamGridDb == null || !steamGridDb.IsConfigured || string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            // SteamGridDB n'a pas de filtre plateforme dans notre recherche
            // par nom -- on l'ajoute au terme de recherche pour réduire les
            // faux positifs (remakes, portages, jeux au nom proche).
            string query = $"{title} {PlatformSearchSuffix(platform)}".Trim();

            logger.Info($"CoverDownloader : repli sur SteamGridDB pour \"{query}\".");

            return steamGridDb.GetCoverUrlByName(query);
        }


        /// <summary>
        /// Retire les mentions de région courantes ("USA", "Japan", "Europe",
        /// avec ou sans ":") n'importe où dans le titre avant recherche --
        /// Snes.csv les garde pour l'affichage (ex. "Final Fantasy USA:
        /// Mystic Quest"), mais elles nuisent à la recherche par nom sur les
        /// API de jaquettes, qui indexent généralement le titre commercial
        /// sans cette mention.
        /// </summary>
        private static string CleanTitleForSearch(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            string cleaned = System.Text.RegularExpressions.Regex.Replace(
                title,
                @"\b(USA|Japan|Europe)\b:?",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s{2,}", " ");
            cleaned = cleaned.Trim(' ', ':', '-');

            return cleaned;
        }


        private static string PlatformSearchSuffix(PlatformId platform)
        {
            switch (platform)
            {
                case PlatformId.SuperNintendo: return "SNES";
                default: return string.Empty;
            }
        }


        private static string MakeSafeFileName(string checksum)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                checksum = checksum.Replace(c, '_');
            }

            return checksum;
        }
    }
}

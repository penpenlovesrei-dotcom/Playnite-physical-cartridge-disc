using System;
using System.Net;

using CartridgeShelf.Core;

using Playnite.SDK;

namespace CartridgeShelf.Services
{
    /// <summary>
    /// Récupère des jaquettes depuis le dépôt GitHub public
    /// libretro-thumbnails (utilisé par RetroArch), classé par système et
    /// par titre exact (convention No-Intro). Aucune clé, aucun compte --
    /// accès direct en HTTP.
    ///
    /// Contrairement à ScreenScraper/SteamGridDB il n'y a pas de véritable
    /// API de recherche ici : on construit l'URL attendue à partir du
    /// titre et on vérifie son existence avec une requête HEAD avant de la
    /// retourner, pour permettre à CoverDownloader de retomber sur
    /// SteamGridDB si le titre ne matche pas exactement.
    ///
    /// Note : contrairement à DiscShelf (titres déjà en convention
    /// redump/No-Intro), les titres de Snes.csv sont saisis à la main et
    /// ne suivent pas forcément le nommage exact attendu ici -- le taux de
    /// correspondance sera donc probablement plus faible.
    /// </summary>
    public class LibretroThumbnailsClient
    {
        private const string BaseUrl = "https://raw.githubusercontent.com/libretro-thumbnails";

        private readonly ILogger logger;


        public LibretroThumbnailsClient()
        {
            logger = LogManager.GetLogger();
        }


        public string GetBoxArtUrl(string title, PlatformId platform)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            string repo = LibretroThumbnailSystems.ToRepoName(platform);

            if (repo == null)
            {
                return null;
            }

            string fileName = SanitizeTitle(title) + ".png";

            string url = $"{BaseUrl}/{repo}/master/Named_Boxarts/{Uri.EscapeDataString(fileName)}";

            if (!UrlExists(url))
            {
                logger.Info($"LibretroThumbnails : pas de jaquette pour \"{title}\" sur {repo}.");

                return null;
            }

            return url;
        }


        /// <summary>
        /// Caractères interdits par la convention de nommage
        /// libretro-thumbnails, à remplacer par "_" dans le nom de
        /// fichier (voir README du dépôt).
        /// </summary>
        private static string SanitizeTitle(string title)
        {
            char[] forbidden = { '&', '*', '/', ':', '`', '<', '>', '?', '\\', '|', '"' };

            title = title.Trim();

            foreach (char c in forbidden)
            {
                title = title.Replace(c, '_');
            }

            return title;
        }


        private bool UrlExists(string url)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "HEAD";
                request.Timeout = 5000;
                request.UserAgent = "CartridgeShelf-Playnite-Plugin";

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    return response.StatusCode == HttpStatusCode.OK;
                }
            }
            catch (WebException)
            {
                // 404 (jaquette absente pour ce titre) ou problème réseau --
                // dans les deux cas on retombe simplement sur SteamGridDB.
                return false;
            }
            catch (Exception ex)
            {
                logger.Info($"LibretroThumbnails : erreur inattendue pour {url} ({ex.Message}).");

                return false;
            }
        }
    }
}

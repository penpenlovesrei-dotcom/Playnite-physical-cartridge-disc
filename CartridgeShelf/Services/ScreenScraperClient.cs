using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Script.Serialization;

using Playnite.SDK;

namespace CartridgeShelf.Services
{
    /// <summary>
    /// Client minimal pour l'API ScreenScraper.fr. Copié depuis DiscShelf ;
    /// la recherche par serial (serialnum1) n'a pas de sens pour les
    /// cartouches SN Operator (pas de serial exact, juste un checksum
    /// interne à Epilogue) -- CoverDownloader n'appelle donc pas encore
    /// GetBoxArtUrl ici. Conservé tel quel pour référence / adaptation
    /// future (recherche par nom via romnom=, à vérifier dans la doc API).
    /// </summary>
    public class ScreenScraperClient
    {
        private const string BaseUrl = "https://www.screenscraper.fr/api2/jeuInfos.php";

        private readonly ILogger logger;

        private readonly string devId;
        private readonly string devPassword;
        private readonly string userId;
        private readonly string userPassword;
        private readonly string softwareName;


        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(devId) && !string.IsNullOrWhiteSpace(devPassword);


        public ScreenScraperClient(
            string devId,
            string devPassword,
            string userId,
            string userPassword,
            string softwareName = "CartridgeShelf")
        {
            this.devId = devId;
            this.devPassword = devPassword;
            this.userId = userId;
            this.userPassword = userPassword;
            this.softwareName = softwareName;

            logger = LogManager.GetLogger();
        }


        public static ScreenScraperClient LoadFromFile(string filePath)
        {
            ILogger logger = LogManager.GetLogger();

            if (!System.IO.File.Exists(filePath))
            {
                logger.Info(
                    $"ScreenScraperClient : fichier d'identifiants absent ({filePath}), jaquettes désactivées."
                );

                return new ScreenScraperClient(null, null, null, null);
            }

            foreach (string line in System.IO.File.ReadAllLines(filePath))
            {
                string trimmed = line.Trim();

                if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                {
                    continue;
                }

                string[] parts = trimmed.Split(';');

                string devId = parts.Length > 0 ? parts[0].Trim() : null;
                string devPassword = parts.Length > 1 ? parts[1].Trim() : null;
                string userId = parts.Length > 2 ? parts[2].Trim() : null;
                string userPassword = parts.Length > 3 ? parts[3].Trim() : null;

                return new ScreenScraperClient(devId, devPassword, userId, userPassword);
            }

            logger.Info(
                $"ScreenScraperClient : aucune ligne d'identifiants valide dans {filePath}, jaquettes désactivées."
            );

            return new ScreenScraperClient(null, null, null, null);
        }


        /// <summary>
        /// Recherche par nom de jeu (paramètre romnom= de l'API). C'est la
        /// voie principale pour les cartouches SN Operator : on n'a pas de
        /// serial exact comme pour les disques, juste le titre résolu via
        /// Snes.csv/UserSnesAdditions.csv. Le paramètre region (ex. "Japon")
        /// sert à privilégier la jaquette de cette région parmi les résultats,
        /// plutôt que de se rabattre par défaut sur la version "world".
        /// </summary>
        public string GetBoxArtUrlByName(string title, int systemeId, string region)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            string json = null;

            try
            {
                string url = BuildUrlByName(title, systemeId);

                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("User-Agent", softwareName);

                    json = client.DownloadString(url);
                }
            }
            catch (WebException ex)
            {
                logger.Info(
                    $"ScreenScraperClient : requête échouée pour \"{title}\" ({ex.Message})."
                );

                return null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "ScreenScraperClient : erreur inattendue pendant la requête.");

                return null;
            }

            return ExtractBoxArtUrl(json, title, MapRegionToCode(region));
        }


        /// <summary>
        /// Recherche par serial -- conservé de DiscShelf, utile si une
        /// plateforme cartouche future expose un vrai serial exploitable.
        /// </summary>
        public string GetBoxArtUrl(string serial, int systemeId)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(serial))
            {
                return null;
            }

            string json = null;

            try
            {
                string url = BuildUrl(serial, systemeId);

                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("User-Agent", softwareName);

                    json = client.DownloadString(url);
                }
            }
            catch (WebException ex)
            {
                logger.Info(
                    $"ScreenScraperClient : requête échouée pour {serial} ({ex.Message})."
                );

                return null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "ScreenScraperClient : erreur inattendue pendant la requête.");

                return null;
            }

            return ExtractBoxArtUrl(json, serial, null);
        }


        private string BuildUrl(string serial, int systemeId)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.Append(BaseUrl);
            sb.Append("?output=json");
            sb.Append("&devid=").Append(Uri.EscapeDataString(devId));
            sb.Append("&devpassword=").Append(Uri.EscapeDataString(devPassword));
            sb.Append("&softname=").Append(Uri.EscapeDataString(softwareName));

            if (!string.IsNullOrWhiteSpace(userId))
            {
                sb.Append("&ssid=").Append(Uri.EscapeDataString(userId));
                sb.Append("&sspassword=").Append(Uri.EscapeDataString(userPassword));
            }

            sb.Append("&systemeid=").Append(systemeId);
            sb.Append("&serialnum1=").Append(Uri.EscapeDataString(serial));

            return sb.ToString();
        }


        private string BuildUrlByName(string title, int systemeId)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.Append(BaseUrl);
            sb.Append("?output=json");
            sb.Append("&devid=").Append(Uri.EscapeDataString(devId));
            sb.Append("&devpassword=").Append(Uri.EscapeDataString(devPassword));
            sb.Append("&softname=").Append(Uri.EscapeDataString(softwareName));

            if (!string.IsNullOrWhiteSpace(userId))
            {
                sb.Append("&ssid=").Append(Uri.EscapeDataString(userId));
                sb.Append("&sspassword=").Append(Uri.EscapeDataString(userPassword));
            }

            sb.Append("&systemeid=").Append(systemeId);
            sb.Append("&romnom=").Append(Uri.EscapeDataString(title));

            return sb.ToString();
        }


        /// <summary>
        /// Traduit un nom de région tel qu'écrit dans Snes.csv ("Japon",
        /// "USA", "Europe") vers le code région à 2-3 lettres utilisé par
        /// ScreenScraper ("jp", "us", "eu"). Retourne null si non reconnu
        /// (le fallback "wor"/premier disponible s'applique alors).
        /// </summary>
        private static string MapRegionToCode(string region)
        {
            if (string.IsNullOrWhiteSpace(region))
            {
                return null;
            }

            switch (region.Trim().ToLowerInvariant())
            {
                case "japon":
                case "japan":
                case "jp":
                    return "jp";
                case "usa":
                case "us":
                case "états-unis":
                    return "us";
                case "europe":
                case "eu":
                    return "eu";
                default:
                    return null;
            }
        }


        private string ExtractBoxArtUrl(string json, string identifier, string preferredRegionCode)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;

                Dictionary<string, object> root =
                    serializer.Deserialize<Dictionary<string, object>>(json);

                if (!TryGetDict(root, "response", out Dictionary<string, object> response))
                {
                    return null;
                }

                if (!TryGetDict(response, "jeu", out Dictionary<string, object> jeu))
                {
                    return null;
                }

                if (!jeu.TryGetValue("medias", out object mediasObj) ||
                    !(mediasObj is System.Collections.ArrayList medias))
                {
                    return null;
                }

                // "box-2D" = photo du boîtier physique dans la région
                // demandée (ex. jaquette japonaise réelle pour les Super
                // Famicom). Le probable souci de rognage/zoom vient de
                // l'affichage Playnite (mode d'étirement de la jaquette dans
                // la vue grille), pas du choix de l'image elle-même -- voir
                // message accompagnant ce changement.
                string[] typePriority = { "box-2D" };

                foreach (string typePrefix in typePriority)
                {
                    string url = FindBestMediaUrl(medias, typePrefix, preferredRegionCode);

                    if (url != null)
                    {
                        return url;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"ScreenScraperClient : erreur de parsing JSON pour {identifier}.");

                return null;
            }
        }


        private static string FindBestMediaUrl(
            System.Collections.ArrayList medias,
            string typePrefix,
            string preferredRegionCode)
        {
            string worldUrl = null;
            string fallbackUrl = null;

            foreach (object mediaObj in medias)
            {
                if (!(mediaObj is Dictionary<string, object> media))
                {
                    continue;
                }

                if (!media.TryGetValue("type", out object typeObj))
                {
                    continue;
                }

                string type = typeObj as string ?? string.Empty;

                if (!type.StartsWith(typePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!media.TryGetValue("url", out object urlObj))
                {
                    continue;
                }

                string mediaUrl = urlObj as string;

                if (string.IsNullOrWhiteSpace(mediaUrl))
                {
                    continue;
                }

                media.TryGetValue("region", out object regionObj);
                string region = regionObj as string ?? string.Empty;

                if (preferredRegionCode != null &&
                    string.Equals(region, preferredRegionCode, StringComparison.OrdinalIgnoreCase))
                {
                    // Meilleure correspondance possible -- on arrête tout de suite.
                    return mediaUrl;
                }

                if (worldUrl == null && string.Equals(region, "wor", StringComparison.OrdinalIgnoreCase))
                {
                    worldUrl = mediaUrl;
                }

                if (fallbackUrl == null)
                {
                    fallbackUrl = mediaUrl;
                }
            }

            return worldUrl ?? fallbackUrl;
        }


        private static bool TryGetDict(
            Dictionary<string, object> source,
            string key,
            out Dictionary<string, object> result)
        {
            result = null;

            if (source == null)
            {
                return false;
            }

            if (source.TryGetValue(key, out object value) &&
                value is Dictionary<string, object> dict)
            {
                result = dict;

                return true;
            }

            return false;
        }
    }
}

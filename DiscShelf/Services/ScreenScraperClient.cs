using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Script.Serialization;

using Playnite.SDK;

namespace DiscShelf.Services
{
    /// <summary>
    /// Client minimal pour l'API ScreenScraper.fr (jeuInfos.php), recherche
    /// par serial (serialnum1). Nécessite un compte développeur gratuit
    /// (devid/devpassword, demandé sur leur forum) et idéalement un compte
    /// utilisateur (ssid/sspassword) pour un quota correct.
    /// Doc / discussions : https://www.screenscraper.fr/
    /// </summary>
    public class ScreenScraperClient
    {
        private const string BaseUrl = "https://www.screenscraper.fr/api2/jeuInfos.php";

        /// <summary>
        /// Ordre de préférence des régions pour choisir une jaquette quand
        /// plusieurs sont disponibles et qu'aucune n'est "wor" (monde).
        /// "jp" est privilégié avant "eu"/"fr" car une bonne partie des jeux
        /// rétro couverts par ce plugin (Saturn, NeoGeoCD...) sont des
        /// exclusivités japonaises -- prendre une jaquette occidentale par
        /// défaut donnerait le mauvais visuel pour ces jeux-là.
        /// </summary>
        private static readonly string[] RegionPreferenceOrder =
            { "wor", "jp", "us", "eu", "fr", "ss" };

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
            string softwareName = "DiscShelf")
        {
            this.devId = devId;
            this.devPassword = devPassword;
            this.userId = userId;
            this.userPassword = userPassword;
            this.softwareName = softwareName;

            logger = LogManager.GetLogger();
        }


        /// <summary>
        /// Charge les identifiants depuis un fichier CSV (une ligne,
        /// devid;devpassword;ssid;sspassword, les lignes commençant par
        /// # sont ignorées). Retourne un client "non configuré" (IsConfigured
        /// = false) si le fichier est absent ou vide, plutôt que de planter.
        /// </summary>
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
        /// Retourne l'URL d'une jaquette (media "box-2D") pour ce serial,
        /// ou null si non trouvée / erreur.
        /// </summary>
        public string GetBoxArtUrl(string serial, int systemeId)
        {
            if (!IsConfigured)
            {
                logger.Info("ScreenScraperClient : identifiants développeur absents, appel ignoré.");

                return null;
            }

            if (string.IsNullOrWhiteSpace(serial))
            {
                return null;
            }

            string url = BuildUrl(sb => sb
                .Append("&systemeid=").Append(systemeId)
                .Append("&serialnum1=").Append(Uri.EscapeDataString(serial)));

            string json = Download(url, serial);

            return ExtractBoxArtUrl(json, serial);
        }


        /// <summary>
        /// Repli par nom + plateforme, pour les cas où on n'a pas de vrai
        /// serial (ex. NeoGeoCD, identifié par une étiquette de volume, pas
        /// un code officiel). Moins précis qu'une recherche par serial --
        /// ScreenScraper fait un matching approximatif sur le nom de fichier
        /// ROM (paramètre "romnom"), mais reste borné à la bonne plateforme
        /// via systemeid, contrairement à SteamGridDB qui n'a aucune notion
        /// de plateforme.
        /// </summary>
        public string GetBoxArtUrlByName(string name, int systemeId)
        {
            if (!IsConfigured)
            {
                logger.Info("ScreenScraperClient : identifiants développeur absents, appel ignoré.");

                return null;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string url = BuildUrl(sb => sb
                .Append("&systemeid=").Append(systemeId)
                .Append("&romtype=rom")
                .Append("&romnom=").Append(Uri.EscapeDataString(name)));

            string json = Download(url, name);

            return ExtractBoxArtUrl(json, name);
        }


        private string Download(string url, string context)
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("User-Agent", softwareName);

                    return client.DownloadString(url);
                }
            }
            catch (WebException ex)
            {
                logger.Info(
                    $"ScreenScraperClient : requête échouée pour {context} ({ex.Message})."
                );

                return null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "ScreenScraperClient : erreur inattendue pendant la requête.");

                return null;
            }
        }


        private string BuildUrl(Action<System.Text.StringBuilder> appendSpecificParams)
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

            appendSpecificParams(sb);

            return sb.ToString();
        }


        /// <summary>
        /// Parcourt la réponse JSON (structure non typée, potentiellement
        /// variable selon les versions de l'API) à la recherche d'un media
        /// de type "box-2D". Logge un extrait de la réponse en cas d'échec
        /// pour faciliter le diagnostic/ajustement.
        /// </summary>
        private string ExtractBoxArtUrl(string json, string serial)
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
                    logger.Info(
                        $"ScreenScraperClient : pas de champ 'response' pour {serial}. Extrait : {Truncate(json)}"
                    );

                    return null;
                }

                if (!TryGetDict(response, "jeu", out Dictionary<string, object> jeu))
                {
                    logger.Info(
                        $"ScreenScraperClient : jeu introuvable sur ScreenScraper pour {serial}."
                    );

                    return null;
                }

                jeu.TryGetValue("id", out object jeuIdObj);
                jeu.TryGetValue("nom", out object jeuNomObj);

                logger.Info(
                    $"ScreenScraperClient : jeu ScreenScraper matché pour \"{serial}\" -> id={jeuIdObj}, nom={jeuNomObj}"
                );

                if (!jeu.TryGetValue("medias", out object mediasObj) ||
                    !(mediasObj is System.Collections.ArrayList medias))
                {
                    logger.Info(
                        $"ScreenScraperClient : pas de médias pour {serial}."
                    );

                    return null;
                }

                // On collecte tous les box-2D disponibles (région -> url),
                // puis on choisit selon un ordre de préférence explicite --
                // plutôt que "le premier trouvé", qui donnait de mauvaises
                // surprises (ex. jaquette française prise au hasard sur un
                // jeu exclusif Japon, faute de région "wor").
                Dictionary<string, string> urlByRegion =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

                    if (!type.StartsWith("box-2D", StringComparison.OrdinalIgnoreCase))
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
                    string region = (regionObj as string ?? string.Empty).Trim();

                    if (!urlByRegion.ContainsKey(region))
                    {
                        urlByRegion[region] = mediaUrl;
                    }
                }

                logger.Info(
                    $"ScreenScraperClient : médias box-2D disponibles pour {serial} : {(urlByRegion.Count > 0 ? string.Join(", ", urlByRegion.Keys) : "aucun")}"
                );

                foreach (string preferredRegion in RegionPreferenceOrder)
                {
                    if (urlByRegion.TryGetValue(preferredRegion, out string preferredUrl))
                    {
                        return preferredUrl;
                    }
                }

                // Aucune région préférée disponible : on prend ce qu'il y a.
                foreach (string anyUrl in urlByRegion.Values)
                {
                    return anyUrl;
                }

                logger.Info(
                    $"ScreenScraperClient : jeu trouvé mais aucun media box-2D pour {serial}."
                );

                return null;
            }
            catch (Exception ex)
            {
                logger.Error(
                    ex,
                    $"ScreenScraperClient : erreur de parsing JSON pour {serial}. Extrait : {Truncate(json)}"
                );

                return null;
            }
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


        private static string Truncate(string text, int maxLength = 300)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length <= maxLength
                ? text
                : text.Substring(0, maxLength) + "...";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Script.Serialization;

using Playnite.SDK;

namespace DiscShelf.Services
{
    /// <summary>
    /// Client minimal pour l'API SteamGridDB (recherche par NOM, pas par
    /// serial). À utiliser en repli quand ScreenScraper ne trouve rien pour
    /// le serial exact. Nécessite une clé API personnelle, gratuite,
    /// générée sur https://www.steamgriddb.com/profile/preferences/api
    /// </summary>
    public class SteamGridDbClient
    {
        private const string BaseUrl = "https://www.steamgriddb.com/api/v2";

        private readonly ILogger logger;

        private readonly string apiKey;


        public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey);


        public SteamGridDbClient(string apiKey)
        {
            this.apiKey = apiKey;

            logger = LogManager.GetLogger();
        }


        public static SteamGridDbClient LoadFromFile(string filePath)
        {
            ILogger logger = LogManager.GetLogger();

            if (!System.IO.File.Exists(filePath))
            {
                logger.Info(
                    $"SteamGridDbClient : fichier d'identifiants absent ({filePath}), repli désactivé."
                );

                return new SteamGridDbClient(null);
            }

            foreach (string line in System.IO.File.ReadAllLines(filePath))
            {
                string trimmed = line.Trim();

                if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                {
                    continue;
                }

                return new SteamGridDbClient(trimmed);
            }

            logger.Info(
                $"SteamGridDbClient : aucune clé valide dans {filePath}, repli désactivé."
            );

            return new SteamGridDbClient(null);
        }


        /// <summary>
        /// Cherche un jeu par nom et retourne l'URL de la première jaquette
        /// ("grid") trouvée, ou null si rien n'est trouvé / erreur.
        /// </summary>
        public string GetCoverUrlByName(string gameName)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(gameName))
            {
                return null;
            }

            try
            {
                int? gameId = SearchGameId(gameName);

                if (gameId == null)
                {
                    logger.Info($"SteamGridDbClient : aucun jeu trouvé pour \"{gameName}\".");

                    return null;
                }

                return GetFirstGridUrl(gameId.Value);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"SteamGridDbClient : erreur pendant la recherche de \"{gameName}\".");

                return null;
            }
        }


        private int? SearchGameId(string gameName)
        {
            string url = $"{BaseUrl}/search/autocomplete/{Uri.EscapeDataString(gameName)}";

            string json = DownloadWithAuth(url);

            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();

            Dictionary<string, object> root =
                serializer.Deserialize<Dictionary<string, object>>(json);

            if (root == null ||
                !root.TryGetValue("data", out object dataObj) ||
                !(dataObj is System.Collections.ArrayList list) ||
                list.Count == 0)
            {
                return null;
            }

            if (!(list[0] is Dictionary<string, object> firstGame))
            {
                return null;
            }

            if (firstGame.TryGetValue("id", out object idObj))
            {
                return Convert.ToInt32(idObj);
            }

            return null;
        }


        private string GetFirstGridUrl(int gameId)
        {
            string url = $"{BaseUrl}/grids/game/{gameId}";

            string json = DownloadWithAuth(url);

            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();

            Dictionary<string, object> root =
                serializer.Deserialize<Dictionary<string, object>>(json);

            if (root == null ||
                !root.TryGetValue("data", out object dataObj) ||
                !(dataObj is System.Collections.ArrayList list) ||
                list.Count == 0)
            {
                return null;
            }

            if (!(list[0] is Dictionary<string, object> firstGrid))
            {
                return null;
            }

            return firstGrid.TryGetValue("url", out object urlObj)
                ? urlObj as string
                : null;
        }


        private string DownloadWithAuth(string url)
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("Authorization", "Bearer " + apiKey);

                    return client.DownloadString(url);
                }
            }
            catch (WebException ex)
            {
                logger.Info($"SteamGridDbClient : requête échouée ({ex.Message}).");

                return null;
            }
        }
    }
}

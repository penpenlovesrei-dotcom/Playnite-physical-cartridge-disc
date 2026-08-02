using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using DiscShelf.Core;

using Playnite.SDK;

namespace DiscShelf.Database
{
    public class DiscEntry
    {
        /// <summary>
        /// Un même jeu multi-disques a plusieurs serials (un par disque)
        /// qui pointent tous vers cette même entrée.
        /// </summary>
        public List<string> Serials { get; set; } = new List<string>();

        public string Title { get; set; }

        public string Tags { get; set; }

        public PlatformId Platform { get; set; }
    }


    /// <summary>
    /// Charge et interroge les bases de signatures (une par plateforme,
    /// ex. Playstation.csv, PlayStation2.csv). Format attendu par ligne CSV :
    ///   serial(s);titre;tags   (délimiteur configurable, ex. ',' pour les
    ///   bases externes type GameTDB/redump)
    /// Un jeu multi-disques a son champ serial entre guillemets avec un
    /// serial par ligne, ex. :
    ///   "SLPS_000.71
    ///   SLPS_000.72";Mon jeu;[J]
    /// </summary>
    public class DiscDatabase
    {
        private readonly ILogger logger;

        private readonly List<DiscEntry> allEntries =
            new List<DiscEntry>();

        private readonly Dictionary<string, DiscEntry> bySerial =
            new Dictionary<string, DiscEntry>(StringComparer.OrdinalIgnoreCase);


        public int Count => allEntries.Count;


        public DiscDatabase()
        {
            logger = LogManager.GetLogger();
        }


        public void Load(string filePath, PlatformId platform, char delimiter = ';')
        {
            if (!File.Exists(filePath))
            {
                logger.Error(
                    $"DiscDatabase : fichier introuvable : {filePath}"
                );

                return;
            }

            string text = File.ReadAllText(filePath, Encoding.UTF8);

            List<string[]> records = CsvParser.Parse(text, delimiter);

            int loaded = 0;

            foreach (string[] record in records)
            {
                if (record.Length < 2)
                {
                    continue;
                }

                string serialField = record[0].Trim();
                string title = record[1].Trim();
                string tags = record.Length > 2 ? record[2].Trim() : string.Empty;

                if (string.IsNullOrWhiteSpace(serialField) ||
                    string.IsNullOrWhiteSpace(title))
                {
                    // Lignes séparatrices type ";;" présentes dans le fichier
                    continue;
                }

                // Ligne d'en-tête générique (ex. "ID,title") présente dans
                // certaines bases externes (GameTDB, etc.)
                if (string.Equals(serialField, "ID", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(serialField, "Serial", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(serialField, "TITLES", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] serials = serialField.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                );

                DiscEntry entry = new DiscEntry
                {
                    Title = title,
                    Tags = tags,
                    Platform = platform
                };

                foreach (string rawSerial in serials)
                {
                    string serial = rawSerial.Trim();

                    if (string.IsNullOrEmpty(serial))
                    {
                        continue;
                    }

                    // Mega CD : le padding à largeur fixe du serial lu sur
                    // le disque ne tombe pas toujours au même endroit que
                    // dans cette base (ex. "GM G-6013-00" vs "GM G-6013 -00")
                    // -- on retire tous les espaces internes des deux côtés
                    // pour que ça matche de façon fiable.
                    if (platform == PlatformId.MegaCD)
                    {
                        serial = serial.Replace(" ", "");
                    }

                    entry.Serials.Add(serial);

                    if (bySerial.ContainsKey(serial))
                    {
                        logger.Info(
                            $"DiscDatabase : serial en doublon ignoré : {serial}"
                        );

                        continue;
                    }

                    bySerial.Add(serial, entry);
                }

                if (entry.Serials.Count > 0)
                {
                    allEntries.Add(entry);
                    loaded++;
                }
            }

            logger.Info(
                $"DiscDatabase : {loaded} entrée(s) chargée(s) depuis {Path.GetFileName(filePath)} ({platform})."
            );
        }


        public DiscEntry Find(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
            {
                return null;
            }

            bySerial.TryGetValue(serial.Trim(), out DiscEntry entry);

            return entry;
        }


        /// <summary>
        /// Repli utilisé quand l'identifiant complet ne matche rien (ex.
        /// NeoGeoCD : la date d'un fichier peut différer de la date de
        /// création du volume enregistrée dans la base, alors que
        /// l'étiquette du volume, elle, est fiable). Cherche un serial de
        /// la même plateforme se terminant par "_étiquette".
        /// </summary>
        public DiscEntry FindByLabelSuffix(string label, PlatformId platform)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return null;
            }

            string needle = "_" + label.Trim();

            foreach (DiscEntry entry in allEntries)
            {
                if (entry.Platform != platform)
                {
                    continue;
                }

                foreach (string candidate in entry.Serials)
                {
                    if (candidate.EndsWith(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
            }

            return null;
        }


        /// <summary>
        /// Repli "date seule" (jour, sans l'heure) utilisé quand l'étiquette
        /// du volume n'est pas exploitable (ex. "UNTITLED"). Cherche un
        /// serial de la même plateforme dont les 10 premiers caractères
        /// (YYYY-MM-DD) correspondent.
        /// </summary>
        public DiscEntry FindByDatePrefix(string dateOnly, PlatformId platform)
        {
            if (string.IsNullOrWhiteSpace(dateOnly) || dateOnly.Length != 10)
            {
                return null;
            }

            foreach (DiscEntry entry in allEntries)
            {
                if (entry.Platform != platform)
                {
                    continue;
                }

                foreach (string candidate in entry.Serials)
                {
                    if (candidate.Length >= 10 &&
                        candidate.Substring(0, 10).Equals(dateOnly, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
            }

            return null;
        }
        /// <summary>
        /// Repli "sans préfixe éditeur" pour les bases où certaines entrées
        /// ont leur préfixe (ex. "MK-") retiré de façon incohérente (vu sur
        /// Dreamcast : le disque donne "MK-51000", la base a juste
        /// "51000"). Retire un préfixe alphabétique suivi d'un tiret et
        /// retente une recherche exacte sur ce qui reste.
        /// </summary>
        public DiscEntry FindStrippingPublisherPrefix(string serial, PlatformId platform)
        {
            if (string.IsNullOrWhiteSpace(serial))
            {
                return null;
            }

            string stripped = Regex.Replace(serial.Trim(), @"^[A-Za-z]+-", "");

            if (stripped == serial.Trim())
            {
                return null;
            }

            bySerial.TryGetValue(stripped, out DiscEntry entry);

            if (entry != null && entry.Platform != platform)
            {
                return null;
            }

            return entry;
        }
    }
}

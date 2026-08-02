using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using CartridgeShelf.Core;

using Playnite.SDK;

namespace CartridgeShelf.Database
{
    public class CartridgeEntry
    {
        /// <summary>Checksum hex (ex. "9ea9"), clé unique par jeu.</summary>
        public string Checksum { get; set; }

        public string Title { get; set; }

        public string Tags { get; set; }

        /// <summary>Région de la cartouche (ex. "Japon", "USA", "Europe").</summary>
        public string Region { get; set; }

        public PlatformId Platform { get; set; }
    }


    /// <summary>
    /// Charge et interroge les bases de correspondance (une par plateforme,
    /// ex. Snes.csv). Format attendu par ligne CSV :
    ///   checksum;titre;tags;region
    /// Le champ région est en dernier pour rester compatible avec les
    /// anciennes lignes à 3 colonnes (checksum;titre;tags) déjà écrites --
    /// region est alors simplement vide.
    /// Équivalent cartouche de DiscDatabase, mais la clé est un checksum
    /// USB (4 caractères hex) au lieu d'un serial de disque, et il n'y a
    /// pas de notion multi-disques donc pas de champ multi-lignes.
    /// </summary>
    public class CartridgeDatabase
    {
        private readonly ILogger logger;

        private readonly List<CartridgeEntry> allEntries =
            new List<CartridgeEntry>();

        private readonly Dictionary<string, CartridgeEntry> byChecksum =
            new Dictionary<string, CartridgeEntry>(StringComparer.OrdinalIgnoreCase);


        public int Count => allEntries.Count;


        public CartridgeDatabase()
        {
            logger = LogManager.GetLogger();
        }


        public void Load(string filePath, PlatformId platform)
        {
            if (!File.Exists(filePath))
            {
                logger.Error(
                    $"CartridgeDatabase : fichier introuvable : {filePath}"
                );

                return;
            }

            string text = File.ReadAllText(filePath, Encoding.UTF8);

            List<string[]> records = CsvParser.Parse(text);

            int loaded = 0;

            foreach (string[] record in records)
            {
                if (record.Length < 2)
                {
                    continue;
                }

                string checksum = record[0].Trim();
                string title = record[1].Trim();
                string tags = record.Length > 2 ? record[2].Trim() : string.Empty;
                string region = record.Length > 3 ? record[3].Trim() : string.Empty;

                if (string.IsNullOrWhiteSpace(checksum) ||
                    string.IsNullOrWhiteSpace(title) ||
                    checksum.StartsWith("#"))
                {
                    continue;
                }

                CartridgeEntry entry = new CartridgeEntry
                {
                    Checksum = checksum,
                    Title = title,
                    Tags = tags,
                    Region = region,
                    Platform = platform
                };

                if (byChecksum.ContainsKey(checksum))
                {
                    logger.Info(
                        $"CartridgeDatabase : checksum en doublon ignoré : {checksum}"
                    );

                    continue;
                }

                byChecksum.Add(checksum, entry);
                allEntries.Add(entry);
                loaded++;
            }

            logger.Info(
                $"CartridgeDatabase : {loaded} entrée(s) chargée(s) depuis {Path.GetFileName(filePath)} ({platform})."
            );
        }


        public CartridgeEntry Find(string checksum)
        {
            if (string.IsNullOrWhiteSpace(checksum))
            {
                return null;
            }

            byChecksum.TryGetValue(checksum.Trim(), out CartridgeEntry entry);

            return entry;
        }


        /// <summary>
        /// Ajoute une entrée en mémoire (utilisé par le mécanisme d'ajout
        /// interactif). Ne touche à aucun fichier -- l'appelant est
        /// responsable de la persistance si besoin. Ignore silencieusement
        /// si le checksum existe déjà.
        /// </summary>
        public void Add(string checksum, string title, string region, PlatformId platform)
        {
            if (string.IsNullOrWhiteSpace(checksum) || string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            checksum = checksum.Trim();

            if (byChecksum.ContainsKey(checksum))
            {
                return;
            }

            CartridgeEntry entry = new CartridgeEntry
            {
                Checksum = checksum,
                Title = title.Trim(),
                Tags = string.Empty,
                Region = region?.Trim() ?? string.Empty,
                Platform = platform
            };

            byChecksum.Add(checksum, entry);
            allEntries.Add(entry);
        }
    }
}

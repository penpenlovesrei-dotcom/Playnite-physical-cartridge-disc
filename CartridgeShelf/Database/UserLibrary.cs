using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Playnite.SDK;

namespace CartridgeShelf.Database
{
    public class UserLibraryEntry
    {
        public string Checksum { get; set; }

        /// <summary>Chemin du fichier ROM dumpé sur le SSD (ex. .sfc).</summary>
        public string RomPath { get; set; }

        public string EmulatorPath { get; set; }

        /// <summary>
        /// Arguments passés à l'émulateur. Le jeton {ROM} est remplacé
        /// par le chemin du fichier ROM, entouré de guillemets.
        /// </summary>
        public string ArgumentsTemplate { get; set; }


        public string BuildArguments()
        {
            string template = ArgumentsTemplate ?? "\"{ROM}\"";

            return template.Replace("{ROM}", RomPath);
        }
    }


    /// <summary>
    /// Bibliothèque perso de l'utilisateur : associe un checksum de
    /// cartouche (identifié via CartridgeDatabase) au fichier ROM présent
    /// sur le SSD et à l'émulateur à utiliser pour la lancer.
    /// Fichier CSV attendu : checksum;romPath;emulatorPath;arguments
    /// </summary>
    public class UserLibrary
    {
        private readonly ILogger logger;

        private readonly Dictionary<string, UserLibraryEntry> byChecksum =
            new Dictionary<string, UserLibraryEntry>(StringComparer.OrdinalIgnoreCase);


        public int Count => byChecksum.Count;


        public UserLibrary()
        {
            logger = LogManager.GetLogger();
        }


        public void Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                logger.Info(
                    $"UserLibrary : fichier absent (aucun jeu configuré pour l'instant) : {filePath}"
                );

                return;
            }

            string text = File.ReadAllText(filePath, Encoding.UTF8);

            List<string[]> records = CsvParser.Parse(text);

            int loaded = 0;

            foreach (string[] record in records)
            {
                if (record.Length < 3)
                {
                    continue;
                }

                string checksum = record[0].Trim();

                if (string.IsNullOrWhiteSpace(checksum) ||
                    checksum.StartsWith("#"))
                {
                    continue;
                }

                UserLibraryEntry entry = new UserLibraryEntry
                {
                    Checksum = checksum,
                    RomPath = record[1].Trim(),
                    EmulatorPath = record[2].Trim(),
                    ArgumentsTemplate = record.Length > 3 ? record[3].Trim() : "\"{ROM}\""
                };

                byChecksum[checksum] = entry;
                loaded++;
            }

            logger.Info(
                $"UserLibrary : {loaded} entrée(s) chargée(s) depuis {Path.GetFileName(filePath)}."
            );
        }


        public UserLibraryEntry Find(string checksum)
        {
            if (string.IsNullOrWhiteSpace(checksum))
            {
                return null;
            }

            byChecksum.TryGetValue(checksum.Trim(), out UserLibraryEntry entry);

            return entry;
        }


        /// <summary>
        /// Ajoute/remplace une entrée en mémoire (utilisé par le
        /// configurateur interactif). L'appelant gère la persistance.
        /// </summary>
        public void Add(UserLibraryEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Checksum))
            {
                return;
            }

            byChecksum[entry.Checksum.Trim()] = entry;
        }
    }
}

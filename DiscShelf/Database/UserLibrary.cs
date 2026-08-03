using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Playnite.SDK;

namespace DiscShelf.Database
{
    public class UserLibraryEntry
    {
        public string Serial { get; set; }

        public string IsoPath { get; set; }

        public string EmulatorPath { get; set; }

        /// <summary>
        /// Arguments passés à l'émulateur. Le jeton {ISO} est remplacé
        /// par le chemin de l'image, entouré de guillemets. Ex. : -batch {ISO}
        /// Le gabarit stocké ne porte donc pas les guillemets lui-même :
        /// pour CsvParser, le guillemet délimite un champ et disparaît à
        /// la relecture. Un gabarit qui en contient malgré tout (fichier
        /// écrit à la main, entrée d'une version antérieure) reste accepté.
        /// </summary>
        public string ArgumentsTemplate { get; set; }


        public string BuildArguments()
        {
            string template = ArgumentsTemplate ?? "{ISO}";

            return template
                .Replace("\"{ISO}\"", "{ISO}")
                .Replace("{ISO}", QuotePath(IsoPath));
        }


        private static string QuotePath(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : "\"" + path + "\"";
        }
    }


    /// <summary>
    /// Bibliothèque perso de l'utilisateur : associe un serial de jeu
    /// (identifié via DiscDatabase) à l'image ISO présente sur le SSD
    /// et à l'émulateur à utiliser pour la lancer.
    /// Fichier CSV attendu : serial;isoPath;emulatorPath;arguments
    /// </summary>
    public class UserLibrary
    {
        private readonly ILogger logger;

        private readonly Dictionary<string, UserLibraryEntry> bySerial =
            new Dictionary<string, UserLibraryEntry>(StringComparer.OrdinalIgnoreCase);


        public int Count => bySerial.Count;


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

                string serial = record[0].Trim();

                if (string.IsNullOrWhiteSpace(serial) ||
                    serial.StartsWith("#"))
                {
                    continue;
                }

                UserLibraryEntry entry = new UserLibraryEntry
                {
                    Serial = serial,
                    IsoPath = record[1].Trim(),
                    EmulatorPath = record[2].Trim(),
                    ArgumentsTemplate = record.Length > 3 ? record[3].Trim() : "{ISO}"
                };

                bySerial[serial] = entry;
                loaded++;
            }

            logger.Info(
                $"UserLibrary : {loaded} entrée(s) chargée(s) depuis {Path.GetFileName(filePath)}."
            );
        }


        public UserLibraryEntry Find(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
            {
                return null;
            }

            bySerial.TryGetValue(serial.Trim(), out UserLibraryEntry entry);

            return entry;
        }


        /// <summary>
        /// Ajoute/remplace une entrée en mémoire (utilisé par le
        /// configurateur interactif). L'appelant gère la persistance.
        /// </summary>
        public void Add(UserLibraryEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Serial))
            {
                return;
            }

            bySerial[entry.Serial.Trim()] = entry;
        }
    }
}

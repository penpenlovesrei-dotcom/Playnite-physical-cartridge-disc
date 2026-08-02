using System;
using System.Collections.Generic;
using System.IO;

using DiscShelf.Core;

namespace DiscShelf.Detectors
{
    /// <summary>
    /// Parse un SYSTEM.CNF en dictionnaire clé/valeur (BOOT, BOOT2, VER,
    /// VMODE...). Utiliser des clés exactes (et non un StartsWith) est
    /// important : "BOOT2" commence par "BOOT", donc un simple préfixe
    /// ferait déclencher le détecteur PS1 sur un disque PS2.
    /// </summary>
    public static class SystemCnfReader
    {
        public static Dictionary<string, string> Parse(DiscInfo disc)
        {
            Dictionary<string, string> result =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (disc == null)
            {
                return result;
            }

            DiscFile cnfFile = disc.FindFile("SYSTEM.CNF");

            if (cnfFile == null)
            {
                return result;
            }

            foreach (string rawLine in File.ReadAllLines(cnfFile.FullPath))
            {
                string line = rawLine.Trim();

                int equalIndex = line.IndexOf('=');

                if (equalIndex < 0 || equalIndex == line.Length - 1)
                {
                    continue;
                }

                string key = line.Substring(0, equalIndex).Trim();
                string value = line.Substring(equalIndex + 1).Trim();

                if (key.Length > 0)
                {
                    result[key] = value;
                }
            }

            return result;
        }


        /// <summary>
        /// Transforme une valeur brute type "cdrom0:\SLUS_205.02;1" en un
        /// serial propre "SLUS_205.02" (format disque, avec underscore+point).
        /// </summary>
        public static string NormalizeRawSerial(string raw)
        {
            string value = raw;

            int colonIndex = value.IndexOf(':');
            if (colonIndex >= 0)
            {
                value = value.Substring(colonIndex + 1);
            }

            value = value.TrimStart('\\', '/');

            int semicolonIndex = value.IndexOf(';');
            if (semicolonIndex >= 0)
            {
                value = value.Substring(0, semicolonIndex);
            }

            return value.Trim().ToUpperInvariant();
        }
    }
}

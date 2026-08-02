using System.Collections.Generic;
using System.Text;

namespace DiscShelf.Database
{
    /// <summary>
    /// Parseur CSV minimal mais correct : gère les champs entre guillemets
    /// pouvant contenir le délimiteur ou des retours à la ligne (cas des
    /// jeux multi-disques dans Playstation.csv, où plusieurs serials sont
    /// listés sur plusieurs lignes à l'intérieur d'un seul champ).
    /// Un simple File.ReadAllLines() + Split(';') casse ce format.
    /// </summary>
    public static class CsvParser
    {
        public static List<string[]> Parse(string text, char delimiter = ';')
        {
            List<string[]> records = new List<string[]>();
            List<string> fields = new List<string>();
            StringBuilder current = new StringBuilder();

            bool inQuotes = false;
            int i = 0;
            int length = text.Length;

            while (i < length)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < length && text[i + 1] == '"')
                        {
                            current.Append('"');
                            i += 2;
                            continue;
                        }

                        inQuotes = false;
                        i++;
                        continue;
                    }

                    current.Append(c);
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    i++;
                    continue;
                }

                if (c == delimiter)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                    i++;
                    continue;
                }

                if (c == '\r')
                {
                    i++;
                    continue;
                }

                if (c == '\n')
                {
                    fields.Add(current.ToString());
                    current.Clear();

                    records.Add(fields.ToArray());
                    fields = new List<string>();

                    i++;
                    continue;
                }

                current.Append(c);
                i++;
            }

            if (current.Length > 0 || fields.Count > 0)
            {
                fields.Add(current.ToString());
                records.Add(fields.ToArray());
            }

            return records;
        }
    }
}

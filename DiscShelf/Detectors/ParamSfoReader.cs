using System;
using System.IO;
using System.Text;

namespace DiscShelf.Detectors
{
    /// <summary>
    /// Lecteur minimal du format binaire PARAM.SFO (utilisé par PS3, PSP,
    /// PS4, Vita). On ne lit que ce dont on a besoin : la valeur chaîne
    /// (UTF-8) associée à une clé donnée (ex. "TITLE_ID").
    /// Structure : en-tête (20 octets) + table d'index (16 octets/entrée)
    /// + table des clés (chaînes null-terminées) + table des données.
    /// Référence : https://www.psdevwiki.com/ps3/PARAM.SFO
    /// </summary>
    public static class ParamSfoReader
    {
        private static readonly byte[] Magic = { 0x00, 0x50, 0x53, 0x46 };


        public static string ReadValue(string filePath, string key)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            byte[] data = File.ReadAllBytes(filePath);

            if (data.Length < 20)
            {
                return null;
            }

            for (int i = 0; i < 4; i++)
            {
                if (data[i] != Magic[i])
                {
                    return null;
                }
            }

            uint keyTableStart = ReadUInt32(data, 8);
            uint dataTableStart = ReadUInt32(data, 12);
            uint entryCount = ReadUInt32(data, 16);

            for (uint i = 0; i < entryCount; i++)
            {
                int entryOffset = 20 + (int)(i * 16);

                if (entryOffset + 16 > data.Length)
                {
                    break;
                }

                ushort keyOffset = ReadUInt16(data, entryOffset);
                uint paramLen = ReadUInt32(data, entryOffset + 4);
                uint dataOffset = ReadUInt32(data, entryOffset + 12);

                int keyStart = (int)(keyTableStart + keyOffset);

                if (keyStart < 0 || keyStart >= data.Length)
                {
                    continue;
                }

                string entryKey = ReadNullTerminatedString(data, keyStart);

                if (!string.Equals(entryKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int valueStart = (int)(dataTableStart + dataOffset);
                int valueLen = (int)paramLen;

                if (valueStart < 0 || valueLen < 0 || valueStart + valueLen > data.Length)
                {
                    return null;
                }

                string value = Encoding.UTF8.GetString(data, valueStart, valueLen);

                return value.TrimEnd('\0');
            }

            return null;
        }


        private static string ReadNullTerminatedString(byte[] data, int offset)
        {
            int end = offset;

            while (end < data.Length && data[end] != 0)
            {
                end++;
            }

            return Encoding.ASCII.GetString(data, offset, end - offset);
        }


        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }


        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }
    }
}

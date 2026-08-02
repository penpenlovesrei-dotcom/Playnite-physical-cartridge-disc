using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Win32.SafeHandles;

using Playnite.SDK;

namespace DiscShelf.Detectors
{
    /// <summary>
    /// Lit les tout premiers octets du disque (secteur 0, avant la zone
    /// ISO9660) via un accès bas niveau au lecteur (\\.\X:), pour les cas
    /// où IP.BIN n'apparaît pas comme un fichier normal à la racine
    /// (fréquent sur les disques Saturn "auto-bootables").
    /// Même technique que Iso9660PvdReader, mais lit depuis le tout début
    /// du disque plutôt que le secteur 16.
    /// </summary>
    public static class RawSectorReader
    {
        private const uint GenericRead = 0x80000000;
        private const uint FileShareRead = 0x1;
        private const uint FileShareWrite = 0x2;
        private const uint OpenExisting = 3;
        private const uint FileFlagNoBuffering = 0x20000000;


        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);


        /// <summary>
        /// Lit `length` octets à partir de l'offset donné (en octets, depuis
        /// le tout début du disque). Retourne null si la lecture échoue.
        /// </summary>
        public static byte[] Read(string driveLetter, long byteOffset, int length)
        {
            string devicePath = @"\\.\" + driveLetter.TrimEnd(':', '\\') + ":";

            ILogger logger = LogManager.GetLogger();

            try
            {
                using (SafeFileHandle handle = CreateFile(
                    devicePath,
                    GenericRead,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    FileFlagNoBuffering,
                    IntPtr.Zero))
                {
                    if (handle.IsInvalid)
                    {
                        logger.Info($"RawSectorReader : impossible d'ouvrir {devicePath}.");

                        return null;
                    }

                    using (FileStream stream = new FileStream(handle, FileAccess.Read))
                    {
                        // La lecture brute d'un volume nécessite souvent des
                        // lectures alignées sur la taille de secteur (2048) --
                        // on lit un secteur complet et on découpe ensuite.
                        int sectorSize = 2048;
                        int sectorsNeeded = (int)Math.Ceiling((byteOffset + length) / (double)sectorSize);
                        byte[] buffer = new byte[sectorsNeeded * sectorSize];

                        stream.Seek(0, SeekOrigin.Begin);

                        int read = stream.Read(buffer, 0, buffer.Length);

                        if (read < byteOffset + length)
                        {
                            logger.Info("RawSectorReader : lecture incomplète.");

                            return null;
                        }

                        byte[] result = new byte[length];
                        Array.Copy(buffer, byteOffset, result, 0, length);

                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"RawSectorReader : erreur de lecture sur {devicePath}.");

                return null;
            }
        }


        public static string ReadAscii(string driveLetter, long byteOffset, int length)
        {
            byte[] data = Read(driveLetter, byteOffset, length);

            return data == null ? null : Encoding.ASCII.GetString(data);
        }
    }
}

using System;
using System.IO;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace DiscShelf.Detectors
{
    /// <summary>
    /// Lit le Primary Volume Descriptor (PVD) ISO9660 d'un disque, au
    /// secteur logique 16 (offset 32768). C'est un format standard, pas
    /// une protection propriétaire, mais il n'est pas exposé par les API
    /// .NET habituelles (DriveInfo ne donne que le label, pas la date de
    /// création précise à la seconde/centième) : il faut ouvrir le lecteur
    /// en accès bas niveau via l'API Windows (CreateFile sur \\.\X:).
    /// Référence structure PVD : ECMA-119 / ISO 9660.
    /// </summary>
    public static class Iso9660PvdReader
    {
        private const int SectorSize = 2048;
        private const int PvdSector = 16;

        private const uint GenericRead = 0x80000000;
        private const uint FileShareRead = 0x1;
        private const uint FileShareWrite = 0x2;
        private const uint OpenExisting = 3;


        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);


        public class PvdInfo
        {
            /// <summary>Étiquette du volume (32 caractères max), sans espaces finaux.</summary>
            public string VolumeLabel;

            /// <summary>
            /// Date de création brute au format YYYYMMDDHHMMSSCC (16 chiffres
            /// ASCII littéraux, tels que stockés sur le disque -- pas
            /// convertis en DateTime, certains disques ont des valeurs
            /// invalides/corrompues qu'on doit reproduire telles quelles).
            /// </summary>
            public string CreationTimestampRaw;
        }


        /// <summary>
        /// Lit le PVD sur le lecteur donné (ex. "D"). Retourne null si le
        /// secteur est illisible ou n'est pas un PVD ISO9660 valide.
        /// </summary>
        public static PvdInfo Read(string driveLetter)
        {
            string devicePath = @"\\.\" + driveLetter.TrimEnd(':', '\\') + ":";

            using (SafeFileHandle handle = CreateFile(
                devicePath,
                GenericRead,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    return null;
                }

                using (FileStream stream = new FileStream(handle, FileAccess.Read))
                {
                    stream.Seek((long)PvdSector * SectorSize, SeekOrigin.Begin);

                    byte[] sector = new byte[SectorSize];

                    int read = stream.Read(sector, 0, SectorSize);

                    if (read < SectorSize)
                    {
                        return null;
                    }

                    // Type 1 = Primary Volume Descriptor, "CD001" = magic ISO9660
                    if (sector[0] != 1 ||
                        sector[1] != (byte)'C' || sector[2] != (byte)'D' ||
                        sector[3] != (byte)'0' || sector[4] != (byte)'0' || sector[5] != (byte)'1')
                    {
                        return null;
                    }

                    string volumeLabel = System.Text.Encoding.ASCII
                        .GetString(sector, 40, 32)
                        .TrimEnd(' ', '\0');

                    // Date de création : offset 813, 17 octets (16 chiffres
                    // ASCII + 1 octet d'offset GMT binaire, qu'on ignore ici).
                    string creationRaw = System.Text.Encoding.ASCII
                        .GetString(sector, 813, 16);

                    return new PvdInfo
                    {
                        VolumeLabel = volumeLabel,
                        CreationTimestampRaw = creationRaw
                    };
                }
            }
        }
    }
}

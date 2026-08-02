using System;

using DiscShelf.Core;

using Playnite.SDK;

namespace DiscShelf.Detectors
{
    /// <summary>
    /// Détecte les disques Sega Saturn en lisant les tout premiers octets
    /// du disque en brut (secteur 0, avant la zone ISO9660 -- IP.BIN
    /// n'apparaît pas toujours comme un fichier normal à la racine, donc
    /// on ne peut pas compter sur une simple recherche de fichier ici).
    ///
    /// Structure confirmée via le code source officiel du SDK Saturn
    /// (IPMaker/SystemID.c) :
    ///   0x00 (16 octets) : Hardware ID, toujours "SEGA SEGASATURN "
    ///   0x10 (16 octets) : Maker ID
    ///   0x20 (10 octets) : Product Number (ex. "GS-9099 ", "T-99901G ")
    ///
    /// ⚠️ Repose sur un accès bas niveau au lecteur (\\.\X:) -- premier
    /// vrai test de cette capacité dans le projet (Iso9660PvdReader avait
    /// été écrit mais jamais confirmé fonctionnel sur un vrai disque).
    /// </summary>
    public class SaturnDetector : IDiscDetector
    {
        private const string HardwareIdMagic = "SEGA SEGASATURN ";
        private const int ProductNumberOffset = 0x20;
        private const int ProductNumberLength = 10;

        private readonly ILogger logger;

        public PlatformId Platform => PlatformId.SegaSaturn;


        public SaturnDetector()
        {
            logger = LogManager.GetLogger();
        }


        public bool CanHandle(DiscInfo disc)
        {
            if (disc == null || string.IsNullOrEmpty(disc.DriveLetter))
            {
                return false;
            }

            string hardwareId = RawSectorReader.ReadAscii(disc.DriveLetter, 0, HardwareIdMagic.Length);

            return string.Equals(hardwareId, HardwareIdMagic, StringComparison.Ordinal);
        }


        public GameIdentity Detect(DiscInfo disc)
        {
            GameIdentity identity = new GameIdentity
            {
                Platform = PlatformId.SegaSaturn
            };

            string productNumber = RawSectorReader.ReadAscii(
                disc.DriveLetter,
                ProductNumberOffset,
                ProductNumberLength
            );

            if (string.IsNullOrWhiteSpace(productNumber))
            {
                logger.Info("SaturnDetector : lecture du Product Number impossible.");

                return identity;
            }

            identity.Serial = productNumber.Trim();

            logger.Info($"SaturnDetector : serial détecté = {identity.Serial}");

            return identity;
        }
    }
}

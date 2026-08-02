using System;
using System.Text.RegularExpressions;

using DiscShelf.Core;

using Playnite.SDK;

namespace DiscShelf.Detectors
{
    /// <summary>
    /// Détecte les disques Dreamcast en lisant les tout premiers octets du
    /// disque en brut. Contrairement aux données de jeu (zone haute
    /// densité du GD-ROM, illisible par un lecteur PC standard), l'en-tête
    /// IP0000.BIN vit dans la zone basse densité en tout début de disque,
    /// lisible normalement.
    ///
    /// Structure confirmée (mc.pp.se/dc/ip0000.bin.html, doc de référence
    /// pour la programmation Dreamcast) :
    ///   0x00 (16 octets) : Hardware ID, toujours "SEGA SEGAKATANA "
    ///   0x40 (10 octets) : Product number (ex. "MK-51000")
    ///
    /// ⚠️ Non testé avec un vrai disque. Le format du Product number
    /// varie selon l'éditeur (le préfixe "MK-" de Sega est parfois retiré
    /// dans les bases externes) -- voir le repli dans DiscShelfPlugin.
    /// </summary>
    public class DreamcastDetector : IDiscDetector
    {
        private const string HardwareIdMagic = "SEGA";
        private const string HardwareIdMagic2 = "SEGAKATANA";
        private const int ProductNumberOffset = 0x40;
        private const int ProductNumberLength = 10;

        private readonly ILogger logger;

        public PlatformId Platform => PlatformId.Dreamcast;


        public DreamcastDetector()
        {
            logger = LogManager.GetLogger();
        }


        public bool CanHandle(DiscInfo disc)
        {
            if (disc == null || string.IsNullOrEmpty(disc.DriveLetter))
            {
                return false;
            }

            string header = RawSectorReader.ReadAscii(disc.DriveLetter, 0, 16);

            if (header == null)
            {
                return false;
            }

            return header.StartsWith(HardwareIdMagic, StringComparison.Ordinal) &&
                   header.Contains(HardwareIdMagic2);
        }


        public GameIdentity Detect(DiscInfo disc)
        {
            GameIdentity identity = new GameIdentity
            {
                Platform = PlatformId.Dreamcast
            };

            string productNumber = RawSectorReader.ReadAscii(
                disc.DriveLetter,
                ProductNumberOffset,
                ProductNumberLength
            );

            if (string.IsNullOrWhiteSpace(productNumber))
            {
                logger.Info("DreamcastDetector : lecture du Product Number impossible.");

                return identity;
            }

            string trimmed = productNumber.Trim();

            // La base (GameTDB) attend un tiret entre le préfixe et les
            // chiffres (ex. "HDR-0010"), mais le disque le donne souvent
            // sans ("HDR0010") -- confirmé avec un vrai disque (Sega Rally
            // 2, HDR-0010). On l'insère s'il manque.
            identity.Serial = InsertHyphenIfMissing(trimmed);

            logger.Info($"DreamcastDetector : serial détecté = {identity.Serial}");

            return identity;
        }


        private static string InsertHyphenIfMissing(string serial)
        {
            if (serial.Contains("-"))
            {
                return serial;
            }

            Match match = Regex.Match(serial, @"^([A-Za-z]+)([0-9].*)$");

            return match.Success ? $"{match.Groups[1].Value}-{match.Groups[2].Value}" : serial;
        }
    }
}

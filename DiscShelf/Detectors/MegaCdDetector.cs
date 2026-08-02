using System;

using DiscShelf.Core;

using Playnite.SDK;

namespace DiscShelf.Detectors
{
    /// <summary>
    /// Détecte les disques Mega CD / Sega CD en lisant les tout premiers
    /// octets du disque en brut (même mécanisme que Saturn -- l'en-tête
    /// fait partie du programme de boot, pas d'un fichier navigable
    /// normal).
    ///
    /// Structure confirmée par des sources multiples (exemples de code de
    /// boot loader homebrew + brevet Sega décrivant le format) :
    ///   0x000 (16 octets) : identifiant disque, "SEGADISCSYSTEM " ou
    ///                        "SEGABOOTDISC"
    ///   0x100             : en-tête standard Mega Drive/Genesis (le
    ///                        format de cartouche le mieux documenté qui
    ///                        existe)
    ///   0x180 (14 octets) : Serial number (ex. "GM G-6013-00")
    ///
    /// Ce dernier offset est bien corroboré (identique au format cartouche
    /// Mega Drive standard), plus fiable que ce qu'on avait pour Saturn.
    /// </summary>
    public class MegaCdDetector : IDiscDetector
    {
        private const string HardwareIdMagic1 = "SEGADISCSYSTEM ";
        private const string HardwareIdMagic2 = "SEGABOOTDISC";
        private const int SerialOffset = 0x180;
        private const int SerialLength = 14;

        private readonly ILogger logger;

        public PlatformId Platform => PlatformId.MegaCD;


        public MegaCdDetector()
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

            return header.StartsWith(HardwareIdMagic1, StringComparison.Ordinal) ||
                   header.StartsWith(HardwareIdMagic2, StringComparison.Ordinal);
        }


        public GameIdentity Detect(DiscInfo disc)
        {
            GameIdentity identity = new GameIdentity
            {
                Platform = PlatformId.MegaCD
            };

            string serial = RawSectorReader.ReadAscii(disc.DriveLetter, SerialOffset, SerialLength);

            if (string.IsNullOrWhiteSpace(serial))
            {
                logger.Info("MegaCdDetector : lecture du serial impossible.");

                return identity;
            }

            // Diagnostic : on relit une plage plus large de l'en-tête
            // Mega Drive standard (nom console, copyright, titres) pour
            // vérifier que tout le bloc correspond bien au même disque --
            // utile pour distinguer un mauvais offset d'une lecture
            // périmée/mauvais disque.
            string diagnosticBlock = RawSectorReader.ReadAscii(disc.DriveLetter, 0x100, 0x90);

            logger.Info(
                $"MegaCdDetector : bloc d'en-tête 0x100-0x190 = \"{(diagnosticBlock ?? "(lecture échouée)").Replace("\r", "\\r").Replace("\n", "\\n")}\""
            );

            // Le champ fait 14 octets fixes ; le padding se place à des
            // endroits variables selon la longueur du code produit
            // (ex. "GM G-6013 -00" au lieu de "GM G-6013-00"). On retire
            // TOUS les espaces internes plutôt que de les réduire, pour
            // matcher le format sans espace de la base MegaCD.csv.
            identity.Serial = serial.Replace(" ", "").Trim();

            logger.Info($"MegaCdDetector : serial détecté = {identity.Serial}");

            return identity;
        }
    }
}

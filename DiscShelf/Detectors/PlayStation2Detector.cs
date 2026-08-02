using System.Collections.Generic;

using DiscShelf.Core;

using Playnite.SDK;

namespace DiscShelf.Detectors
{
    /// <summary>
    /// Détecte les disques PlayStation 2 via la clé BOOT2 de SYSTEM.CNF :
    ///   BOOT2 = cdrom0:\SLUS_205.02;1
    /// Le serial est ensuite converti du format "disque" (underscore+point)
    /// vers le format utilisé par la base PlayStation2.csv (tiret, sans
    /// point), ex. SLUS_205.02 -> SLUS-20502.
    /// </summary>
    public class PlayStation2Detector : IDiscDetector
    {
        private readonly ILogger logger;

        public PlatformId Platform => PlatformId.PlayStation2;


        public PlayStation2Detector()
        {
            logger = LogManager.GetLogger();
        }


        public bool CanHandle(DiscInfo disc)
        {
            if (disc == null)
            {
                return false;
            }

            Dictionary<string, string> cnf = SystemCnfReader.Parse(disc);

            return cnf.ContainsKey("BOOT2");
        }


        public GameIdentity Detect(DiscInfo disc)
        {
            GameIdentity identity = new GameIdentity
            {
                Platform = PlatformId.PlayStation2
            };

            Dictionary<string, string> cnf = SystemCnfReader.Parse(disc);

            if (!cnf.TryGetValue("BOOT2", out string bootValue))
            {
                logger.Info("PlayStation2Detector : clé BOOT2 absente de SYSTEM.CNF.");

                return identity;
            }

            string diskFormatSerial = SystemCnfReader.NormalizeRawSerial(bootValue);

            identity.Serial = ToDatabaseFormat(diskFormatSerial);

            logger.Info(
                $"PlayStation2Detector : serial détecté = {diskFormatSerial} (base : {identity.Serial})"
            );

            return identity;
        }


        /// <summary>
        /// SLUS_205.02 -> SLUS-20502
        /// </summary>
        private static string ToDatabaseFormat(string diskFormatSerial)
        {
            int underscoreIndex = diskFormatSerial.IndexOf('_');

            if (underscoreIndex < 0)
            {
                return diskFormatSerial;
            }

            string prefix = diskFormatSerial.Substring(0, underscoreIndex);
            string digits = diskFormatSerial.Substring(underscoreIndex + 1).Replace(".", "");

            return prefix + "-" + digits;
        }
    }
}

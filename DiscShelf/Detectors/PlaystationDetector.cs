using System.Collections.Generic;

using DiscShelf.Core;

using Playnite.SDK;

namespace DiscShelf.Detectors
{
    /// <summary>
    /// Détecte les disques PlayStation (PS1) via la clé BOOT de SYSTEM.CNF :
    ///   BOOT = cdrom:\SLPS_020.44;1
    /// (clé exacte, pas un préfixe : "BOOT2" ne doit pas matcher ici)
    /// </summary>
    public class PlaystationDetector : IDiscDetector
    {
        private readonly ILogger logger;

        public PlatformId Platform => PlatformId.PlayStation;


        public PlaystationDetector()
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

            return cnf.ContainsKey("BOOT");
        }


        public GameIdentity Detect(DiscInfo disc)
        {
            GameIdentity identity = new GameIdentity
            {
                Platform = PlatformId.PlayStation
            };

            Dictionary<string, string> cnf = SystemCnfReader.Parse(disc);

            if (!cnf.TryGetValue("BOOT", out string bootValue))
            {
                logger.Info("PlaystationDetector : clé BOOT absente de SYSTEM.CNF.");

                return identity;
            }

            identity.Serial = SystemCnfReader.NormalizeRawSerial(bootValue);

            logger.Info($"PlaystationDetector : serial détecté = {identity.Serial}");

            return identity;
        }
    }
}

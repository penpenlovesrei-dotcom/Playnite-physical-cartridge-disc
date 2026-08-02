using System.IO;

using DiscShelf.Core;

using Playnite.SDK;

namespace DiscShelf.Detectors
{
    /// <summary>
    /// Détecte les disques PlayStation 3 via PS3_GAME\PARAM.SFO, en lisant
    /// la clé TITLE_ID (ex. "BLUS30418"). Contrairement à PS1/PS2, ce
    /// fichier est dans un sous-dossier, pas à la racine, et le format est
    /// binaire (SFO) plutôt que texte.
    /// </summary>
    public class PlayStation3Detector : IDiscDetector
    {
        private readonly ILogger logger;

        public PlatformId Platform => PlatformId.PlayStation3;


        public PlayStation3Detector()
        {
            logger = LogManager.GetLogger();
        }


        public bool CanHandle(DiscInfo disc)
        {
            if (disc == null || string.IsNullOrEmpty(disc.DriveLetter))
            {
                return false;
            }

            return File.Exists(GetParamSfoPath(disc));
        }


        public GameIdentity Detect(DiscInfo disc)
        {
            GameIdentity identity = new GameIdentity
            {
                Platform = PlatformId.PlayStation3
            };

            string sfoPath = GetParamSfoPath(disc);

            string titleId = ParamSfoReader.ReadValue(sfoPath, "TITLE_ID");

            if (string.IsNullOrWhiteSpace(titleId))
            {
                logger.Info("PlayStation3Detector : clé TITLE_ID absente de PARAM.SFO.");

                return identity;
            }

            identity.Serial = titleId.Trim().ToUpperInvariant();

            logger.Info($"PlayStation3Detector : serial détecté = {identity.Serial}");

            return identity;
        }


        private static string GetParamSfoPath(DiscInfo disc)
        {
            return Path.Combine(disc.DriveLetter, "PS3_GAME", "PARAM.SFO");
        }
    }
}

using System;
using System.IO;

using DiscShelf.Core;

using Playnite.SDK;

namespace DiscShelf.Detectors
{
    /// <summary>
    /// Détecte les disques Neo Geo CD via IPL.TXT (obligatoire à la racine
    /// de tout disque de jeu). Stratégie d'identification :
    ///   1. L'étiquette du volume seule est utilisée en priorité (ex. "RB2")
    ///      -- suffisamment discriminante en pratique, pas besoin de la
    ///      combiner à une date.
    ///   2. Si l'étiquette est absente ou vaut "UNTITLED" (non exploitable),
    ///      on se rabat sur la date seule (jour, sans heure/minute/seconde)
    ///      du fichier ABS.TXT.
    /// GameIdentity.Serial contient soit l'étiquette brute, soit
    /// "DATE:YYYY-MM-DD" pour signaler au plugin quelle stratégie de
    /// recherche utiliser dans DiscDatabase (FindByLabelSuffix / FindByDatePrefix).
    /// </summary>
    public class NeoGeoCdDetector : IDiscDetector
    {
        private readonly ILogger logger;

        public PlatformId Platform => PlatformId.NeoGeoCD;


        public NeoGeoCdDetector()
        {
            logger = LogManager.GetLogger();
        }


        public bool CanHandle(DiscInfo disc)
        {
            return disc != null && disc.HasFile("IPL.TXT");
        }


        public GameIdentity Detect(DiscInfo disc)
        {
            GameIdentity identity = new GameIdentity
            {
                Platform = PlatformId.NeoGeoCD
            };

            string label = GetVolumeLabel(disc.DriveLetter);

            if (!string.IsNullOrWhiteSpace(label) &&
                !string.Equals(label.Trim(), "UNTITLED", StringComparison.OrdinalIgnoreCase))
            {
                identity.Serial = label.Trim();

                logger.Info($"NeoGeoCdDetector : étiquette du volume = {identity.Serial}");

                return identity;
            }

            logger.Info("NeoGeoCdDetector : étiquette non exploitable, repli sur la date de ABS.TXT.");

            DiscFile referenceFile = disc.FindFile("ABS.TXT");

            if (referenceFile == null)
            {
                logger.Info("NeoGeoCdDetector : ABS.TXT introuvable, identification impossible.");

                return identity;
            }

            try
            {
                DateTime timestamp = File.GetLastWriteTime(referenceFile.FullPath);

                identity.Serial = "DATE:" + timestamp.ToString("yyyy-MM-dd");

                logger.Info($"NeoGeoCdDetector : repli date = {identity.Serial}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "NeoGeoCdDetector : erreur pendant la lecture de la date de ABS.TXT.");
            }

            return identity;
        }


        private static string GetVolumeLabel(string driveLetter)
        {
            try
            {
                return new DriveInfo(driveLetter).VolumeLabel;
            }
            catch
            {
                return null;
            }
        }
    }
}

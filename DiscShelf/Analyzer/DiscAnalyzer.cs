using System;
using System.IO;

using DiscShelf.Core;

using Playnite.SDK;

namespace DiscShelf.Analyzer
{
    public class DiscAnalyzer
    {
        private readonly ILogger logger;


        public DiscAnalyzer()
        {
            logger = LogManager.GetLogger();
        }


        public DiscInfo Analyze(string drivePath)
        {
            DiscInfo disc = new DiscInfo();

            try
            {
                disc.DriveLetter = drivePath;

                DirectoryInfo root = new DirectoryInfo(drivePath);

                if (!root.Exists)
                {
                    logger.Info(
                        $"DiscAnalyzer : lecteur introuvable ({drivePath})."
                    );

                    return disc;
                }

                // Certains jeux (GameCube/Wii notamment) rangent l'identifiant
                // dans un fichier situé directement à la racine, la lecture
                // ne descend donc que d'un niveau : c'est suffisant pour tous
                // les détecteurs actuels (SYSTEM.CNF, exécutable de boot...).
                foreach (FileInfo file in root.GetFiles())
                {
                    disc.Files.Add(new DiscFile
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        Size = file.Length
                    });
                }

                logger.Info(
                    $"DiscAnalyzer : {disc.Files.Count} fichier(s) trouvé(s) sur {drivePath}."
                );

                // Liste détaillée seulement pour les petits disques (ex.
                // session basse densité Dreamcast) -- utile en diagnostic,
                // éviterait de spammer le log pour un vrai disque de jeu.
                if (disc.Files.Count <= 20)
                {
                    foreach (DiscFile file in disc.Files)
                    {
                        logger.Info($"DiscAnalyzer : fichier = {file.Name} ({file.Size} octets)");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(
                    ex,
                    $"DiscAnalyzer : erreur pendant l'analyse de {drivePath}."
                );
            }

            return disc;
        }
    }
}

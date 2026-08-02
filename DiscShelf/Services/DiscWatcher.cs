using System;
using System.IO;
using System.Threading;

using Playnite.SDK;

namespace DiscShelf.Services
{
    /// <summary>
    /// Surveille un lecteur optique par polling (simple et fiable,
    /// pas besoin de WMI/WndProc). Prévient via deux callbacks distincts
    /// à l'insertion et au retrait d'un disque.
    /// </summary>
    public class DiscWatcher : IDisposable
    {
        private readonly ILogger logger;

        private readonly Action onDiscInserted;

        private readonly Action onDiscRemoved;

        private Timer timer;

        private bool lastDiscState;

        public string CurrentDrive { get; private set; }


        public DiscWatcher(
            Action onDiscInserted,
            Action onDiscRemoved)
        {
            this.onDiscInserted = onDiscInserted;
            this.onDiscRemoved = onDiscRemoved;

            logger = LogManager.GetLogger();
        }


        /// <summary>
        /// Démarre la surveillance. Retourne false si aucun lecteur
        /// optique n'a été trouvé.
        /// </summary>
        public bool Start()
        {
            CurrentDrive = FindOpticalDrive();

            if (string.IsNullOrEmpty(CurrentDrive))
            {
                logger.Info("DiscWatcher : aucun lecteur optique détecté.");

                return false;
            }

            logger.Info($"DiscWatcher : lecteur optique surveillé : {CurrentDrive}");

            // lastDiscState reste à sa valeur par défaut (false) : le premier
            // CheckNow()/CheckDisc() déclenchera onDiscInserted si un disque
            // est déjà présent au démarrage de Playnite.
            timer = new Timer(CheckDisc, null, 2000, 2000);

            return true;
        }


        public void Stop()
        {
            timer?.Dispose();
            timer = null;
        }


        public void Dispose()
        {
            Stop();
        }


        /// <summary>
        /// Force une évaluation immédiate de l'état actuel (utile au démarrage
        /// du plugin pour afficher tout de suite le bon état).
        /// </summary>
        public void CheckNow()
        {
            CheckDisc(null);
        }


        private void CheckDisc(object state)
        {
            try
            {
                bool present = HasDisc(CurrentDrive);

                if (present == lastDiscState)
                {
                    return;
                }

                lastDiscState = present;

                if (present)
                {
                    logger.Info("DiscWatcher : disque inséré.");

                    onDiscInserted?.Invoke();
                }
                else
                {
                    logger.Info("DiscWatcher : disque retiré.");

                    onDiscRemoved?.Invoke();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DiscWatcher : erreur pendant la surveillance.");
            }
        }


        private static bool HasDisc(string drive)
        {
            try
            {
                return new DriveInfo(drive).IsReady;
            }
            catch
            {
                return false;
            }
        }


        private string FindOpticalDrive()
        {
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (drive.DriveType == DriveType.CDRom)
                        {
                            return drive.Name;
                        }
                    }
                    catch
                    {
                        // Lecteur inaccessible temporairement, on continue.
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DiscWatcher : erreur recherche lecteur.");
            }

            return null;
        }
    }
}

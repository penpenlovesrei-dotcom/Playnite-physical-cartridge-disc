using System;
using System.Threading;

using CartridgeShelf.Core;
using CartridgeShelf.Detectors;

using Playnite.SDK;

namespace CartridgeShelf.Services
{
    /// <summary>
    /// Poll un device via un ICartridgeDetector (ex. SnOperatorDetector) et
    /// prévient via deux callbacks distincts à l'insertion et au retrait
    /// d'une cartouche. Structure inspirée de DiscWatcher (Timer, détection
    /// de changement d'état).
    ///
    /// Point important (retenu après debug du script Python) : si
    /// detector.Read() retourne null (état indéterminé -- le device n'a
    /// pas répondu à temps ce cycle), on NE TOUCHE PAS à lastPresentState
    /// et on réessaie au prochain poll. Confondre "pas de réponse cette
    /// fois" avec "cartouche retirée" causait des faux positifs répétés
    /// côté Python.
    /// </summary>
    public class CartridgeWatcher : IDisposable
    {
        private const int PollIntervalMs = 1000;

        private readonly ILogger logger;

        private readonly ICartridgeDetector detector;

        private readonly Action<CartridgeInfo> onCartridgeInserted;

        private readonly Action onCartridgeRemoved;

        private Timer timer;

        private bool? lastPresentState; // null = pas encore évalué


        public CartridgeWatcher(
            ICartridgeDetector detector,
            Action<CartridgeInfo> onCartridgeInserted,
            Action onCartridgeRemoved)
        {
            this.detector = detector;
            this.onCartridgeInserted = onCartridgeInserted;
            this.onCartridgeRemoved = onCartridgeRemoved;

            logger = LogManager.GetLogger();
        }


        /// <summary>
        /// Démarre la surveillance. Retourne false si le device n'est pas
        /// disponible au démarrage (débranché, ou déjà utilisé par
        /// Playback -- ferme Playback avant de lancer Playnite pour que ce
        /// watcher puisse ouvrir le device).
        /// </summary>
        public bool Start()
        {
            if (!detector.CanHandle())
            {
                logger.Info("CartridgeWatcher : device non disponible au démarrage.");

                return false;
            }

            logger.Info("CartridgeWatcher : device détecté, démarrage de la surveillance.");

            timer = new Timer(Poll, null, PollIntervalMs, PollIntervalMs);

            return true;
        }


        public void Stop()
        {
            timer?.Dispose();
            timer = null;

            (detector as IDisposable)?.Dispose();
        }


        public void Dispose()
        {
            Stop();
        }


        public void CheckNow()
        {
            Poll(null);
        }


        private void Poll(object state)
        {
            try
            {
                CartridgeInfo info = detector.Read();

                if (info == null)
                {
                    // État indéterminé ce cycle : on ne touche pas à
                    // lastPresentState, on retente au prochain poll.
                    return;
                }

                if (lastPresentState.HasValue && info.Present == lastPresentState.Value)
                {
                    return;
                }

                lastPresentState = info.Present;

                if (info.Present)
                {
                    logger.Info("CartridgeWatcher : cartouche insérée.");

                    onCartridgeInserted?.Invoke(info);
                }
                else
                {
                    logger.Info("CartridgeWatcher : cartouche retirée.");

                    onCartridgeRemoved?.Invoke();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "CartridgeWatcher : erreur pendant la surveillance.");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Playnite.SDK;

namespace DigitalShelf.Services
{
    /// <summary>
    /// Transition mosaïque façon Super Nintendo, jouée pendant le changement
    /// de vue. Un thème Fullscreen ne peut pas produire cet effet : il
    /// n'autorise ni code-behind ni shader, et WPF ne fournit en standard que
    /// le flou et l'ombre portée. On passe donc par une fenêtre superposée,
    /// créée le temps de la transition puis refermée.
    ///
    /// Principe : on capture l'écran, on le réaffiche à résolution
    /// décroissante en interpolation « plus proche voisin » (d'où les gros
    /// carrés), on bascule la vue au maximum de pixelisation, puis on
    /// recapture et on remonte en résolution.
    ///
    /// L'animation est pilotée par CompositionTarget.Rendering, c'est-à-dire
    /// par la boucle de rendu de WPF elle-même. Un DispatcherTimer, utilisé
    /// auparavant, n'est pas synchronisé avec le rafraîchissement de l'écran :
    /// ses intervalles dérivent et l'animation avance par à-coups. Ici le
    /// facteur de pixelisation est recalculé à chaque image effectivement
    /// rendue, à partir du temps écoulé, donc indépendamment du débit
    /// d'images.
    ///
    /// Interrupteur : l'effet ne s'active que si le fichier mosaic_enabled.txt
    /// est présent à la racine du plugin. Le supprimer désactive la
    /// transition immédiatement, sans recompilation ni redémarrage.
    /// </summary>
    public class MosaicTransition
    {
        public const string EnableFileName = "mosaic_enabled.txt";

        /// <summary>
        /// Hauteur des bandeaux blancs du thème, exclus de l'effet. Valeurs
        /// reprises de Views\Main.xaml, où les grilles des lignes 0 et 5
        /// déclarent toutes deux Height="100".
        /// </summary>
        private const double TopBarHeight = 100;

        private const double BottomBarHeight = 100;

        /// <summary>
        /// La capture est faite à résolution réduite : mesuré à pleine
        /// résolution, un rendu de la fenêtre coûtait entre 56 et 64 ms, contre
        /// 15 à 18 ms ici. La perte de netteté est sans conséquence, l'image ne
        /// servant qu'à être pixelisée.
        /// </summary>
        private const double CaptureScale = 0.5;

        /// <summary>
        /// Facteurs de réduction appliqués à la capture. Celle-ci étant déjà à
        /// demi-résolution, le facteur maximum donne des carrés de 56 pixels à
        /// l'écran. La série ne commence pas à 1 : un facteur 1 afficherait une
        /// image identique à l'écran, donnant l'impression d'un effet déclenché
        /// en retard.
        /// </summary>
        private const double MinFactor = 2;

        private const double MaxFactor = 28;

        /// <summary>
        /// Durée de chaque moitié de l'animation. Allongée par rapport aux
        /// 200 ms initiales, où l'effet passait trop vite pour être lu.
        /// </summary>
        private const double PhaseDurationMs = 320;

        /// <summary>
        /// Temps passé au maximum de pixelisation, entre le changement de vue
        /// et la capture de la nouvelle image. Playnite charge les jaquettes de
        /// façon asynchrone (réglage AsyncImageLoading) : capturer aussitôt
        /// après la bascule saisissait des emplacements encore vides, qui
        /// apparaissaient ensuite en se dépixelisant. Ce délai leur laisse le
        /// temps de s'afficher, dissimulé derrière les gros carrés.
        /// </summary>
        private const double SwitchHoldMs = 220;

        private readonly IPlayniteAPI api;

        private readonly ILogger logger;

        private readonly string pluginFolder;

        private Session session;


        public MosaicTransition(IPlayniteAPI api, ILogger logger, string pluginFolder)
        {
            this.api = api;
            this.logger = logger;
            this.pluginFolder = pluginFolder;
        }


        public bool IsEnabled
        {
            get
            {
                try
                {
                    return File.Exists(Path.Combine(pluginFolder, EnableFileName));
                }
                catch
                {
                    return false;
                }
            }
        }


        /// <summary>
        /// Démarre la transition et lui confie l'action de bascule, qu'elle
        /// déclenche au maximum de pixelisation. En cas de problème à n'importe
        /// quelle étape, cette action est exécutée malgré tout : l'effet est
        /// décoratif, il ne doit jamais empêcher le changement de vue.
        /// </summary>
        public void Play(Action switchAction)
        {
            if (switchAction == null)
            {
                return;
            }

            if (!IsEnabled)
            {
                switchAction();

                return;
            }

            // Le gel de l'écran doit suivre l'appui sur le bouton sans le
            // moindre détour : si on est déjà sur le thread d'interface, on
            // exécute directement plutôt que de mettre la capture en file
            // d'attente.
            Action start = () =>
            {
                try
                {
                    if (session != null)
                    {
                        Abandon();
                    }

                    if (!Start(switchAction))
                    {
                        switchAction();
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "DigitalShelf : transition mosaïque impossible, bascule directe.");

                    Abandon();

                    switchAction();
                }
            };

            if (api.MainView.UIDispatcher.CheckAccess())
            {
                start();
            }
            else
            {
                api.MainView.UIDispatcher.Invoke(DispatcherPriority.Send, start);
            }
        }


        private bool Start(Action switchAction)
        {
            Window main = Application.Current?.MainWindow;

            if (main == null || main.ActualWidth < 1 || main.ActualHeight < 1)
            {
                logger.Warn("DigitalShelf : fenêtre principale indisponible, transition ignorée.");

                return false;
            }

            double regionHeight = main.ActualHeight - TopBarHeight - BottomBarHeight;

            if (regionHeight < 1)
            {
                logger.Warn("DigitalShelf : zone centrale trop petite, transition ignorée.");

                return false;
            }

            Stopwatch measure = Stopwatch.StartNew();

            BitmapSource region = CaptureRegion(main, regionHeight);

            if (region == null)
            {
                return false;
            }

            Image image = new Image { Stretch = Stretch.Fill };

            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

            Window overlay = CreateOverlay(main, image, regionHeight);

            session = new Session
            {
                Main = main,
                RegionHeight = regionHeight,
                Overlay = overlay,
                Image = image,
                Region = region,
                SwitchAction = switchAction,
                Phase = TransitionPhase.PixelatingOut
            };

            // La toute première image affichée est déjà pixelisée : l'effet est
            // donc visible dès le premier rendu de la superposition.
            image.Source = GetFrame(session, MinFactor);

            overlay.Show();

            logger.Debug($"DigitalShelf : superposition prête en {measure.ElapsedMilliseconds} ms.");

            session.Watch.Start();

            CompositionTarget.Rendering += OnRendering;

            return true;
        }


        /// <summary>
        /// Appelé par WPF avant chaque image rendue. Tout est piloté par le
        /// temps écoulé : si une image est sautée, l'animation continue au bon
        /// endroit au lieu de prendre du retard.
        /// </summary>
        private void OnRendering(object sender, EventArgs e)
        {
            Session current = session;

            if (current == null)
            {
                return;
            }

            try
            {
                double elapsed = current.Watch.Elapsed.TotalMilliseconds;
                double progress = Math.Min(1.0, elapsed / PhaseDurationMs);

                switch (current.Phase)
                {
                    case TransitionPhase.PixelatingOut:
                        Show(current, Interpolate(progress));

                        if (progress >= 1.0)
                        {
                            Action action = current.SwitchAction;
                            current.SwitchAction = null;

                            action?.Invoke();

                            current.Watch.Restart();
                            current.Phase = TransitionPhase.HoldingAfterSwitch;
                        }

                        return;

                    case TransitionPhase.HoldingAfterSwitch:
                        {
                            // On reste au maximum de pixelisation le temps que
                            // Playnite charge les jaquettes de la nouvelle vue.
                            Show(current, MaxFactor);

                            if (elapsed < SwitchHoldMs)
                            {
                                return;
                            }

                            BitmapSource newRegion = CaptureRegion(current.Main, current.RegionHeight);

                            if (newRegion != null)
                            {
                                current.Region = newRegion;
                                current.Cache.Clear();
                                current.CurrentFactor = -1;
                            }

                            Show(current, MaxFactor);

                            current.Watch.Restart();
                            current.Phase = TransitionPhase.PixelatingIn;

                            return;
                        }

                    case TransitionPhase.PixelatingIn:
                        Show(current, Interpolate(1.0 - progress));

                        if (progress >= 1.0)
                        {
                            Abandon();
                        }

                        return;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DigitalShelf : erreur pendant la transition mosaïque.");

                Action pending = current.SwitchAction;

                Abandon();

                pending?.Invoke();
            }
        }


        /// <summary>
        /// Progression géométrique plutôt que linéaire : à l'œil, passer de 2 à
        /// 4 est un saut aussi marqué que de 14 à 28, donc c'est le rapport
        /// entre facteurs qui doit progresser régulièrement, pas leur
        /// différence.
        /// </summary>
        private static double Interpolate(double progress)
        {
            return MinFactor * Math.Pow(MaxFactor / MinFactor, Math.Max(0.0, Math.Min(1.0, progress)));
        }


        private void Show(Session current, double factor)
        {
            int rounded = (int)Math.Round(factor);

            if (rounded == current.CurrentFactor)
            {
                return;
            }

            current.CurrentFactor = rounded;
            current.Image.Source = GetFrame(current, rounded);
        }


        /// <summary>
        /// Fabrique le palier demandé à la volée, puis le conserve : chaque
        /// image rendue ne produit au plus qu'une seule image réduite,
        /// opération négligeable.
        /// </summary>
        private BitmapSource GetFrame(Session current, double factor)
        {
            int rounded = Math.Max(1, (int)Math.Round(factor));

            if (current.Cache.TryGetValue(rounded, out BitmapSource cached))
            {
                return cached;
            }

            try
            {
                BitmapSource built = Pixelate(current.Region, rounded);

                current.Cache[rounded] = built;

                return built;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"DigitalShelf : palier de pixelisation {rounded} impossible.");

                return current.Region;
            }
        }


        /// <summary>
        /// Arrête l'animation et referme la superposition. Sûr à appeler
        /// plusieurs fois.
        /// </summary>
        private void Abandon()
        {
            Session current = session;

            session = null;

            if (current == null)
            {
                return;
            }

            CompositionTarget.Rendering -= OnRendering;

            try
            {
                current.Overlay?.Close();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DigitalShelf : fermeture de la superposition impossible.");
            }
        }


        private BitmapSource CaptureRegion(Window main, double regionHeight)
        {
            try
            {
                int width = (int)Math.Ceiling(main.ActualWidth * CaptureScale);
                int height = (int)Math.Ceiling(main.ActualHeight * CaptureScale);

                if (width <= 0 || height <= 0)
                {
                    return null;
                }

                // Réduire la résolution de sortie revient à demander à WPF de
                // rastériser la fenêtre à cette échelle : c'est là qu'est
                // l'économie, pas seulement sur la taille de l'image.
                RenderTargetBitmap full = new RenderTargetBitmap(
                    width, height, 96 * CaptureScale, 96 * CaptureScale, PixelFormats.Pbgra32
                );

                full.Render(main);
                full.Freeze();

                int cropTop = (int)(TopBarHeight * CaptureScale);

                Int32Rect crop = new Int32Rect(
                    0,
                    cropTop,
                    width,
                    Math.Min((int)(regionHeight * CaptureScale), height - cropTop)
                );

                CroppedBitmap cropped = new CroppedBitmap(full, crop);
                cropped.Freeze();

                return cropped;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DigitalShelf : capture de la zone centrale impossible.");

                return null;
            }
        }


        /// <summary>
        /// Fenêtre sans bordure, non activable et transparente aux clics :
        /// elle ne doit ni voler le focus manette, ni intercepter la
        /// navigation. Elle ne couvre que la zone centrale, les bandeaux du
        /// thème restant visibles et nets.
        /// </summary>
        private Window CreateOverlay(Window main, Image content, double regionHeight)
        {
            Point origin = new Point(main.Left, main.Top + TopBarHeight);

            try
            {
                PresentationSource source = PresentationSource.FromVisual(main);

                if (source != null)
                {
                    Point device = main.PointToScreen(new Point(0, TopBarHeight));

                    origin = source.CompositionTarget.TransformFromDevice.Transform(device);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DigitalShelf : position exacte introuvable, repli sur la position de la fenêtre.");
            }

            return new Window
            {
                Owner = main,
                Content = content,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Focusable = false,
                IsHitTestVisible = false,
                Background = Brushes.Black,
                Left = origin.X,
                Top = origin.Y,
                Width = main.ActualWidth,
                Height = regionHeight
            };
        }


        /// <summary>
        /// Réduit l'image d'un facteur donné. Réaffichée en plein écran avec
        /// l'interpolation « plus proche voisin », elle produit les carrés
        /// caractéristiques de l'effet mosaïque.
        /// </summary>
        private static BitmapSource Pixelate(BitmapSource source, int factor)
        {
            if (source == null || factor <= 1)
            {
                return source;
            }

            double scale = 1.0 / factor;

            TransformedBitmap reduced = new TransformedBitmap(
                source, new ScaleTransform(scale, scale)
            );

            CachedBitmap cached = new CachedBitmap(
                reduced, BitmapCreateOptions.None, BitmapCacheOption.OnLoad
            );

            cached.Freeze();

            return cached;
        }


        private enum TransitionPhase
        {
            PixelatingOut,
            HoldingAfterSwitch,
            PixelatingIn
        }


        private class Session
        {
            public Window Main;

            public double RegionHeight;

            public Window Overlay;

            public Image Image;

            /// <summary>Capture servant de base à tous les paliers.</summary>
            public BitmapSource Region;

            /// <summary>Paliers déjà fabriqués, indexés par facteur.</summary>
            public readonly Dictionary<int, BitmapSource> Cache = new Dictionary<int, BitmapSource>();

            public TransitionPhase Phase;

            public int CurrentFactor = -1;

            public Action SwitchAction;

            public readonly Stopwatch Watch = new Stopwatch();
        }
    }
}

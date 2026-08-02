using System;
using System.Windows.Threading;

using DigitalShelf.Core;
using DigitalShelf.Services;

using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace DigitalShelf.Controllers
{
    /// <summary>
    /// Contrôleur de "lancement" de la tuile. C'est le point d'interception
    /// qui permet d'exécuter du code plutôt que de démarrer un jeu :
    /// valider la tuile bascule la bibliothèque vers l'autre vue.
    /// </summary>
    public class SwitchViewPlayController : PlayController
    {
        private readonly IPlayniteAPI api;

        private readonly ILogger logger;

        private readonly FilterPresetManager presetManager;

        private readonly ShelfSlotManager slotManager;

        private readonly MosaicTransition transition;

        private readonly TransitionSounds sounds;


        public SwitchViewPlayController(
            Game game,
            IPlayniteAPI api,
            ILogger logger,
            FilterPresetManager presetManager,
            ShelfSlotManager slotManager,
            MosaicTransition transition,
            TransitionSounds sounds)
            : base(game)
        {
            this.api = api;
            this.logger = logger;
            this.presetManager = presetManager;
            this.slotManager = slotManager;
            this.transition = transition;
            this.sounds = sounds;

            Name = "Ouvrir";
        }


        public override void Play(PlayActionArgs args)
        {
            ShelfView current = presetManager.GetCurrentView();

            ShelfView target = current == ShelfView.Console
                ? ShelfView.Digital
                : ShelfView.Console;

            Guid presetId = presetManager.GetPresetId(target);

            if (presetId == Guid.Empty)
            {
                logger.Error(
                    $"DigitalShelf : préréglage introuvable pour la vue {target}, bascule annulée."
                );

                InvokeOnStopped(new GameStoppedEventArgs(0));

                return;
            }

            // Le changement de vue déplace la sélection, ce qui déclencherait
            // un son de navigation par-dessus ceux de la transition. On le
            // neutralise sur toute la durée de celle-ci, un peu au-delà de ses
            // 860 ms pour couvrir le déplacement de sélection qui la suit.
            sounds.MuteNavigationFor(1400);

            // Joué tout de suite : c'est la première chose faite après l'appui
            // sur le bouton, donc le retour le plus immédiat possible, avant
            // même que la transition visuelle n'ait commencé.
            sounds.PlayPress();

            // Volontairement pas d'InvokeOnStarted : cet événement fait
            // considérer à Playnite qu'un vrai jeu démarre, ce qui déclenche
            // sa minimisation automatique -- d'où un aller-retour visible
            // par le bureau Windows entre les deux vues. Seul Stopped est
            // notifié, pour solder la séquence de lancement sans que
            // l'interface ne se retire.
            //
            // Cette notification doit rester ICI, avant la transition : la
            // repousser après laisse à Playnite le temps d'afficher sa fenêtre
            // de session de jeu, visible en arrière-plan pendant la bascule.
            InvokeOnStopped(new GameStoppedEventArgs(0));

            // La bascule est confiée à la transition, qui la déclenche au
            // moment le plus pixelisé. Si l'effet est désactivé ou échoue,
            // elle est exécutée directement.
            transition.Play(() =>
            {
                try
                {
                    slotManager.Render(target);

                    api.MainView.ApplyFilterPreset(presetId);

                    // Au moment où la page d'arrivée s'installe : avec la
                    // mosaïque, c'est l'instant où la dépixelisation commence
                    // à la révéler.
                    sounds.PlayOpen();

                    logger.Info($"DigitalShelf : bascule vers la vue {target}.");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"DigitalShelf : échec de la bascule vers la vue {target}.");
                }
            });
        }


        public override void Dispose()
        {
            base.Dispose();
        }
    }
}

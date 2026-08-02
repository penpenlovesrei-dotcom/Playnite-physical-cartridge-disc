using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

using DigitalShelf.Controllers;
using DigitalShelf.Core;
using DigitalShelf.Services;

using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace DigitalShelf
{
    /// <summary>
    /// Ajoute une troisième jaquette à la rangée du thème Fullscreen, à côté
    /// des jaquettes cartouche (CartridgeShelf) et CD (DiscShelf). Valider
    /// cette jaquette ne lance pas de jeu : elle bascule la bibliothèque
    /// vers les plateformes numériques (Steam, Epic, GOG...) puis, dans
    /// cette vue, sert de retour vers la vue console.
    ///
    /// LibraryPlugin et non GenericPlugin comme ses deux aînés : c'est le
    /// modèle où Playnite reconnaît le plugin comme propriétaire de ses
    /// jeux, et consulte donc GetPlayActions pour la tuile.
    /// </summary>
    public class DigitalShelfPlugin : LibraryPlugin
    {
        private readonly ILogger logger;

        private FilterPresetManager presetManager;

        private ShelfSlotManager slotManager;

        private MosaicTransition transition;

        private TransitionSounds sounds;


        public override Guid Id =>
            Guid.Parse("f7ba5ce6-190b-47ed-ba0c-59928375d2a1");


        public override string Name => "DigitalShelf";


        public DigitalShelfPlugin(IPlayniteAPI api)
            : base(api)
        {
            logger = LogManager.GetLogger();

            presetManager = new FilterPresetManager(api, logger, Id);

            slotManager = new ShelfSlotManager(
                api,
                logger,
                Id,
                GetPluginUserDataPath(),
                Path.Combine(GetPluginAssetPath(), "digital_game.png"),
                Path.Combine(GetPluginAssetPath(), "console_return.png")
            );

            transition = new MosaicTransition(api, logger, GetPluginFolder());

            sounds = new TransitionSounds(api, logger, GetPluginFolder());

            logger.Info("DigitalShelf : plugin chargé, attente du démarrage complet de Playnite.");
        }


        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);

            try
            {
                // Le démarrage applique un préréglage et pose la sélection, ce
                // qui déclencherait un son de navigation par-dessus le son
                // d'intro joué par le thème.
                sounds.MuteNavigationFor(5000);

                presetManager.EnsurePresets();

                // On impose la vue console au démarrage plutôt que de suivre
                // le préréglage mémorisé par Playnite. Deux raisons : on
                // repart toujours de l'écran attendu (les trois jaquettes),
                // et surtout le filtre restauré par Playnite l'est avant que
                // ce plugin ait pu mettre ses préréglages à jour, ce qui
                // laissait au premier lancement les jeux Steam affichés à la
                // suite des trois jaquettes.
                ApplyView(ShelfView.Console);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DigitalShelf : erreur pendant l'initialisation.");
            }
        }


        /// <summary>
        /// Un import de bibliothèque (celui de Steam au premier lancement,
        /// par exemple) se termine après OnApplicationStarted et réinitialise
        /// l'affichage. On réaffirme donc le préréglage courant une fois
        /// l'import fini, sans quoi la liste reste montrée sans filtre.
        /// </summary>
        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            base.OnLibraryUpdated(args);

            try
            {
                ApplyView(presetManager.GetCurrentView());
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DigitalShelf : erreur après mise à jour de la bibliothèque.");
            }
        }


        /// <summary>
        /// Joue le son de déplacement. C'est le plugin qui s'en charge depuis
        /// que les sons d'interface de l'extension Playnite Sounds ont été
        /// désactivés : les siens se superposaient à ceux de la transition, et
        /// il n'est pas possible de les faire taire ponctuellement.
        ///
        /// Vérifié par instrumentation : cet événement suit fidèlement chaque
        /// déplacement, dans l'ordre, aussi bien sur les trois jaquettes que
        /// dans la liste Steam.
        /// </summary>
        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            base.OnGameSelected(args);

            try
            {
                sounds.PlayNavigation();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DigitalShelf : erreur pendant le son de navigation.");
            }
        }


        /// <summary>
        /// Met la tuile dans l'état voulu puis applique le préréglage
        /// correspondant. Toujours differé sur le thread d'interface : au
        /// démarrage comme après un import, Playnite est encore en train de
        /// construire sa vue au moment où on est appelé.
        /// </summary>
        private void ApplyView(ShelfView view)
        {
            Guid presetId = presetManager.GetPresetId(view);

            if (presetId == Guid.Empty)
            {
                logger.Error($"DigitalShelf : préréglage introuvable pour la vue {view}.");

                return;
            }

            PlayniteApi.MainView.UIDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    try
                    {
                        slotManager.Render(view);

                        PlayniteApi.MainView.ApplyFilterPreset(presetId);

                        logger.Info($"DigitalShelf : vue {view} appliquée.");
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"DigitalShelf : échec de l'application de la vue {view}.");
                    }
                })
            );
        }


        /// <summary>
        /// L'unique "jeu" de cette bibliothèque est la tuile. La déclarer
        /// ici la rend légitime aux yeux de Playnite : elle survit aux mises
        /// à jour de bibliothèque au lieu d'être vue comme une entrée
        /// orpheline. L'identité (GameId + PluginId) est la même que celle
        /// posée par ShelfSlotManager, donc pas de doublon.
        /// </summary>
        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            ShelfView view = presetManager.GetCurrentView();

            string coverPath = Path.Combine(
                GetPluginAssetPath(),
                view == ShelfView.Console ? "digital_game.png" : "console_return.png"
            );

            GameMetadata metadata = new GameMetadata
            {
                GameId = ShelfSlotManager.SlotGameId,
                Name = ShelfSlotManager.GetStateName(view),
                SortingName = ShelfSlotManager.PinnedSortingName,
                IsInstalled = true
            };

            if (File.Exists(coverPath))
            {
                metadata.CoverImage = new MetadataFile(coverPath);
            }

            return new List<GameMetadata> { metadata };
        }


        /// <summary>
        /// Point d'interception : au lieu d'une action de lancement
        /// classique, la tuile reçoit un contrôleur qui bascule la vue.
        /// </summary>
        public override IEnumerable<PlayController> GetPlayActions(GetPlayActionsArgs args)
        {
            if (args?.Game == null ||
                args.Game.PluginId != Id ||
                args.Game.GameId != ShelfSlotManager.SlotGameId)
            {
                return null;
            }

            return new List<PlayController>
            {
                new SwitchViewPlayController(
                    args.Game, PlayniteApi, logger, presetManager, slotManager, transition, sounds)
            };
        }


        /// <summary>
        /// Les jaquettes vivent dans le sous-dossier Assets du plugin, à
        /// côté du DLL (même convention que CartridgeShelf).
        /// </summary>
        private string GetPluginAssetPath()
        {
            return Path.Combine(GetPluginFolder(), "Assets");
        }


        private string GetPluginFolder()
        {
            return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }
    }
}

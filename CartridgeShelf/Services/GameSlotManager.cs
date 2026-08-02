using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using CartridgeShelf.Core;
using CartridgeShelf.Database;

using Playnite.SDK;
using Playnite.SDK.Models;

namespace CartridgeShelf.Services
{
    /// <summary>
    /// Représente et met à jour, dans la base Playnite, l'unique entrée
    /// "jeu" qui reflète l'état courant du lecteur de cartouches : soit le
    /// jeu actuellement inséré, soit le placeholder no_cartridge quand
    /// aucune cartouche n'est présente. Adapté de DiscShelf.GameSlotManager.
    /// </summary>
    public class GameSlotManager
    {
        private const string SlotGameId = "CARTRIDGESHELF_SLOT";

        // "!1" : place la jaquette cartouche en dernier des trois, derrière
        // la tuile DigitalShelf ("!!") et la jaquette CD ("!0"). Ordre voulu
        // dans le thème Fullscreen : bibliothèque numérique, CD, cartouche.
        private const string PinnedSortingName = "!1";

        private readonly IPlayniteAPI api;

        private readonly ILogger logger;

        private readonly Guid pluginId;

        private readonly CoverDownloader coverDownloader;

        private readonly PlatformResolver platformResolver;

        private readonly string noCartridgeCoverPath;


        public GameSlotManager(
            IPlayniteAPI api,
            ILogger logger,
            Guid pluginId,
            CoverDownloader coverDownloader,
            PlatformResolver platformResolver,
            string noCartridgeCoverPath)
        {
            this.api = api;
            this.logger = logger;
            this.pluginId = pluginId;
            this.coverDownloader = coverDownloader;
            this.platformResolver = platformResolver;
            this.noCartridgeCoverPath = noCartridgeCoverPath;
        }


        public void ShowNoCartridge()
        {
            Game game = RecreateSlot();

            game.Name = "Aucune cartouche";
            game.SortingName = PinnedSortingName;
            game.PlatformIds = new List<Guid>();
            game.GameActions = new ObservableCollection<GameAction>();
            game.IsInstalled = false;

            SetCover(game, noCartridgeCoverPath);

            api.Database.Games.Update(game);

            logger.Info("GameSlotManager : affichage no_cartridge.");
        }


        public void ShowUnknownCartridge(GameIdentity identity)
        {
            Game game = RecreateSlot();

            game.Name = $"Cartouche non identifiée ({identity.Checksum})";
            game.SortingName = PinnedSortingName;
            game.PlatformIds = new List<Guid> { platformResolver.GetOrCreate(identity.Platform) };
            game.GameActions = new ObservableCollection<GameAction>();
            game.IsInstalled = false;

            SetCover(game, noCartridgeCoverPath);

            api.Database.Games.Update(game);

            logger.Info(
                $"GameSlotManager : cartouche non identifiée ({identity.Checksum})."
            );
        }


        public void ShowGame(
            GameIdentity identity,
            CartridgeEntry entry,
            UserLibraryEntry userEntry)
        {
            Game game = RecreateSlot();

            game.Name = entry.Title;
            game.SortingName = PinnedSortingName;
            game.PlatformIds = new List<Guid> { platformResolver.GetOrCreate(identity.Platform) };

            string cover = coverDownloader.GetCover(identity.Checksum, identity.Platform, entry.Title, entry.Region);
            SetCover(game, cover ?? noCartridgeCoverPath);

            ObservableCollection<GameAction> actions = new ObservableCollection<GameAction>();

            if (userEntry != null && File.Exists(userEntry.EmulatorPath))
            {
                actions.Add(new GameAction
                {
                    Name = "Lancer",
                    Type = GameActionType.File,
                    Path = userEntry.EmulatorPath,
                    Arguments = userEntry.BuildArguments(),
                    IsPlayAction = true
                });
            }
            else
            {
                logger.Info(
                    $"GameSlotManager : pas d'entrée UserLibrary (ou émulateur introuvable) pour {identity.Checksum}, jeu affiché sans action de lancement."
                );
            }

            game.GameActions = actions;
            game.IsInstalled = actions.Count > 0;

            api.Database.Games.Update(game);

            logger.Info($"GameSlotManager : affichage de {entry.Title}.");
        }


        /// <summary>
        /// Supprime puis recrée entièrement l'entrée du slot (au lieu de
        /// muter en place le même objet Game) : le rendu Fullscreen
        /// (ListBox virtualisée) ne rafraîchit pas toujours visuellement
        /// un item existant dont les propriétés changent (Name,
        /// CoverImage), même quand la mise à jour en base réussit (voir
        /// journal : "affichage de ..." sans erreur mais jaquette non
        /// rafraîchie à l'écran). Un vrai Remove + Add force la
        /// régénération complète du container de la ListBox pour ce slot.
        /// </summary>
        private Game RecreateSlot()
        {
            Game existing = api.Database.Games
                .FirstOrDefault(g => g.PluginId == pluginId && g.GameId == SlotGameId);

            if (existing != null)
            {
                if (!string.IsNullOrEmpty(existing.CoverImage))
                {
                    try
                    {
                        api.Database.RemoveFile(existing.CoverImage);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "GameSlotManager : erreur pendant la suppression de l'ancienne jaquette.");
                    }
                }

                api.Database.Games.Remove(existing);
            }

            Game game = new Game("Aucune cartouche")
            {
                GameId = SlotGameId,
                PluginId = pluginId,
                SortingName = PinnedSortingName,
                Added = DateTime.Now,
                PlatformIds = new List<Guid>(),
                GenreIds = new List<Guid>(),
                CategoryIds = new List<Guid>(),
                TagIds = new List<Guid>(),
                FeatureIds = new List<Guid>(),
                PublisherIds = new List<Guid>(),
                DeveloperIds = new List<Guid>(),
                SeriesIds = new List<Guid>(),
                AgeRatingIds = new List<Guid>(),
                RegionIds = new List<Guid>(),
                GameActions = new ObservableCollection<GameAction>(),
                Links = new ObservableCollection<Link>(),
                Roms = new ObservableCollection<GameRom>()
            };

            api.Database.Games.Add(game);

            return game;
        }


        private void SetCover(Game game, string sourceFilePath)
        {
            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                return;
            }

            try
            {
                if (!string.IsNullOrEmpty(game.CoverImage))
                {
                    api.Database.RemoveFile(game.CoverImage);
                }

                // Le slot est un objet Game unique et réutilisé : si le
                // fichier stocké porte toujours le même nom d'une mise à
                // jour à l'autre (ex. no_cartridge.png), le chemin final
                // peut rester identique et l'UI (WPF met en cache les
                // images par chemin/URI) peut continuer d'afficher
                // l'ancienne jaquette malgré le changement de contenu sur
                // disque. On force un nom de fichier unique à chaque appel
                // pour garantir l'invalidation du cache d'image.
                string uniqueName = Guid.NewGuid().ToString("N") + Path.GetExtension(sourceFilePath);
                string tempPath = Path.Combine(Path.GetTempPath(), uniqueName);
                File.Copy(sourceFilePath, tempPath, overwrite: true);

                try
                {
                    game.CoverImage = api.Database.AddFile(tempPath, game.Id);
                }
                finally
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "GameSlotManager : erreur pendant la mise à jour de la jaquette.");
            }
        }
    }
}

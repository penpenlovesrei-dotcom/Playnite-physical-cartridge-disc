using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using DiscShelf.Core;
using DiscShelf.Database;

using Playnite.SDK;
using Playnite.SDK.Models;

namespace DiscShelf.Services
{
    /// <summary>
    /// Représente et met à jour, dans la base Playnite, l'unique entrée
    /// "jeu" qui reflète l'état courant du lecteur optique : soit le jeu
    /// actuellement inséré, soit le placeholder no_disc quand le lecteur
    /// est vide. Ce n'est PAS un catalogue qui s'accumule au fil des
    /// disques insérés : une seule entrée est créée puis mise à jour en
    /// place, conformément à la spec (le disque retiré n'est pas conservé
    /// dans l'affichage).
    /// </summary>
    public class GameSlotManager
    {
        private const string SlotGameId = "DISCSHELF_SLOT";

        // Force le slot en tête de liste quel que soit le tri alphabétique
        // (le "!" trie avant les lettres et les chiffres dans la plupart
        // des cultures ; à ajuster si ce n'est pas le cas chez toi).
        // "!0" : place la jaquette CD au milieu des trois, derrière la tuile
        // DigitalShelf ("!!") et devant la jaquette cartouche ("!1").
        private const string PinnedSortingName = "!0";

        private readonly IPlayniteAPI api;

        private readonly ILogger logger;

        private readonly Guid pluginId;

        private readonly CoverDownloader coverDownloader;

        private readonly PlatformResolver platformResolver;

        private readonly string noDiscCoverPath;


        public GameSlotManager(
            IPlayniteAPI api,
            ILogger logger,
            Guid pluginId,
            CoverDownloader coverDownloader,
            PlatformResolver platformResolver,
            string noDiscCoverPath)
        {
            this.api = api;
            this.logger = logger;
            this.pluginId = pluginId;
            this.coverDownloader = coverDownloader;
            this.platformResolver = platformResolver;
            this.noDiscCoverPath = noDiscCoverPath;
        }


        public void ShowNoDisc()
        {
            Game game = RecreateSlot();

            game.Name = "Aucun disque";
            game.SortingName = PinnedSortingName;
            game.PlatformIds = new List<Guid>();
            game.GameActions = new ObservableCollection<GameAction>();
            game.IsInstalled = false;

            SetCover(game, noDiscCoverPath);

            api.Database.Games.Update(game);

            logger.Info("GameSlotManager : affichage no_disc.");
        }


        public void ShowUnknownDisc(GameIdentity identity)
        {
            Game game = RecreateSlot();

            game.Name = $"Disque non identifié ({identity.Serial})";
            game.SortingName = PinnedSortingName;
            game.PlatformIds = new List<Guid> { platformResolver.GetOrCreate(identity.Platform) };
            game.GameActions = new ObservableCollection<GameAction>();
            game.IsInstalled = false;

            SetCover(game, noDiscCoverPath);

            api.Database.Games.Update(game);

            logger.Info(
                $"GameSlotManager : disque non identifié ({identity.Serial})."
            );
        }


        public void ShowGame(
            GameIdentity identity,
            DiscEntry entry,
            UserLibraryEntry userEntry)
        {
            Game game = RecreateSlot();

            game.Name = entry.Title;
            game.SortingName = PinnedSortingName;
            game.PlatformIds = new List<Guid> { platformResolver.GetOrCreate(identity.Platform) };

            string cover = coverDownloader.GetCover(identity.Serial, identity.Platform, entry.Title);
            SetCover(game, cover ?? noDiscCoverPath);

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
                    $"GameSlotManager : pas d'entrée UserLibrary (ou émulateur introuvable) pour {identity.Serial}, jeu affiché sans action de lancement."
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
        /// CoverImage), même quand la mise à jour en base réussit. Bug
        /// identifié et corrigé sur CartridgeShelf (même architecture) :
        /// un vrai Remove + Add force la régénération complète du
        /// container de la ListBox pour ce slot.
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

            Game game = new Game("Aucun disque")
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

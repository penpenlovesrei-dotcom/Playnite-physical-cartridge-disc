using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using DigitalShelf.Core;

using Playnite.SDK;
using Playnite.SDK.Models;

namespace DigitalShelf.Services
{
    /// <summary>
    /// Gère l'unique faux jeu qui matérialise la tuile. Contrairement aux
    /// slots de CartridgeShelf/DiscShelf qui reflètent un support physique,
    /// cette tuile a deux états et sert de bouton de bascule entre la vue
    /// console et la vue bibliothèques numériques.
    /// </summary>
    public class ShelfSlotManager
    {
        public const string SlotGameId = "DIGITALSHELF_SLOT";

        /// <summary>
        /// "!!" place la tuile en tête, devant les jaquettes cartouche ("!0")
        /// et CD ("!1") : '!' précède '0' dans l'ordre des caractères. Ce
        /// choix n'est pas qu'esthétique. Playnite sélectionne le premier
        /// élément de la liste après un changement de filtre, et le SDK
        /// n'offre aucun moyen de déplacer le focus visible du mode
        /// Fullscreen (SelectGame ne change que la sélection logique).
        /// Mettre la tuile en première position est donc le seul moyen
        /// fiable pour qu'elle soit sélectionnée au retour de la vue
        /// numérique, où elle est de toute façon déjà en tête.
        /// </summary>
        public const string PinnedSortingName = "!!";

        private const string ConsoleStateName = "Digital Game Library";

        private const string DigitalStateName = "Retour console";

        private const string CoverCacheFileName = "covers.txt";

        private readonly IPlayniteAPI api;

        private readonly ILogger logger;

        private readonly Guid pluginId;

        private readonly string userDataPath;

        private readonly Dictionary<ShelfView, string> coverSourcePaths;

        /// <summary>
        /// Chemins des deux jaquettes telles que stockées dans la base
        /// Playnite. Les recalculer à chaque bascule coûtait une copie de
        /// fichier sur disque (~340 Ko) sur le thread UI, ce qui rendait le
        /// changement de vue visiblement lent : on ne les enregistre donc
        /// qu'une fois, puis on se contente d'alterner entre les deux.
        /// </summary>
        private readonly Dictionary<ShelfView, string> coverDbPaths =
            new Dictionary<ShelfView, string>();


        public ShelfSlotManager(
            IPlayniteAPI api,
            ILogger logger,
            Guid pluginId,
            string userDataPath,
            string digitalCoverPath,
            string consoleReturnCoverPath)
        {
            this.api = api;
            this.logger = logger;
            this.pluginId = pluginId;
            this.userDataPath = userDataPath;

            coverSourcePaths = new Dictionary<ShelfView, string>
            {
                { ShelfView.Console, digitalCoverPath },
                { ShelfView.Digital, consoleReturnCoverPath }
            };

            LoadCoverCache();
        }


        public static string GetStateName(ShelfView view)
        {
            return view == ShelfView.Console ? ConsoleStateName : DigitalStateName;
        }


        /// <summary>
        /// Met la tuile dans l'état correspondant à la vue passée : en vue
        /// console elle invite à ouvrir le numérique, en vue numérique elle
        /// sert de retour.
        /// </summary>
        public void Render(ShelfView view)
        {
            Game game = FindSlot() ?? CreateSlot();

            game.Name = GetStateName(view);
            game.SortingName = PinnedSortingName;
            game.IsInstalled = true;

            string cover = GetCoverDbPath(game, view);

            if (!string.IsNullOrEmpty(cover))
            {
                game.CoverImage = cover;
            }

            api.Database.Games.Update(game);

            logger.Info($"DigitalShelf : tuile affichée dans l'état \"{game.Name}\".");
        }


        public Game FindSlot()
        {
            return api.Database.Games
                .FirstOrDefault(g => g.PluginId == pluginId && g.GameId == SlotGameId);
        }


        /// <summary>
        /// Crée la tuile si l'import de bibliothèque n'a pas encore eu lieu.
        /// L'identité (PluginId + GameId) est la même que celle renvoyée par
        /// GetGames, donc un import ultérieur met à jour cette entrée au
        /// lieu d'en créer une seconde.
        /// </summary>
        private Game CreateSlot()
        {
            Game game = new Game(ConsoleStateName)
            {
                GameId = SlotGameId,
                PluginId = pluginId,
                SortingName = PinnedSortingName,
                Added = DateTime.Now,
                IsInstalled = true,
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

            logger.Info("DigitalShelf : tuile créée dans la base.");

            return game;
        }


        /// <summary>
        /// Renvoie le chemin base de données de la jaquette correspondant à
        /// la vue, en l'enregistrant à la première demande seulement. Les
        /// deux images étant stockées sous des chemins distincts, le cache
        /// d'images de WPF ne les confond pas : contrairement au slot de
        /// CartridgeShelf, aucune astuce de nom de fichier unique n'est
        /// nécessaire ici.
        /// </summary>
        private string GetCoverDbPath(Game game, ShelfView view)
        {
            if (coverDbPaths.TryGetValue(view, out string cached) &&
                !string.IsNullOrEmpty(cached) &&
                CoverStillExists(cached))
            {
                return cached;
            }

            string source = coverSourcePaths[view];

            if (string.IsNullOrEmpty(source) || !File.Exists(source))
            {
                logger.Warn($"DigitalShelf : jaquette introuvable ({source}), la tuile restera sans image.");

                return null;
            }

            try
            {
                string dbPath = api.Database.AddFile(source, game.Id);

                coverDbPaths[view] = dbPath;

                SaveCoverCache();

                logger.Info($"DigitalShelf : jaquette de la vue {view} enregistrée en base.");

                return dbPath;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"DigitalShelf : erreur pendant l'enregistrement de la jaquette de la vue {view}.");

                return null;
            }
        }


        private bool CoverStillExists(string dbPath)
        {
            try
            {
                string full = api.Database.GetFullFilePath(dbPath);

                return !string.IsNullOrEmpty(full) && File.Exists(full);
            }
            catch
            {
                return false;
            }
        }


        /// <summary>
        /// Les deux jaquettes sont réenregistrées à chaque démarrage si on
        /// ne mémorise pas leur emplacement : on garde donc la trace des
        /// chemins d'une session à l'autre pour éviter d'accumuler des
        /// copies orphelines dans la base.
        /// </summary>
        private void LoadCoverCache()
        {
            string path = Path.Combine(userDataPath, CoverCacheFileName);

            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = line.Split(new[] { '=' }, 2);

                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    if (Enum.TryParse(parts[0].Trim(), out ShelfView view))
                    {
                        coverDbPaths[view] = parts[1].Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DigitalShelf : lecture du cache de jaquettes impossible, il sera reconstruit.");
            }
        }


        private void SaveCoverCache()
        {
            try
            {
                Directory.CreateDirectory(userDataPath);

                File.WriteAllLines(
                    Path.Combine(userDataPath, CoverCacheFileName),
                    coverDbPaths.Select(kv => $"{kv.Key}={kv.Value}").ToArray()
                );
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DigitalShelf : écriture du cache de jaquettes impossible.");
            }
        }
    }
}

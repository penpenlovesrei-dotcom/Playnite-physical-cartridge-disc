using System;
using System.Collections.Generic;
using System.Linq;

using DigitalShelf.Core;

using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace DigitalShelf.Services
{
    /// <summary>
    /// Crée et maintient les deux préréglages de filtre entre lesquels la
    /// tuile fait basculer. Les préréglages vivent dans la base Playnite
    /// (api.Database.FilterPresets), pas dans le thème : l'utilisateur n'a
    /// donc rien à configurer à la main.
    /// </summary>
    public class FilterPresetManager
    {
        public const string ConsolePresetName = "Console";

        public const string DigitalPresetName = "Digital Game Library";

        // Les deux plugins frères qui alimentent les jaquettes cartouche et
        // CD. Ce sont des GenericPlugin (donc absents de Addons.Plugins en
        // tant que LibraryPlugin), mais ils posent bien un PluginId sur
        // leurs faux jeux : c'est ce PluginId que le filtre "Library" cible.
        private static readonly Guid CartridgeShelfId =
            Guid.Parse("a1312fcb-7107-4168-95ba-181dd6069299");

        private static readonly Guid DiscShelfId =
            Guid.Parse("7c3e2d1a-9f61-4c7d-8b4e-123456789abc");

        private readonly IPlayniteAPI api;

        private readonly ILogger logger;

        private readonly Guid pluginId;


        public FilterPresetManager(IPlayniteAPI api, ILogger logger, Guid pluginId)
        {
            this.api = api;
            this.logger = logger;
            this.pluginId = pluginId;
        }


        /// <summary>
        /// Crée les préréglages manquants et remet à jour la liste des
        /// bibliothèques numériques (idempotent : appelé à chaque démarrage,
        /// pour prendre en compte un Epic/GOG installé entre-temps).
        /// </summary>
        public void EnsurePresets()
        {
            UpsertPreset(ConsolePresetName, GetConsoleLibraryIds());
            UpsertPreset(DigitalPresetName, GetDigitalLibraryIds());
        }


        public Guid GetPresetId(ShelfView view)
        {
            string name = view == ShelfView.Console ? ConsolePresetName : DigitalPresetName;

            FilterPreset preset = FindPreset(name);

            return preset?.Id ?? Guid.Empty;
        }


        /// <summary>
        /// Détermine la vue courante à partir du préréglage actif. Tout ce
        /// qui n'est pas explicitement le préréglage numérique est traité
        /// comme la vue console : c'est l'état par défaut, y compris quand
        /// aucun préréglage n'est appliqué (premier démarrage).
        /// </summary>
        public ShelfView GetCurrentView()
        {
            Guid activeId = api.MainView.GetActiveFilterPreset();

            Guid digitalId = GetPresetId(ShelfView.Digital);

            return activeId != Guid.Empty && activeId == digitalId
                ? ShelfView.Digital
                : ShelfView.Console;
        }


        /// <summary>
        /// La vue console ne montre que les trois shelves : cartouche, CD,
        /// et la tuile elle-même (sans quoi il n'y aurait plus rien pour
        /// basculer vers le numérique).
        /// </summary>
        private List<Guid> GetConsoleLibraryIds()
        {
            return new List<Guid> { CartridgeShelfId, DiscShelfId, pluginId };
        }


        /// <summary>
        /// La vue numérique montre toutes les vraies bibliothèques
        /// (Steam, Epic, GOG, EA...) plus la tuile, qui y sert de retour.
        /// Les bibliothèques sont découvertes dynamiquement pour qu'un
        /// nouveau launcher installé plus tard soit pris en compte sans
        /// retoucher au code.
        /// </summary>
        private List<Guid> GetDigitalLibraryIds()
        {
            List<Guid> ids = api.Addons.Plugins
                .OfType<LibraryPlugin>()
                .Select(p => p.Id)
                .Where(id => id != pluginId && id != CartridgeShelfId && id != DiscShelfId)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                logger.Warn(
                    "DigitalShelf : aucune bibliothèque numérique détectée (Steam, Epic, GOG...). La vue numérique n'affichera que la tuile de retour."
                );
            }
            else
            {
                logger.Info($"DigitalShelf : {ids.Count} bibliothèque(s) numérique(s) détectée(s).");
            }

            // La tuile doit rester visible dans la vue numérique, sinon
            // impossible de revenir à la vue console.
            ids.Add(pluginId);

            return ids;
        }


        private void UpsertPreset(string name, List<Guid> libraryIds)
        {
            FilterPreset preset = FindPreset(name);

            if (preset == null)
            {
                preset = new FilterPreset
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    // false : on ne veut pas voir réapparaître la bande de
                    // sélection rapide native du mode Fullscreen. La
                    // bascule passe exclusivement par la tuile.
                    ShowInFullscreeQuickSelection = false,
                    Settings = new FilterPresetSettings()
                };

                preset.Settings.Library = new IdItemFilterItemProperties(libraryIds);

                api.Database.FilterPresets.Add(preset);

                logger.Info($"DigitalShelf : préréglage \"{name}\" créé ({libraryIds.Count} bibliothèque(s)).");

                return;
            }

            if (preset.Settings == null)
            {
                preset.Settings = new FilterPresetSettings();
            }

            preset.Settings.Library = new IdItemFilterItemProperties(libraryIds);
            preset.ShowInFullscreeQuickSelection = false;

            api.Database.FilterPresets.Update(preset);

            logger.Info($"DigitalShelf : préréglage \"{name}\" mis à jour ({libraryIds.Count} bibliothèque(s)).");
        }


        private FilterPreset FindPreset(string name)
        {
            return api.Database.FilterPresets
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}

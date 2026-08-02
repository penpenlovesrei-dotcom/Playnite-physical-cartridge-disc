using System;
using System.Linq;

using DiscShelf.Core;

using Playnite.SDK;
using Playnite.SDK.Models;

namespace DiscShelf.Services
{
    /// <summary>
    /// Récupère (ou crée si besoin) l'entrée Platform de Playnite
    /// correspondant à un PlatformId de DiscShelf.
    /// </summary>
    public class PlatformResolver
    {
        private readonly IPlayniteAPI api;

        private readonly ILogger logger;


        public PlatformResolver(IPlayniteAPI api)
        {
            this.api = api;

            logger = LogManager.GetLogger();
        }


        public Guid GetOrCreate(PlatformId platformId)
        {
            string name = ToDisplayName(platformId);

            Platform platform = api.Database.Platforms
                .FirstOrDefault(p => string.Equals(
                    p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (platform != null)
            {
                return platform.Id;
            }

            logger.Info($"PlatformResolver : création de la plateforme {name}.");

            platform = new Platform(name);

            api.Database.Platforms.Add(platform);

            return platform.Id;
        }


        private static string ToDisplayName(PlatformId platformId)
        {
            switch (platformId)
            {
                case PlatformId.PlayStation: return "Sony PlayStation";
                case PlatformId.PlayStation2: return "Sony PlayStation 2";
                case PlatformId.PlayStation3: return "Sony PlayStation 3";
                case PlatformId.SegaSaturn: return "Sega Saturn";
                case PlatformId.MegaCD: return "Sega CD";
                case PlatformId.Dreamcast: return "Sega Dreamcast";
                case PlatformId.Xbox: return "Microsoft Xbox";
                case PlatformId.Xbox360: return "Microsoft Xbox 360";
                case PlatformId.GameCube: return "Nintendo GameCube";
                case PlatformId.Wii: return "Nintendo Wii";
                case PlatformId.NeoGeoCD: return "SNK Neo Geo CD";
                default: return platformId.ToString();
            }
        }
    }
}

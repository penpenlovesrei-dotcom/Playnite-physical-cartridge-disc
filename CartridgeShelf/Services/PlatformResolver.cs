using System;
using System.Linq;

using CartridgeShelf.Core;

using Playnite.SDK;
using Playnite.SDK.Models;

namespace CartridgeShelf.Services
{
    /// <summary>
    /// Récupère (ou crée si besoin) l'entrée Platform de Playnite
    /// correspondant à un PlatformId de CartridgeShelf.
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
                case PlatformId.SuperNintendo: return "Super Nintendo Entertainment System";
                default: return platformId.ToString();
            }
        }
    }
}

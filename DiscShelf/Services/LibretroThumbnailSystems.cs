using DiscShelf.Core;

namespace DiscShelf.Services
{
    /// <summary>
    /// Correspondance entre nos PlatformId et le nom du dépôt GitHub
    /// libretro-thumbnails (organisation "libretro-thumbnails", un dépôt
    /// par système). Aucune authentification requise -- accès direct via
    /// raw.githubusercontent.com.
    /// Voir https://github.com/libretro-thumbnails/libretro-thumbnails
    /// Retourne null si la plateforme n'a pas de dépôt connu.
    /// </summary>
    public static class LibretroThumbnailSystems
    {
        public static string ToRepoName(PlatformId platform)
        {
            switch (platform)
            {
                case PlatformId.PlayStation: return "Sony_-_PlayStation";
                case PlatformId.PlayStation2: return "Sony_-_PlayStation_2";
                case PlatformId.SegaSaturn: return "Sega_-_Saturn";
                case PlatformId.MegaCD: return "Sega_-_Mega-CD_-_Sega_CD";
                case PlatformId.Dreamcast: return "Sega_-_Dreamcast";
                case PlatformId.NeoGeoCD: return "SNK_-_Neo_Geo_CD";
                case PlatformId.GameCube: return "Nintendo_-_GameCube";
                case PlatformId.Wii: return "Nintendo_-_Wii";
                case PlatformId.Xbox: return "Microsoft_-_Xbox";
                case PlatformId.Xbox360: return "Microsoft_-_Xbox_360";

                default: return null;
            }
        }
    }
}

using CartridgeShelf.Core;

namespace CartridgeShelf.Services
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
                case PlatformId.SuperNintendo: return "Nintendo_-_Super_Nintendo_Entertainment_System";

                default: return null;
            }
        }
    }
}

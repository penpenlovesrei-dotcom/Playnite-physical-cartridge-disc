using DiscShelf.Core;

namespace DiscShelf.Services
{
    /// <summary>
    /// Correspondance entre nos PlatformId et les systemeid utilisés par
    /// l'API ScreenScraper.fr (voir systemesListe.php pour la liste complète).
    /// Retourne -1 si la plateforme n'est pas encore mappée.
    /// </summary>
    public static class ScreenScraperSystemIds
    {
        public static int ToSystemeId(PlatformId platform)
        {
            switch (platform)
            {
                // Confirmés (recherche web sur romsinfos.php?plateforme=X) :
                case PlatformId.PlayStation: return 57;
                case PlatformId.SegaSaturn: return 22;
                case PlatformId.Dreamcast: return 23;
                case PlatformId.GameCube: return 13;
                case PlatformId.Wii: return 16;
                case PlatformId.Xbox360: return 33;
                case PlatformId.NeoGeoCD: return 70;

                // Non vérifiés (valeurs "attendues" d'après la convention
                // ScreenScraper, à confirmer via systemesListe.php avant
                // utilisation en prod) :
                case PlatformId.PlayStation2: return 58;
                case PlatformId.PlayStation3: return 59;
                case PlatformId.MegaCD: return 20;
                case PlatformId.Xbox: return 32;

                default: return -1;
            }
        }
    }
}

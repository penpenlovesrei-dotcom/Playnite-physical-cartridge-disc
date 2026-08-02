using CartridgeShelf.Core;

namespace CartridgeShelf.Services
{
    /// <summary>
    /// Correspondance entre nos PlatformId et les systemeid ScreenScraper.
    /// Confirmé via romsinfos.php?plateforme=4 (page "Super Nintendo").
    /// </summary>
    public static class ScreenScraperSystemIds
    {
        public static int ToSystemeId(PlatformId platform)
        {
            switch (platform)
            {
                case PlatformId.SuperNintendo: return 4;
                default: return -1;
            }
        }
    }
}

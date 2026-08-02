namespace CartridgeShelf.Core
{
    public static class CartridgeState
    {
        public static bool CartridgeInserted { get; set; }

        public static string CurrentTitle { get; set; }

        public static string CurrentChecksum { get; set; }

        public static PlatformId CurrentPlatform { get; set; }


        public static void Clear()
        {
            CartridgeInserted = false;
            CurrentTitle = null;
            CurrentChecksum = null;
            CurrentPlatform = PlatformId.Unknown;
        }
    }
}

namespace DiscShelf.Core
{
    public static class DiscState
    {
        public static bool DiscInserted { get; set; }

        public static string CurrentTitle { get; set; }

        public static string CurrentSerial { get; set; }

        public static PlatformId CurrentPlatform { get; set; }


        public static void Clear()
        {
            DiscInserted = false;
            CurrentTitle = null;
            CurrentSerial = null;
            CurrentPlatform = PlatformId.Unknown;
        }
    }
}

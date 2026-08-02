namespace DiscShelf.Core
{
    public class GameIdentity
    {
        public PlatformId Platform { get; set; }

        public string Serial { get; set; }


        public GameIdentity()
        {
            Platform = PlatformId.Unknown;
            Serial = string.Empty;
        }


        public override string ToString()
        {
            return $"{Platform} | {Serial}";
        }
    }
}

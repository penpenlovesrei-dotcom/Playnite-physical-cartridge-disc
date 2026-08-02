namespace CartridgeShelf.Core
{
    public class GameIdentity
    {
        public PlatformId Platform { get; set; }

        /// <summary>
        /// Identifiant de la cartouche : le checksum 16 bits (format hex,
        /// ex. "9ea9") extrait de la réponse USB du SN Operator. Joue le
        /// même rôle que le "Serial" pour DiscShelf.
        /// </summary>
        public string Checksum { get; set; }


        public GameIdentity()
        {
            Platform = PlatformId.Unknown;
            Checksum = string.Empty;
        }


        public override string ToString()
        {
            return $"{Platform} | {Checksum}";
        }
    }
}

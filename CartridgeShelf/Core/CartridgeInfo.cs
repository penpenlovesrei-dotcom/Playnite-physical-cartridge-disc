namespace CartridgeShelf.Core
{
    /// <summary>
    /// Enregistrement brut renvoyé par un lecteur de cartouches (ex. SN
    /// Operator) pour la cartouche actuellement insérée. Équivalent
    /// cartouche de DiscInfo/DiscFile pour DiscShelf, mais beaucoup plus
    /// simple puisqu'il n'y a pas de système de fichiers à parcourir : le
    /// device renvoie directement un petit enregistrement binaire décodé
    /// par le détecteur (voir Detectors/SnOperatorDetector.cs).
    /// </summary>
    public class CartridgeInfo
    {
        public bool Present { get; set; }

        /// <summary>Checksum 16 bits (octets 13-14 de la réponse USB), unique par jeu.</summary>
        public ushort Checksum { get; set; }

        /// <summary>Code de taille ROM brut (octet 5) -- hypothèse non confirmée à 100%.</summary>
        public byte SizeCode { get; set; }

        /// <summary>Vrai si la cartouche a une sauvegarde SRAM (octets 6-7 == 0x0D 0x21) -- hypothèse.</summary>
        public bool HasSram { get; set; }
    }
}

using CartridgeShelf.Core;

namespace CartridgeShelf.Detectors
{
    /// <summary>
    /// Un détecteur sait interroger un lecteur de cartouches (device USB)
    /// et en extraire l'identifiant (checksum) du jeu inséré.
    /// Ajouter un nouveau lecteur (GB Operator, etc.) = ajouter une nouvelle
    /// implémentation de cette interface et l'enregistrer dans
    /// CartridgeShelfPlugin.
    /// </summary>
    public interface ICartridgeDetector
    {
        PlatformId Platform { get; }

        /// <summary>Vrai si ce détecteur sait dialoguer avec le device présenté.</summary>
        bool CanHandle();

        /// <summary>Interroge le device et retourne l'état actuel (présence + identité).</summary>
        CartridgeInfo Read();

        GameIdentity ToIdentity(CartridgeInfo info);
    }
}

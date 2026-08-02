using DiscShelf.Core;

namespace DiscShelf.Detectors
{
    /// <summary>
    /// Un détecteur sait reconnaître un disque d'une plateforme donnée
    /// et en extraire l'identifiant (serial) du jeu.
    /// Ajouter une nouvelle console = ajouter une nouvelle implémentation
    /// de cette interface et l'enregistrer dans DiscShelfPlugin.
    /// </summary>
    public interface IDiscDetector
    {
        PlatformId Platform { get; }

        bool CanHandle(DiscInfo disc);

        GameIdentity Detect(DiscInfo disc);
    }
}

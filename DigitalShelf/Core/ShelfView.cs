namespace DigitalShelf.Core
{
    /// <summary>
    /// Les deux vues entre lesquelles la tuile fait basculer la
    /// bibliothèque. Chaque vue correspond à un préréglage de filtre créé
    /// par FilterPresetManager.
    /// </summary>
    public enum ShelfView
    {
        /// <summary>
        /// Vue par défaut : uniquement les jaquettes des shelves
        /// (cartouche, CD) plus la tuile elle-même.
        /// </summary>
        Console,

        /// <summary>
        /// Vue bibliothèques numériques : Steam, Epic, GOG, EA... plus la
        /// tuile elle-même, qui sert alors de retour.
        /// </summary>
        Digital
    }
}

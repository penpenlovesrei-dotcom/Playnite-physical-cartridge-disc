namespace CartridgeShelf.Core
{
    // Point de départ : uniquement SNES (SN Operator). À étendre au fur et
    // à mesure de l'ajout d'autres lecteurs de cartouches Epilogue
    // (GB Operator, etc.) ou d'autres marques.
    public enum PlatformId
    {
        Unknown = 0,

        SuperNintendo,

        // TODO : GameBoy, GameBoyColor, GameBoyAdvance, Nintendo64, ...
    }
}

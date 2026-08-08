namespace Bato
{
    public enum PickupItemType : byte
    {
        None = 0,
        RemoteBoat = 1,
        Shield = 2,
        FireBall = 3,
        ChainBall = 4,
    }

    public static class PickupItemNames
    {
        public static string Get(PickupItemType type) => type switch
        {
            PickupItemType.RemoteBoat => "BARQUE RC",
            PickupItemType.Shield => "BOUCLIER",
            PickupItemType.FireBall => "BOULET FEU",
            PickupItemType.ChainBall => "BOULET CHAÎNE",
            _ => string.Empty,
        };
    }
}

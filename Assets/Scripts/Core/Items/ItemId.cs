namespace Worker.Core
{
    /// <summary>
    /// Item identifiers are persisted in save files, so existing values must never
    /// be renumbered. Append new items at the end.
    /// </summary>
    public enum ItemId : ushort
    {
        None = 0,

        Log = 1,
        Plank = 2,
        ChairLeg = 3,
        Seat = 4,
        WoodChair = 5,

        Fabric = 6,
        MetalRod = 7,
        OfficeChair = 8,
        Bookshelf = 9
    }
}

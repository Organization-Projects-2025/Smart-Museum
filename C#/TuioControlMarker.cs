/// <summary>
/// reacTIVision / TUIO class IDs for tangible UI vs museum figures.
/// </summary>
public static class TuioControlMarker
{
    /// <summary>Reserved for future circular menu + login/register; ignored everywhere for now.</summary>
    public const int ReservedEmptySymbolId = 0;

    /// <summary>Circular menu (logged in), login/register auth rings, admin analytics — not a museum figure.</summary>
    public const int MenuAuthSymbolId = 3;

    /// <summary>Legacy alias — same as <see cref="MenuAuthSymbolId"/>.</summary>
    public const int SymbolId = MenuAuthSymbolId;

    public static bool IsMenuAuthMarker(int symbolId) => symbolId == MenuAuthSymbolId;

    public static bool IsReservedEmptySlot(int symbolId) => symbolId == ReservedEmptySymbolId;
}

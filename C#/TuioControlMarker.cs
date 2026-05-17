/// <summary>
/// reacTIVision / TUIO class IDs for tangible UI vs museum figures.
/// </summary>
public static class TuioControlMarker
{
    /// <summary>Unused reacTIVision class id — ignored on the table.</summary>
    public const int ReservedEmptySymbolId = 6;

    /// <summary>Circular menu (logged in), login/register auth rings, admin analytics — not a museum figure.</summary>
    public const int MenuAuthSymbolId = 3;

    /// <summary>Legacy alias — same as <see cref="MenuAuthSymbolId"/>.</summary>
    public const int SymbolId = MenuAuthSymbolId;

    public static bool IsMenuAuthMarker(int symbolId) => symbolId == MenuAuthSymbolId;

    public static bool IsReservedEmptySlot(int symbolId) => symbolId == ReservedEmptySymbolId;
}

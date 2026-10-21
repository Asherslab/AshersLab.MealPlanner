using System.Globalization;

namespace AshersLab.MealPlanner.Shared.Domain;

/// <summary>
/// Order for a list the household arranges by hand (categories), as a position per item: the list is sorted by
/// position. Moving one item writes only that item's position - a number between its new neighbours' - so two
/// devices moving different items both keep their moves, where swapping two items' positions would not.
/// </summary>
/// <remarks>
/// Two items can end on the same position (two devices each add one at the end offline); the list then sorts
/// them by name, and the next move that needs a gap between equal positions renumbers the whole list. Decimal
/// halves about 90 times in one gap before it runs out of digits; that also renumbers.
/// </remarks>
public static class ListPosition
{
    /// <summary>The position for an item added at the end of a list whose positions are <paramref name="positions"/>.</summary>
    public static decimal AtEnd(IEnumerable<decimal> positions) =>
        positions.Select(p => (decimal?)p).Max() is { } last ? decimal.Floor(last) + 1 : 1;

    /// <summary>
    /// The positions to write to move the item at <paramref name="index"/> one place up (<paramref name="direction"/>
    /// -1) or down (+1) in a list whose positions, in display order, are <paramref name="positions"/>. Usually one
    /// change, the moved item's; every item's when there is no gap to move into. Empty when it cannot move (already
    /// at that end).
    /// </summary>
    public static IReadOnlyList<(int Index, decimal Position)> Move(IReadOnlyList<decimal> positions, int index, int direction)
    {
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction), "Up is -1, down is +1.");
        if (index < 0 || index >= positions.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        int passed = index + direction;
        if (passed < 0 || passed >= positions.Count)
            return [];

        // The gap to land in: between the neighbour being passed and the one beyond it.
        int     beyond = passed + direction;
        decimal near   = positions[passed];
        decimal? far   = beyond >= 0 && beyond < positions.Count ? positions[beyond] : null;

        decimal landing = far is { } f ? Midpoint(near, f) : near + direction;
        decimal low  = direction < 0 ? far ?? decimal.MinValue : near;
        decimal high = direction < 0 ? near : far ?? decimal.MaxValue;

        if (low < landing && landing < high)
            return [(index, landing)];

        // No room (equal neighbours, or out of digits): number the list 1..n in its new order.
        List<int> order = [.. Enumerable.Range(0, positions.Count)];
        order.RemoveAt(index);
        order.Insert(passed, index);

        return [.. order.Select((item, at) => (item, (decimal)(at + 1))).Where(c => positions[c.item] != c.Item2)];
    }

    /// <summary>Positions on the wire and in the database: invariant culture, so every device reads the same number.</summary>
    public static string Format(decimal position) => position.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads <see cref="Format"/>'s output, and only that: anything else is refused.</summary>
    public static bool TryParse(string? value, out decimal position) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out position)
        && Format(position) == value;

    private static decimal Midpoint(decimal a, decimal b) => a + (b - a) / 2;
}

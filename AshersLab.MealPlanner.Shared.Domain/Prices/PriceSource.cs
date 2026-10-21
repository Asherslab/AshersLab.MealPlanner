namespace AshersLab.MealPlanner.Shared.Domain.Prices;

/// <summary>
/// Where a price was seen. On the wire and in both databases by <b>name</b> - never rename a member. All three count
/// the same when costing: the latest observation wins, whatever its source.
/// </summary>
public enum PriceSource
{
    /// <summary>Typed in by the planner.</summary>
    Manual,

    /// <summary>A matched line of an imported receipt (slices 12 and 13).</summary>
    Receipt,

    /// <summary>Woolworths' own price, fetched by the server (version 2).</summary>
    Live,
}

public static class PriceSources
{
    public static string Format(PriceSource source) => source.ToString();

    /// <summary>Reads exactly a member's name, case and all - nothing else, numbers included.</summary>
    public static bool TryParse(string? value, out PriceSource source)
    {
        foreach (PriceSource candidate in Enum.GetValues<PriceSource>())
        {
            if (candidate.ToString() == value)
            {
                source = candidate;
                return true;
            }
        }

        source = default;
        return false;
    }

    /// <summary>What the planner sees in a price's history: "typed in", "receipt", "online".</summary>
    public static string Label(this PriceSource source) => source switch
    {
        PriceSource.Manual  => "typed in",
        PriceSource.Receipt => "receipt",
        PriceSource.Live    => "online",
        _                   => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}

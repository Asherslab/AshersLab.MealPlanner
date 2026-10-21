namespace AshersLab.MealPlanner.Shared.Domain.Ingredients;

/// <summary>
/// What the household has of a staple (oil, soy sauce, spices). Staples are used in amounts nobody weighs, so they are
/// never drawn down gram by gram: the planner says which of these it is, and the later edit wins. On the wire and in
/// both databases by <b>name</b> - never rename a member. Meaningless for an ingredient that is not a staple.
/// </summary>
public enum StapleStock
{
    Have,

    /// <summary>Goes on the shopping list.</summary>
    RunningLow,

    /// <summary>Goes on the shopping list.</summary>
    Out,
}

public static class StapleStocks
{
    public static string Format(StapleStock stock) => stock.ToString();

    /// <summary>Reads exactly a member's name, case and all - nothing else, numbers included.</summary>
    public static bool TryParse(string? value, out StapleStock stock)
    {
        foreach (StapleStock candidate in Enum.GetValues<StapleStock>())
        {
            if (candidate.ToString() == value)
            {
                stock = candidate;
                return true;
            }
        }

        stock = default;
        return false;
    }

    /// <summary>What the planner reads: "Have", "Running low", "Out".</summary>
    public static string Label(this StapleStock stock) => stock switch
    {
        StapleStock.RunningLow => "Running low",
        _                      => stock.ToString(),
    };

    /// <summary>Whether the shopping list should buy it.</summary>
    public static bool NeedsBuying(this StapleStock stock) => stock != StapleStock.Have;
}

namespace AshersLab.MealPlanner.Shared.Domain.Meals;

/// <summary>
/// What kind of thing a meal is, which decides how it is costed. On the wire and in both
/// databases by <b>name</b> - never rename a member.
/// </summary>
public enum MealKind
{
    /// <summary>Has ingredients and makes a batch of <c>ServesPerBatch</c> serves: "4x spaghetti bolognese".</summary>
    Cooked,

    /// <summary>One serve, costed by its ingredients, no batch: "bagels", "scrambled eggs + toast".</summary>
    Simple,

    /// <summary>Eats a serve of a batch cooked earlier. Costs nothing itself; the batch it eats from was paid for.</summary>
    Leftovers,

    /// <summary>Costs the household nothing: "family dinner" at someone else's.</summary>
    NoCost,

    /// <summary>A typed amount for the whole thing, not ingredients: takeaway.</summary>
    FixedCost,
}

public static class MealKinds
{
    public static string Format(MealKind kind) => kind.ToString();

    /// <summary>Reads exactly a member's name, case and all - nothing else, numbers included.</summary>
    public static bool TryParse(string? value, out MealKind kind)
    {
        foreach (MealKind candidate in Enum.GetValues<MealKind>())
        {
            if (candidate.ToString() == value)
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }

    /// <summary>Whether its cost comes from its ingredients - and so whether the meal page lists them.</summary>
    public static bool HasIngredients(this MealKind kind) => kind is MealKind.Cooked or MealKind.Simple;

    /// <summary>Whether it is made for several serves at once: a batch, or a takeaway that feeds several.</summary>
    public static bool HasServes(this MealKind kind) => kind is MealKind.Cooked or MealKind.FixedCost;

    /// <summary>What the planner picks from: "Cooked in a batch", "Single serve"...</summary>
    public static string Label(this MealKind kind) => kind switch
    {
        MealKind.Cooked    => "Cooked in a batch",
        MealKind.Simple    => "Single serve",
        MealKind.Leftovers => "Leftovers",
        MealKind.NoCost    => "No cost",
        MealKind.FixedCost => "Fixed cost",
        _                  => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The line under the choice, saying what it means.</summary>
    public static string Explanation(this MealKind kind) => kind switch
    {
        MealKind.Cooked    => "Made from ingredients, several serves at once.",
        MealKind.Simple    => "Made from ingredients, one serve at a time.",
        MealKind.Leftovers => "Eats a serve of something cooked earlier.",
        MealKind.NoCost    => "Costs nothing - eating at someone else's.",
        MealKind.FixedCost => "A set price, not ingredients - takeaway.",
        _                  => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

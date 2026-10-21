namespace AshersLab.MealPlanner.Shared.Domain.Ingredients;

/// <summary>
/// What an ingredient's quantities are counted in. One per ingredient, and no conversion between them: a recipe's
/// "2 cucumbers" and a pack's "3 cucumbers" are both <see cref="Each"/>.
/// </summary>
/// <remarks>
/// On the wire and in both databases by <b>name</b> ("Gram") - never rename a member. Changing an ingredient's unit
/// does not convert anything that is already counted in the old one.
/// </remarks>
public enum BaseUnit
{
    Gram,
    Millilitre,
    Each,
}

public static class BaseUnits
{
    /// <summary>What the planner sees beside a quantity: "g", "ml", "each".</summary>
    public static string Symbol(this BaseUnit unit) => unit switch
    {
        BaseUnit.Gram       => "g",
        BaseUnit.Millilitre => "ml",
        BaseUnit.Each       => "each",
        _                   => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>The unit's name as an op carries it.</summary>
    public static string Format(BaseUnit unit) => unit.ToString();

    /// <summary>Reads exactly a member's name, case and all - nothing else, numbers included.</summary>
    public static bool TryParse(string? value, out BaseUnit unit)
    {
        foreach (BaseUnit candidate in Enum.GetValues<BaseUnit>())
        {
            if (candidate.ToString() == value)
            {
                unit = candidate;
                return true;
            }
        }

        unit = default;
        return false;
    }
}

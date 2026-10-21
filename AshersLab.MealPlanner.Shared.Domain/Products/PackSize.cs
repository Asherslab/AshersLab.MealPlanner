using AshersLab.MealPlanner.Shared.Domain.Ingredients;

namespace AshersLab.MealPlanner.Shared.Domain.Products;

/// <summary>
/// A product's pack quantity as the planner types and reads it: "2kg", "8x250g", "1.25 L", "18pk". Always held in the
/// ingredient's base unit - grams, millilitres or each - so "2kg" is 2000. Read like any quantity
/// (<see cref="Quantity"/>); this adds what a pack must be.
/// </summary>
public static class PackSize
{
    /// <summary>The largest pack: a tonne, a thousand litres, a million things.</summary>
    public const decimal Max = Quantity.Max;

    /// <summary>Thousandths of a gram, millilitre or each - more is a typo, not precision.</summary>
    public const int MaxDecimals = Quantity.MaxDecimals;

    /// <summary>
    /// Reads what the planner typed into a quantity in <paramref name="unit"/>; null with a reason in
    /// <paramref name="problem"/> when it cannot.
    /// </summary>
    public static decimal? Parse(string? text, BaseUnit unit, out string? problem)
    {
        decimal? total = Quantity.Read(text, unit, out problem);
        if (total is null)
        {
            problem ??= "Enter how much is in a pack.";
            return null;
        }

        problem = Problem(total.Value);
        return problem is null ? total : null;
    }

    /// <summary>Null when <paramref name="quantity"/> (in the base unit) is a pack size; otherwise why not.</summary>
    public static string? Problem(decimal quantity) =>
        Quantity.Problem(quantity, "A pack has to hold something.", "That is more than any pack.");

    /// <summary>What the planner reads: "2 kg", "500 g", "1.25 L", "330 ml", "12 each".</summary>
    public static string Format(decimal quantity, BaseUnit unit) => Quantity.Format(quantity, unit);

    /// <summary>What goes back into the box for editing: "2kg" reads back as 2000 g.</summary>
    public static string Editable(decimal quantity, BaseUnit unit) => Quantity.Editable(quantity, unit);
}

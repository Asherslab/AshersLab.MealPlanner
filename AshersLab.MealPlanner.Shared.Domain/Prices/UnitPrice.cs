using AshersLab.MealPlanner.Shared.Domain.Ingredients;

namespace AshersLab.MealPlanner.Shared.Domain.Prices;

/// <summary>
/// A price per base unit, kept as the exact ratio it was read from - <see cref="Cents"/> for <see cref="PerQuantity"/>
/// grams, millilitres or each ($24.99 for 2000 g) - and never divided out. Dividing would round (1.2495 c/g is not a
/// whole number of cents, and a decimal division can repeat); a cost is rounded once, at the end, in <see cref="CostOf"/>.
/// </summary>
public readonly record struct UnitPrice : IComparable<UnitPrice>
{
    public UnitPrice(long cents, decimal perQuantity)
    {
        if (cents < 0)
            throw new ArgumentOutOfRangeException(nameof(cents), cents, "A price is never negative.");
        if (perQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(perQuantity), perQuantity, "A price is for some quantity.");

        Cents       = cents;
        PerQuantity = perQuantity;
    }

    public long Cents { get; }

    /// <summary>How many base units <see cref="Cents"/> buys.</summary>
    public decimal PerQuantity { get; }

    /// <summary>
    /// What <paramref name="quantity"/> base units cost, in whole cents: the exact product rounded once, half away
    /// from zero (937.5 c is 938 c), the way a till rounds.
    /// </summary>
    public long CostOf(decimal quantity) =>
        (long)decimal.Round(Cents * quantity / PerQuantity, 0, MidpointRounding.AwayFromZero);

    /// <summary>What the planner reads a price per: a kilogram, a litre, or one.</summary>
    public static decimal DisplayQuantity(BaseUnit unit) => unit == BaseUnit.Each ? 1 : 1000;

    /// <summary>"$12.50/kg", "$3.20/L", "$0.45 each".</summary>
    public string Format(BaseUnit unit)
    {
        string money = Money.Format(CostOf(DisplayQuantity(unit)));
        return unit switch
        {
            BaseUnit.Gram       => $"{money}/kg",
            BaseUnit.Millilitre => $"{money}/L",
            _                   => $"{money} each",
        };
    }

    /// <summary>Cheaper first, compared exactly by cross-multiplying - no rounding decides which is cheaper.</summary>
    public int CompareTo(UnitPrice other) => (Cents * other.PerQuantity).CompareTo(other.Cents * PerQuantity);
}

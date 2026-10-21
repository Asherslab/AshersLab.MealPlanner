using AshersLab.MealPlanner.Shared.Domain.Prices;

namespace AshersLab.MealPlanner.Shared.Domain.Meals;

/// <summary>
/// A cost that may be incomplete: <see cref="Cents"/> is what is known, and <see cref="Missing"/> how many inputs could
/// not be costed. <b>An incomplete cost is "from $X + N unpriced", never shown as X alone</b> - a silently low number is
/// worse than none.
/// </summary>
public readonly record struct Cost(long Cents, int Missing)
{
    public bool Complete => Missing == 0;

    /// <summary>"$12.80", or "from $12.80 + 2 unpriced"; with a <paramref name="per"/> of " a serve", "from $3.20 a serve + 2 unpriced".</summary>
    public string Format(string per = "") =>
        Complete ? $"{Money.Format(Cents)}{per}" : $"from {Money.Format(Cents)}{per} + {Missing} unpriced";
}

/// <summary>Why one of a meal's ingredients has or has no cost.</summary>
public enum LineState
{
    Costed,

    /// <summary>"Not entered yet": the spreadsheet import makes these.</summary>
    NoQuantity,

    /// <summary>The ingredient has no price (<see cref="IngredientPricing"/>), or is gone.</summary>
    Unpriced,
}

/// <summary>One of a meal's ingredients as costing reads it: its quantity, and its ingredient's price - null when unpriced or gone.</summary>
public sealed record CostLine(decimal? Quantity, UnitPrice? Price);

/// <summary>One line's cost: whole cents when <see cref="LineState.Costed"/>, otherwise null.</summary>
public sealed record LineCost(LineState State, long? Cents);

/// <summary>
/// A meal's cost. <see cref="Batch"/> is what making it once costs; <see cref="PerServe"/> what one serve costs. Both
/// null when there is nothing to cost - leftovers (the batch it eats from was paid for, and costs there), or a meal made
/// from ingredients that has none yet (shown as "no ingredients yet", never as $0).
/// </summary>
public sealed record MealCost(Cost? Batch, Cost? PerServe, IReadOnlyList<LineCost> Lines);

/// <summary>
/// Steps 2 and 3 of "How a cost is computed" (docs/modules/meals/meals.md): a meal's batch and per-serve cost from its
/// ingredients' quantities and prices. Run identically in the browser and on the server.
/// </summary>
public static class MealCosting
{
    /// <summary>
    /// What one line costs: quantity × the ingredient's price per base unit, rounded once to whole cents
    /// (<see cref="UnitPrice.CostOf"/>). No quantity, or no price: not costed, and says which.
    /// </summary>
    public static LineCost Line(CostLine line) => line switch
    {
        { Quantity: null }                    => new LineCost(LineState.NoQuantity, null),
        { Price: null }                       => new LineCost(LineState.Unpriced, null),
        { Quantity: { } q, Price: { } price } => new LineCost(LineState.Costed, price.CostOf(q)),
    };

    /// <summary>
    /// The meal's cost by its kind:
    /// <list type="bullet">
    /// <item><see cref="MealKind.Cooked"/>: batch = the sum of its lines' cents - each line rounded once, so the lines on
    /// the meal's page add up to the batch exactly; any line with no quantity or no price makes it incomplete. Per serve
    /// = batch ÷ <paramref name="servesPerBatch"/>, rounded once, half away from zero.</item>
    /// <item><see cref="MealKind.Simple"/>: one serve, so per serve = batch.</item>
    /// <item><see cref="MealKind.FixedCost"/>: batch = the typed amount (incomplete when there is none), per serve =
    /// amount ÷ the serves it feeds.</item>
    /// <item><see cref="MealKind.NoCost"/>: $0, complete - it really costs nothing.</item>
    /// <item><see cref="MealKind.Leftovers"/>: nothing of its own.</item>
    /// </list>
    /// Ingredient lines are ignored by the kinds that are not made from them (they are kept, not deleted, when the kind
    /// changes).
    /// </summary>
    public static MealCost Cost(MealKind kind, int servesPerBatch, long? fixedCostCents, IReadOnlyList<CostLine> lines)
    {
        if (servesPerBatch < 1)
            throw new ArgumentOutOfRangeException(nameof(servesPerBatch), servesPerBatch, "A batch makes at least one serve.");

        switch (kind)
        {
            case MealKind.Cooked or MealKind.Simple:
            {
                List<LineCost> costed = [.. lines.Select(Line)];
                if (costed.Count == 0)
                    return new MealCost(null, null, costed);

                Cost batch = new(costed.Sum(l => l.Cents ?? 0), costed.Count(l => l.State != LineState.Costed));
                return new MealCost(batch, kind == MealKind.Simple ? batch : PerServe(batch, servesPerBatch), costed);
            }
            case MealKind.FixedCost:
            {
                Cost batch = fixedCostCents is { } cents ? new Cost(cents, 0) : new Cost(0, 1);
                return new MealCost(batch, PerServe(batch, servesPerBatch), []);
            }
            case MealKind.NoCost:
                return new MealCost(new Cost(0, 0), new Cost(0, 0), []);
            case MealKind.Leftovers:
                return new MealCost(null, null, []);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    /// <summary>A batch's cost shared over its serves, rounded once, half away from zero; as incomplete as the batch.</summary>
    public static Cost PerServe(Cost batch, int serves) =>
        new((long)decimal.Round((decimal)batch.Cents / serves, 0, MidpointRounding.AwayFromZero), batch.Missing);
}

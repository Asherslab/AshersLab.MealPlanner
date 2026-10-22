using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Pantry;

namespace AshersLab.MealPlanner.Shared.Domain.Shopping;

/// <summary>A pack a store sells of an ingredient: its size in the ingredient's base unit, and its latest price - null when unpriced.</summary>
public sealed record PackOption(Guid ProductId, Guid StoreId, decimal PackQuantity, long? PriceCents);

/// <summary>Whole packs of one product: "1 pack (1.25 kg spare)", and what they cost - null when the product is unpriced.</summary>
public sealed record PackChoice(Guid ProductId, Guid StoreId, decimal PackQuantity, int Packs, decimal Spare, long? Cents)
{
    /// <summary>What goes into the pantry when the packs are ticked bought.</summary>
    public decimal Quantity => PackQuantity * Packs;
}

public enum LineNeed
{
    /// <summary>The pantry does not hold enough: buy the shortfall, in whole packs.</summary>
    Buy,

    /// <summary>The pantry holds it all: shown so it can be checked, not bought.</summary>
    InPantry,

    /// <summary>A staple marked running low or out: one pack.</summary>
    Staple,
}

/// <summary>
/// One ingredient's line on a list. <paramref name="Need"/> is what the range's meals use; <paramref name="Have"/> the
/// pantry's stock at the list's start; <paramref name="Shortfall"/> the difference, never below zero.
/// <paramref name="Pack"/> is null when there is nothing to buy, or no product gives a pack size - then the shortfall is
/// bought as an amount ("750 g") and its cost is unknown.
/// </summary>
public sealed record LinePlan(LineNeed Need, decimal Needed, decimal Have, decimal Shortfall, PackChoice? Pack);

/// <summary>
/// The shopping list's arithmetic (docs/modules/shopping/shopping-list.md): what to buy after the pantry, in whole packs,
/// and what the shop costs. Pure; the same in the browser and on the server.
/// </summary>
public static class ShoppingPlan
{
    /// <summary>No line buys more packs than this: past it, a pack size is a typo (8 g for 8 kg), not a shop.</summary>
    public const int MaxPacks = 999;

    /// <summary>
    /// A line from what the meals need and what the pantry has at the start: stock counts only when it is
    /// <see cref="StockState.InStock"/> - out, probably out or never counted is nothing. Covered entirely → in the pantry.
    /// </summary>
    public static LinePlan Line(decimal needed, StockLevel have, Guid? usualStoreId, IEnumerable<PackOption> packs)
    {
        if (needed < 0)
            throw new ArgumentOutOfRangeException(nameof(needed), needed, "A meal never uses less than nothing.");
        decimal stock = have.State == StockState.InStock ? have.Quantity : 0;
        decimal shortfall = Math.Max(0, needed - stock);
        return shortfall == 0
            ? new LinePlan(LineNeed.InPantry, needed, stock, 0, null)
            : new LinePlan(LineNeed.Buy, needed, stock, shortfall, Choose(shortfall, usualStoreId, packs));
    }

    /// <summary>
    /// A ticked line the plan no longer needs (the toast skipped after the bread was bought). <b>What was bought stays
    /// bought</b>: the packs covering <paramref name="bought"/> - what its tick put in the pantry - still a line to buy and
    /// still in the Spend. Nothing bought (ticked "have it"): one pack, so ticking it bought after all buys one.
    /// </summary>
    public static LinePlan Unplanned(decimal? bought, Guid? usualStoreId, IEnumerable<PackOption> packs) =>
        bought is > 0 and { } quantity
            ? new LinePlan(LineNeed.Buy, 0, 0, quantity, Choose(quantity, usualStoreId, packs))
            : new LinePlan(LineNeed.Buy, 0, 0, 0, Choose(null, usualStoreId, packs));

    /// <summary>A staple running low or out: one pack, of the cheapest at its usual store.</summary>
    public static LinePlan Staple(Guid? usualStoreId, IEnumerable<PackOption> packs) =>
        new(LineNeed.Staple, 0, 0, 0, Choose(null, usualStoreId, packs));

    /// <summary>
    /// Whole packs covering <paramref name="shortfall"/> (null: one pack, for a staple). Packs at the
    /// <paramref name="usualStoreId"/> win when there are any - the list sends the planner there. Among them: a priced
    /// product before an unpriced one; then the cheapest for the packs needed, so 300 g of mayo is one 400 g jar, not a
    /// 2 kg tub cheaper per gram; then the least spare; then the product id, so every device picks the same one.
    /// </summary>
    public static PackChoice? Choose(decimal? shortfall, Guid? usualStoreId, IEnumerable<PackOption> packs)
    {
        if (shortfall is <= 0)
            throw new ArgumentOutOfRangeException(nameof(shortfall), shortfall, "Nothing to buy has no packs.");

        List<PackOption> sized = [.. packs.Where(p => p.PackQuantity > 0)];
        List<PackOption> usual = [.. sized.Where(p => usualStoreId is { } store && p.StoreId == store)];
        return (usual.Count > 0 ? usual : sized)
            .Select(p => Of(p, shortfall))
            .Where(c => c.Packs <= MaxPacks)
            .OrderBy(c => c.Cents is null)
            .ThenBy(c => c.Cents)
            .ThenBy(c => c.Spare)
            .ThenBy(c => c.ProductId)
            .FirstOrDefault();
    }

    private static PackChoice Of(PackOption pack, decimal? shortfall)
    {
        decimal packs = shortfall is { } need ? Math.Ceiling(need / pack.PackQuantity) : 1;
        int count = packs > MaxPacks ? MaxPacks + 1 : (int)packs;
        decimal spare = count * pack.PackQuantity - (shortfall ?? pack.PackQuantity);
        return new PackChoice(pack.ProductId, pack.StoreId, pack.PackQuantity, count, spare, pack.PriceCents * count);
    }

    /// <summary>
    /// What a line's packs cost, as a cost that may be incomplete: unpriced, or with no pack size at all, is one missing.
    /// </summary>
    public static Cost CostOf(LinePlan line) => line.Need switch
    {
        LineNeed.InPantry                  => new Cost(0, 0),
        _ when line.Pack?.Cents is { } c   => new Cost(c, 0),
        _                                  => new Cost(0, 1),
    };

    /// <summary>A cost of whole cents, or one missing when nobody gave a price.</summary>
    public static Cost Priced(long? cents) => cents is { } c ? new Cost(c, 0) : new Cost(0, 1);

    /// <summary><paramref name="serves"/> serves at a per-serve cost; null (nothing to cost it by) is one missing, never $0.</summary>
    public static Cost Serves(decimal serves, Cost? perServe)
    {
        if (serves < 0)
            throw new ArgumentOutOfRangeException(nameof(serves), serves, "Nobody eats fewer than no serves.");
        // A part serve - a toddler's half - costs that part, rounded to the cent once.
        return serves == 0 ? new Cost(0, 0)
            : perServe is { } each ? new Cost((long)Math.Round(each.Cents * serves, MidpointRounding.AwayFromZero), each.Complete ? 0 : 1)
            : new Cost(0, 1);
    }

    /// <summary>Costs added: the cents known, and how many could not be costed.</summary>
    public static Cost Sum(IEnumerable<Cost> costs)
    {
        long cents = 0;
        int missing = 0;
        foreach (Cost cost in costs)
        {
            cents   += cost.Cents;
            missing += cost.Missing;
        }
        return new Cost(cents, missing);
    }
}

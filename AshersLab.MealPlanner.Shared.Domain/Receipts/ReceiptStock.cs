using AshersLab.MealPlanner.Shared.Domain.Ingredients;

namespace AshersLab.MealPlanner.Shared.Domain.Receipts;

/// <summary>A shopping-list line ticked bought, and what its tick put into the pantry.</summary>
/// <param name="ItemId">The tick (<c>ShoppingItemRules.TickId</c>): derived, the same on every device.</param>
/// <param name="On">The day it was ticked - its movement's date.</param>
public sealed record TickBought(Guid ItemId, Guid IngredientId, DateOnly On, decimal Quantity);

/// <summary>
/// What a matched receipt line puts into the pantry, and <b>the reconciliation rule</b>: a receipt and a shopping-list tick
/// that describe the same purchase count once. Pure.
/// </summary>
/// <remarks>
/// The tick is the shopper's note in the shop ("mince, in the trolley"); the receipt is the record of what came home - 700 g,
/// not "a pack". So when both describe one purchase <b>the receipt wins</b>: its movement names the tick it covers
/// (<c>StockMovement.CoversItemId</c>), and from then on the tick's own movement does not count, on every device - the
/// cover names the tick, whose id is derived, so it holds even for a tick's movement that arrives after the receipt. The
/// tick itself stays ticked.
/// <para>
/// Which tick: a bought tick of the same ingredient, ticked from <see cref="DaysBefore"/> days before the receipt's date to
/// <see cref="DaysAfter"/> after (an online order ticked when it was placed; a shop ticked and its invoice dated at pickup),
/// not covered already - the nearest in date, then the later. One tick per line, one line per tick. The review shows each
/// cover ("replaces the shopping list's tick of Mon 19 Oct") and the planner can turn it off: two shops of mince in one
/// week are two purchases.
/// </para>
/// </remarks>
public static class ReceiptStock
{
    public const int DaysBefore = 7;

    /// <summary>A shop this many days old or newer goes into the pantry unless the planner says not; an older one does not.</summary>
    public const int RecentDays = 7;

    /// <summary>
    /// Whether the review starts with "add this shop's food to the pantry" on. An old receipt - the first import of the
    /// last few months' receipts, for their prices - would otherwise put long-eaten food into the pantry on its old date,
    /// nothing having been planned since to draw it down, and every shopping list after would think it is still there.
    /// </summary>
    public static bool IntoPantryByDefault(DateOnly purchasedOn, DateOnly today) => today.DayNumber - purchasedOn.DayNumber <= RecentDays;

    public const int DaysAfter = 2;

    /// <summary>The tick a receipt line for <paramref name="ingredient"/> bought on <paramref name="purchasedOn"/> covers, if any.</summary>
    /// <param name="taken">Ticks already covered - by an earlier receipt, or another line of this one.</param>
    public static TickBought? Covers(Guid ingredient, DateOnly purchasedOn, IEnumerable<TickBought> ticks, IReadOnlySet<Guid> taken) =>
        ticks.Where(t => t.IngredientId == ingredient && !taken.Contains(t.ItemId)
                         && t.On >= purchasedOn.AddDays(-DaysBefore) && t.On <= purchasedOn.AddDays(DaysAfter))
            .OrderBy(t => Math.Abs(t.On.DayNumber - purchasedOn.DayNumber))
            .ThenByDescending(t => t.On)
            .ThenBy(t => t.ItemId)
            .FirstOrDefault();

    /// <summary>
    /// What the line puts into the pantry, in the ingredient's base unit: the packs supplied times the product's pack - or,
    /// for weighed meat in grams, the weight supplied. Null when nothing came home.
    /// </summary>
    public static decimal? Quantity(ParsedLine line, decimal packQuantity, BaseUnit unit) =>
        line.Supplied <= 0 ? null
        : line.WeightGrams is { } grams && unit == BaseUnit.Gram ? grams
        : line.Supplied * packQuantity;
}

using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Pantry;
using AshersLab.MealPlanner.Shared.Domain.Shopping;

namespace AshersLab.MealPlanner.Shared.Data.Pantry;

/// <summary>
/// One change to one ingredient's stock, by hand or by a purchase: bought, adjusted, or counted (which sets the level).
/// Added, sometimes deleted as a mistake, never edited. What meals use is not here - it is read from the calendar
/// (<see cref="PantryBook"/>). The same row in the server's database and in every browser's; only ever written by
/// applying an op.
/// </summary>
public class StockMovement : ISyncedRow
{
    public Guid Id { get; set; }

    /// <summary>Not a foreign key: a movement of an ingredient deleted elsewhere is kept, and shown nowhere.</summary>
    public Guid IngredientId { get; set; }

    /// <summary>In the ingredient's base unit; negative for an adjustment down. For a count, the level it sets.</summary>
    public decimal Quantity { get; set; }

    public DateOnly OccurredOn { get; set; }

    public StockReason Reason { get; set; }

    /// <summary>The shopping-list tick that wrote it, or null for one made in the pantry. Not a foreign key.</summary>
    public Guid? ShoppingItemId { get; set; }

    public bool Deleted { get; set; }

    public StockEvent Event() => new(IngredientId, OccurredOn, StockEvent.From(Reason), Quantity, Id);

    /// <summary>
    /// The live movements that count toward stock. One a shopping-list tick wrote counts only while it is what the tick
    /// says now - <c>Bought</c> for a tick that is bought, <c>Adjusted</c> for "have it", nothing for an unticked one - and
    /// only the newest of those. <b>A tick undoes its last movement only on the phone that can see it</b>: two phones
    /// ticking the same line bought offline each write a pack, and after sync both are live rows. The line was bought once,
    /// and the tick (one row, its id derived) says which, so stock follows the tick rather than the rows.
    /// </summary>
    /// <param name="ticks">Every tick by its id. A movement naming a tick not seen yet counts, newest only.</param>
    public static IEnumerable<StockMovement> Counting(IEnumerable<StockMovement> live, IReadOnlyDictionary<Guid, ShoppingTick> ticks) =>
        live.GroupBy(m => m.ShoppingItemId)
            .SelectMany(g => g.Key is not { } item ? g
                : g.Where(m => !ticks.TryGetValue(item, out ShoppingTick tick) || Says(tick, m.Reason)).OrderByDescending(m => m.Id).Take(1));

    private static bool Says(ShoppingTick tick, StockReason reason) => tick switch
    {
        ShoppingTick.Bought => reason == StockReason.Bought,
        ShoppingTick.HaveIt => reason == StockReason.Adjusted,
        _                   => false,
    };
}

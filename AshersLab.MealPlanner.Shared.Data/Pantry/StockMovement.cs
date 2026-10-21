using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Pantry;

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
}

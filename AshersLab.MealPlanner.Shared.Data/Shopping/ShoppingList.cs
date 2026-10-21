using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Shopping;

namespace AshersLab.MealPlanner.Shared.Data.Shopping;

/// <summary>
/// A shopping list: the days whose meals it shops for. Its lines are computed (<see cref="ShoppingBook"/>); only its ticks
/// and extra items are stored, as <see cref="ShoppingListItem"/>s. The same row in the server's database and in every
/// browser's; only ever written by applying an op.
/// </summary>
public class ShoppingList : ISyncedRow
{
    /// <summary>Derived from the dates (<see cref="ShoppingListRules.ListId"/>).</summary>
    public Guid Id { get; set; }

    public DateOnly StartsOn { get; set; }

    public DateOnly EndsOn { get; set; }

    public bool Deleted { get; set; }
}

/// <summary>A tick against one of a list's computed lines, or an extra item typed by hand (<see cref="ShoppingItemRules"/>).</summary>
public class ShoppingListItem : ISyncedRow
{
    public Guid Id { get; set; }

    /// <summary>Not a foreign key, like every reference between synced rows.</summary>
    public Guid ListId { get; set; }

    /// <summary>A tick's ingredient; null for an extra item.</summary>
    public Guid? IngredientId { get; set; }

    /// <summary>An extra item's name; null for a tick.</summary>
    public string? Name { get; set; }

    public Guid? StoreId { get; set; }

    public long? PriceCents { get; set; }

    public ShoppingTick Tick { get; set; }

    public bool Deleted { get; set; }

    public bool IsExtra => IngredientId is null;
}

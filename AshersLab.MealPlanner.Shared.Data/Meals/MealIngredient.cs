using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Meals;

namespace AshersLab.MealPlanner.Shared.Data.Meals;

/// <summary>
/// One ingredient of one meal and how much of it a batch uses. The same row in the server's database and in every
/// browser's; only ever written by applying an op.
/// </summary>
public class MealIngredient : ISyncedRow
{
    public Guid Id { get; set; }

    /// <summary>Not a foreign key: a line of a meal deleted elsewhere is kept and shown nowhere.</summary>
    public Guid MealId { get; set; }

    /// <summary>Not a foreign key: an ingredient deleted elsewhere shows as "ingredient removed", unpriced.</summary>
    public Guid IngredientId { get; set; }

    /// <summary>In the ingredient's base unit, read in whatever unit it has now; null when not entered yet.</summary>
    public decimal? Quantity { get; set; }

    public string? Note { get; set; }

    public bool Deleted { get; set; }

    public MealIngredientDraft Draft() => new(MealId, IngredientId, Quantity, Note);
}

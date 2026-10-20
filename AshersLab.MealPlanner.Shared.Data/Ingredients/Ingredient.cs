using AshersLab.MealPlanner.Shared.Data.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Ingredients;

/// <summary>
/// The generic thing a recipe needs - "beef mince", "grated cheese". The same row in the server's database
/// and in every browser's; only ever written by applying an op.
/// </summary>
/// <remarks>
/// Only the name so far. Base unit, category, usual store and staple arrive with ingredient CRUD (slice 4 of
/// docs/work/2026-10-v1-scope.md), each as another field of <see cref="IngredientEntity"/>.
/// </remarks>
public class Ingredient : ISyncedRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public bool Deleted { get; set; }
}

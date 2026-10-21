using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;

namespace AshersLab.MealPlanner.Shared.Data.Ingredients;

/// <summary>
/// The generic thing a recipe needs - "beef mince", "grated cheese". The same row in the server's database
/// and in every browser's; only ever written by applying an op.
/// </summary>
public class Ingredient : ISyncedRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>
    /// What its quantities are counted in. Every create carries one; the default is only ever seen on a row an
    /// update reached before its create (and on a tombstone).
    /// </summary>
    public BaseUnit BaseUnit { get; set; }

    /// <summary>
    /// Null for none. Not a foreign key (docs/modules/catalogue/catalogue.md): it can name a category this database
    /// has not received yet, or one deleted elsewhere - both shown as "No category".
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Where it goes on the shopping list. Null for none; not a foreign key, as <see cref="CategoryId"/>.</summary>
    public Guid? UsualStoreId { get; set; }

    /// <summary>Used in small amounts (oil, soy sauce): costed like anything else, left off the shopping list.</summary>
    public bool IsStaple { get; set; }

    /// <summary>The unit is the spreadsheet import's guess, not yet confirmed: the page asks the planner to check it.</summary>
    public bool UnitGuessed { get; set; }

    public bool Deleted { get; set; }
}

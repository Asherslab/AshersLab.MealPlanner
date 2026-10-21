using AshersLab.MealPlanner.Shared.Data.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Categories;

/// <summary>
/// A shopping category, in the household's own order. The same row in the server's database and in every
/// browser's; only ever written by applying an op.
/// </summary>
public class Category : ISyncedRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>
    /// Sort key (<c>ListPosition</c>). Sort in memory, not in SQL: EF's SQLite provider stores decimal as text and
    /// will not order by it.
    /// </summary>
    public decimal Position { get; set; }

    public bool Deleted { get; set; }
}

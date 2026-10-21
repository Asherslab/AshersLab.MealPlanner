using AshersLab.MealPlanner.Shared.Data.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>A slot in the day, in the household's own order. Only ever written by applying an op.</summary>
public class Slot : ISyncedRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Sort key (<c>ListPosition</c>). Sort in memory, not in SQL - see <c>Category.Position</c>.</summary>
    public decimal Position { get; set; }

    public bool Deleted { get; set; }

    /// <summary>The day's order: position, then name, then id so it is the same everywhere.</summary>
    public static IEnumerable<Slot> InOrder(IEnumerable<Slot> slots) =>
        slots.OrderBy(s => s.Position).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Id);
}

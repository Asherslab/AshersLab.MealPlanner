using AshersLab.MealPlanner.Shared.Data.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>
/// A plan that repeats every <see cref="WeekCount"/> weeks. Its entries are rows of their own. Only ever written by
/// applying an op.
/// </summary>
public class PlanTemplate : ISyncedRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public int WeekCount { get; set; } = 1;

    /// <summary>The Monday its week 1 began on the calendar; null until it is first applied (<c>Rotation</c>).</summary>
    public DateOnly? StartsOn { get; set; }

    public bool Deleted { get; set; }
}

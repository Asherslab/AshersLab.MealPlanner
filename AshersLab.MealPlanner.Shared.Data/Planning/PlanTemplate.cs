using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Planning;

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

    /// <summary>The rotations it had before, as <see cref="RotationPhases.Format"/> writes them; null for none.</summary>
    public string? Earlier { get; set; }

    public bool Deleted { get; set; }

    /// <summary>The start and week count in effect on <paramref name="date"/> (<see cref="RotationPhases"/>).</summary>
    public (DateOnly? StartsOn, int WeekCount) RotationOn(DateOnly date) =>
        RotationPhases.On(RotationPhases.TryParse(Earlier, out IReadOnlyList<RotationPhase> earlier) ? earlier : [], StartsOn, WeekCount, date);
}

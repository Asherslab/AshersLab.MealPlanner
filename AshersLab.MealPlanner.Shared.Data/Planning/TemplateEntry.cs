using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Planning;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>
/// One thing eaten in a template's week, day and slot, by everyone or some people. Its meals are
/// <see cref="TemplateChoice"/> rows. Only ever written by applying an op.
/// </summary>
public class TemplateEntry : ISyncedRow
{
    public Guid Id { get; set; }

    /// <summary>Not a foreign key: an entry of a template deleted elsewhere is kept and shown nowhere.</summary>
    public Guid TemplateId { get; set; }

    /// <summary>From 1. One beyond the template's week count is kept and shown nowhere.</summary>
    public int Week { get; set; } = 1;

    public DayOfWeek Day { get; set; }

    /// <summary>Not a foreign key: a slot deleted elsewhere hides the entry.</summary>
    public Guid SlotId { get; set; }

    /// <summary>Null for everyone; otherwise <see cref="PlanPeople.Format"/>'s text. Read with <see cref="PeopleIds"/>.</summary>
    public string? People { get; set; }

    public bool Deleted { get; set; }

    /// <summary>Who eats it: null for everyone.</summary>
    public IReadOnlyList<Guid>? PeopleIds() => PlanPeople.TryParse(People, out IReadOnlyList<Guid>? people) ? people : null;
}

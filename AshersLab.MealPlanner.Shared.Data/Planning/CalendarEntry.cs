using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Planning;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>
/// One thing eaten on a real date: made by applying a plan, or added by hand. Its fields are what was planned plus what
/// the planner changed, each null for "as planned" (<see cref="CalendarEntryRules"/>); read it through
/// <see cref="CalendarBook"/>, which resolves them. Only ever written by applying an op.
/// </summary>
public class CalendarEntry : ISyncedRow
{
    public Guid Id { get; set; }

    /// <summary>Not a foreign key; null for an entry added by hand.</summary>
    public Guid? TemplateEntryId { get; set; }

    /// <summary>The date it was planned for. A move is <see cref="MovedDate"/>.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Not a foreign key: the slot it was planned for. A move is <see cref="MovedSlotId"/>.</summary>
    public Guid SlotId { get; set; }

    /// <summary>Not a foreign key. Null: the template entry's usual meal.</summary>
    public Guid? MealId { get; set; }

    /// <summary><see cref="CalendarPeople"/>'s text; null as planned.</summary>
    public string? People { get; set; }

    public CalendarStatus Status { get; set; }

    public DateOnly? MovedDate { get; set; }

    public Guid? MovedSlotId { get; set; }

    /// <summary>Not a foreign key: a leftovers entry's batch, the cooked entry it eats from. Null: whatever is left.</summary>
    public Guid? EatsFrom { get; set; }

    /// <summary>A cooked entry's serves made; null: the meal's serves per batch.</summary>
    public int? ServesMade { get; set; }

    public bool Deleted { get; set; }

    public bool Touched => CalendarEntryRules.Touched(MealId, People, Status, MovedDate, MovedSlotId, EatsFrom, ServesMade);
}

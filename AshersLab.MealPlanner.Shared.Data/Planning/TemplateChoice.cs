using AshersLab.MealPlanner.Shared.Data.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>
/// One meal a template entry may be. Choices list in the order they were added (GUIDv7 ids). Only ever written by
/// applying an op.
/// </summary>
public class TemplateChoice : ISyncedRow
{
    public Guid Id { get; set; }

    /// <summary>Not a foreign key: a choice of an entry deleted elsewhere is kept and shown nowhere.</summary>
    public Guid EntryId { get; set; }

    /// <summary>Not a foreign key: a meal deleted elsewhere shows as "meal removed".</summary>
    public Guid MealId { get; set; }

    public bool Deleted { get; set; }
}

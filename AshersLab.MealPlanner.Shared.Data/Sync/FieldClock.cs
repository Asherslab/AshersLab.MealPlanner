namespace AshersLab.MealPlanner.Shared.Data.Sync;

/// <summary>
/// The clock of the op whose value a field holds. This is what lets two devices' edits to different fields
/// both survive, and an older edit to the same field lose: the merge compares an incoming op with these.
/// </summary>
/// <remarks>
/// One table for every synced entity type, keyed by (<see cref="Entity"/>, <see cref="EntityId"/>,
/// <see cref="Field"/>), rather than a clock column per field - a new field needs no extra column.
/// </remarks>
public class FieldClock
{
    public required string Entity { get; set; }

    public Guid EntityId { get; set; }

    public required string Field { get; set; }

    /// <summary>The <see cref="Domain.Sync.Hlc"/> string form.</summary>
    public required string Hlc { get; set; }
}

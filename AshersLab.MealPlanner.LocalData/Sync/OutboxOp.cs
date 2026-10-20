using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.LocalData.Sync;

/// <summary>
/// An edit made in this browser and not yet confirmed by the server. Written in the same transaction as the
/// edit itself (<see cref="LocalOps.WriteAsync"/>), so an edit is never applied without being queued.
/// Removed when a push acknowledges it or a pull brings it back.
/// </summary>
public class OutboxOp
{
    public Guid OpId { get; set; }

    /// <summary>The <see cref="Hlc"/> string form. Its order is the order the edits were made in.</summary>
    public required string Hlc { get; set; }

    public required string Entity { get; set; }

    public Guid EntityId { get; set; }

    public OpKind Kind { get; set; }

    /// <summary><c>Shared.Data.Sync.OpFieldsJson</c>.</summary>
    public required string FieldsJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

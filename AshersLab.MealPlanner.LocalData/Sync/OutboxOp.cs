using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.LocalData.Sync;

/// <summary>
/// An edit made in this browser and not yet confirmed by the server. Written in the same transaction as the
/// edit itself (<see cref="LocalOps.WriteAsync"/>), so an edit is never applied without being queued.
/// </summary>
/// <remarks>
/// <b>Removed only once this browser's cursor has passed it</b> - a pull brings it back, or a push answers
/// "already have it" at a seq the cursor has reached - or when the server rejects it. A push's "accepted" alone
/// is not enough: if the server is then restored from a backup older than that push, the op is gone from the
/// server, and an op still here is simply pushed again.
/// </remarks>
public class OutboxOp
{
    public Guid OpId { get; set; }

    /// <summary>
    /// The device whose clock <see cref="Hlc"/> is. Null for this browser's own edits. Set only on the ops
    /// <see cref="LocalOps"/> queues to push back what a restored server lost - another device's edit, with its
    /// original clock, so it merges exactly as the original did.
    /// </summary>
    public Guid? DeviceId { get; set; }

    /// <summary>The <see cref="Hlc"/> string form. Its order is the order the edits were made in.</summary>
    public required string Hlc { get; set; }

    public required string Entity { get; set; }

    public Guid EntityId { get; set; }

    public OpKind Kind { get; set; }

    /// <summary><c>Shared.Data.Sync.OpFieldsJson</c>.</summary>
    public required string FieldsJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

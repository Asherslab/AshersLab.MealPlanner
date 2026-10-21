namespace AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;

/// <summary>
/// The sync protocol's version: the shape of the ops and entities a client sends and expects. Every push,
/// pull and change stream carries the client's; the server refuses one outside
/// [<see cref="OldestSupported"/>, <see cref="Current"/>] with <see cref="RefusedStatus"/>.
/// </summary>
/// <remarks>
/// <b>Bump <see cref="Current"/> when an entity's fields or meaning change in a way an older client would
/// misread</b>, and raise <see cref="OldestSupported"/> when the server can no longer accept the older one.
/// A browser can be offline across a deploy: it is refused, reloads to the new app, migrates its own SQLite,
/// and pushes its outbox afterwards (docs/modules/sync/offline-sync.md). While nothing is in production,
/// wipe both databases instead of keeping old versions alive.
/// </remarks>
public static class SyncProtocol
{
    /// <summary>
    /// 2: a pull carries the chain at its cursor and may be answered with a <c>HistoryLost</c> snapshot, and an op
    /// stays queued until the cursor passes it. A version-1 browser sends no chain, so the server cannot tell it its
    /// cursor names ops a restore renumbered, and it cannot read the snapshot's reason - refused, so it reloads.
    /// </summary>
    public const int Current = 2;

    public const int OldestSupported = 2;

    /// <summary>
    /// The gRPC status for a refused version is <c>FailedPrecondition</c> with this as its detail, so the client
    /// can tell it from every other failure without parsing prose.
    /// </summary>
    public const string RefusedStatus = "sync-protocol-unsupported";
}

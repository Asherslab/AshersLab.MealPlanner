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
/// and pushes its outbox afterwards (docs/modules/sync/offline-sync.md). The app holds real data: never wipe
/// either database to get past a version.
/// </remarks>
public static class SyncProtocol
{
    /// <summary>
    /// 2: a pull carries the chain at its cursor and may be answered with a <c>HistoryLost</c> snapshot, and an op
    /// stays queued until the cursor passes it. A version-1 browser sends no chain, so the server cannot tell it its
    /// cursor names ops a restore renumbered, and it cannot read the snapshot's reason - refused, so it reloads.
    /// <para>
    /// 3: a slot has <c>People</c>, who usually eats in it. A version-2 browser throws on a slot op that carries it,
    /// so it is refused too.
    /// </para>
    /// </summary>
    public const int Current = 3;

    public const int OldestSupported = 3;

    /// <summary>
    /// The gRPC status for a refused version is <c>FailedPrecondition</c> with this as its detail, so the client
    /// can tell it from every other failure without parsing prose.
    /// </summary>
    public const string RefusedStatus = "sync-protocol-unsupported";
}

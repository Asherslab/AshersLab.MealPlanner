using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Messages.Requests.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Messages.Responses.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.LocalData.Sync;

/// <param name="Changed">A pull changed rows here - pages should re-read.</param>
/// <param name="Refused">The server refused some pushed ops; how many, and the first reason.</param>
/// <param name="ServerEpoch">The server database pulled from.</param>
/// <param name="PulledThroughSeq">Where this browser's cursor now is.</param>
/// <param name="PushedBack">
/// Ops this browser queued to give back to a server restored from an older backup (0 almost always).
/// </param>
public sealed record SyncRoundResult(bool Changed, (int Count, string? FirstReason) Refused, Guid ServerEpoch, long PulledThroughSeq, int PushedBack = 0);

/// <summary>
/// One sync round: push the whole outbox, then pull until caught up - and after a snapshot, push and pull once
/// more. Network failures are the caller's to catch - nothing here leaves the database half-done, because each
/// step commits on its own and repeating a step is harmless (the server skips op ids it has; a pulled op already
/// applied changes nothing).
/// </summary>
/// <remarks>
/// Why the second pass: an op leaves the outbox only once the cursor has passed it (<see cref="OutboxOp"/>). After
/// a normal pull that is true of everything just pushed. After a snapshot it is not known - a snapshot carries rows,
/// not op ids - and a restored server's snapshot also queues ops to push back. Pushing again settles both: the
/// server answers "already have it" below the new cursor for the first kind, and logs the second, which the
/// second pull brings back.
/// <para>
/// Here rather than in the WASM app so the test suite can run real rounds against the real host. The WASM
/// <c>SyncLoop</c> decides when to run one.
/// </para>
/// </remarks>
public static class SyncRound
{
    public const int PushBatch = 200;
    public const int PullBatch = 200;

    public static async Task<SyncRoundResult> RunAsync(
        IDbContextFactory<LocalDbContext> contexts,
        ISyncService                      server,
        Guid                              deviceId,
        CancellationToken                 cancellationToken = default,
        int                               pullBatch         = PullBatch
    )
    {
        (int count, string? reason) refused = await PushAsync(contexts, server, deviceId, cancellationToken);
        Pulled pulled = await PullAsync(contexts, server, deviceId, pullBatch, cancellationToken);

        bool changed    = pulled.Changed;
        int  pushedBack = pulled.Queued;

        if (pulled.TookSnapshot)
        {
            (int count, string? reason) again = await PushAsync(contexts, server, deviceId, cancellationToken);
            refused = (refused.count + again.count, refused.reason ?? again.reason);

            pulled  =  await PullAsync(contexts, server, deviceId, pullBatch, cancellationToken);
            changed |= pulled.Changed;
            pushedBack += pulled.Queued;
        }

        return new SyncRoundResult(changed, refused, pulled.Epoch, pulled.Seq, pushedBack);
    }

    private static async Task<(int, string?)> PushAsync(
        IDbContextFactory<LocalDbContext> contexts, ISyncService server, Guid deviceId, CancellationToken ct)
    {
        int     refused     = 0;
        string? firstReason = null;

        List<OutboxOp> pending;
        await using (LocalDbContext db = await contexts.CreateDbContextAsync(ct))
            pending = await LocalOps.PendingAsync(db, ct);

        foreach (OutboxOp[] batch in pending.Chunk(PushBatch))
        {
            PushResponse response = await server.Push(new PushRequest
            {
                ProtocolVersion = SyncProtocol.Current,
                Ops             = [.. batch.Select(op => ToContract(op, deviceId))],
            }, ct);

            foreach (PushResult r in response.Results.Where(r => r.Outcome == PushOutcome.Rejected))
            {
                refused++;
                firstReason ??= r.Reason;
            }

            await using LocalDbContext db = await contexts.CreateDbContextAsync(ct);
            await LocalOps.AcknowledgeAsync(db, response.ServerEpoch,
                [.. response.Results.Select(r => new PushAnswer(r.OpId, r.Outcome == PushOutcome.Rejected, r.Seq))], ct);
        }

        return (refused, firstReason);
    }

    private sealed record Pulled(bool Changed, Guid Epoch, long Seq, bool TookSnapshot, int Queued);

    private static async Task<Pulled> PullAsync(
        IDbContextFactory<LocalDbContext> contexts, ISyncService server, Guid deviceId, int pullBatch, CancellationToken ct)
    {
        bool changed  = false;
        bool snapshot = false;
        int  queued   = 0;

        while (true)
        {
            SyncState state;
            await using (LocalDbContext db = await contexts.CreateDbContextAsync(ct))
                state = await db.SyncStates.AsNoTracking().SingleOrDefaultAsync(ct) ?? new SyncState();

            PullResponse response = await server.Pull(new PullRequest
            {
                ProtocolVersion = SyncProtocol.Current,
                ServerEpoch     = state.ServerEpoch,
                SinceSeq        = state.PulledThroughSeq,
                SinceChain      = state.PulledThroughChain,
                Limit           = pullBatch,
            }, ct);

            long seq;
            await using (LocalDbContext db = await contexts.CreateDbContextAsync(ct))
            {
                if (response.Snapshot is { } s)
                {
                    queued += await LocalOps.ApplySnapshotAsync(db, deviceId, response.ServerEpoch, KindOf(s.Reason),
                        s.AsOfSeq, s.AsOfChain, [.. s.Entities.Select(FromContract)], DateTimeOffset.UtcNow, ct);
                    changed  = true;
                    snapshot = true;
                    seq      = s.AsOfSeq;
                }
                else
                {
                    changed |= await LocalOps.ApplyPulledAsync(db, deviceId, response.ServerEpoch,
                        [.. response.Ops.Select(FromContract)], DateTimeOffset.UtcNow, ct);
                    seq = response.Ops.Count > 0 ? response.Ops[^1].Seq : state.PulledThroughSeq;
                }
            }

            if (!response.HasMore)
                return new Pulled(changed, response.ServerEpoch, seq, snapshot, queued);
        }
    }

    private static SnapshotKind KindOf(SnapshotReason reason) => reason switch
    {
        SnapshotReason.FirstLoad   => SnapshotKind.FirstLoad,
        SnapshotReason.NewDatabase => SnapshotKind.NewDatabase,
        SnapshotReason.HistoryLost => SnapshotKind.HistoryLost,
        _ => throw new InvalidOperationException(
                 $"Unknown snapshot reason {(int)reason} - the sync protocol version check should have refused this."),
    };

    private static SyncOp ToContract(OutboxOp op, Guid deviceId) => new()
    {
        OpId     = op.OpId,
        DeviceId = op.DeviceId ?? deviceId,
        Hlc      = op.Hlc,
        Entity   = op.Entity,
        EntityId = op.EntityId,
        Kind     = op.Kind,
        Fields   = [.. OpFieldsJson.Read(op.FieldsJson).Select(f => new SyncField { Name = f.Name, Value = f.Value })],
    };

    private static PulledOp FromContract(SyncOp op) =>
        new(op.OpId, Hlc.Parse(op.Hlc), op.Entity, op.EntityId, op.Kind, [.. op.Fields.Select(f => new OpField(f.Name, f.Value))],
            op.Seq, op.Chain ?? "");

    private static SnapshotRow FromContract(SnapshotEntity entity) =>
        new(entity.Entity, entity.EntityId, entity.Deleted, [.. entity.Fields.Select(f => (new OpField(f.Name, f.Value), Hlc.Parse(f.Hlc)))]);
}

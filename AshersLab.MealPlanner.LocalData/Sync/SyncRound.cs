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
public sealed record SyncRoundResult(bool Changed, (int Count, string? FirstReason) Refused, Guid ServerEpoch, long PulledThroughSeq);

/// <summary>
/// One sync round: push the whole outbox, then pull until caught up. Network failures are the caller's to catch -
/// nothing here leaves the database half-done, because each step commits on its own and repeating a step is
/// harmless (the server skips op ids it has; a pulled op already applied changes nothing).
/// </summary>
/// <remarks>
/// Here rather than in the WASM app so the test suite can run real rounds against the real host. The WASM
/// <c>SyncLoop</c> decides when to run one.
/// </remarks>
public static class SyncRound
{
    public const int PushBatch = 200;
    public const int PullBatch = 200;

    public static async Task<SyncRoundResult> RunAsync(
        IDbContextFactory<LocalDbContext> contexts,
        ISyncService                      server,
        Guid                              deviceId,
        CancellationToken                 cancellationToken = default
    )
    {
        (int, string?) refused = await PushAsync(contexts, server, deviceId, cancellationToken);
        (bool changed, Guid epoch, long seq) = await PullAsync(contexts, server, deviceId, cancellationToken);

        return new SyncRoundResult(changed, refused, epoch, seq);
    }

    private static async Task<(int, string?)> PushAsync(
        IDbContextFactory<LocalDbContext> contexts, ISyncService server, Guid deviceId, CancellationToken ct)
    {
        int     refused     = 0;
        string? firstReason = null;

        while (true)
        {
            List<OutboxOp> pending;
            await using (LocalDbContext db = await contexts.CreateDbContextAsync(ct))
                pending = await LocalOps.PendingAsync(db, PushBatch, ct);

            if (pending.Count == 0)
                return (refused, firstReason);

            PushResponse response = await server.Push(new PushRequest
            {
                ProtocolVersion = SyncProtocol.Current,
                Ops             = [.. pending.Select(op => ToContract(op, deviceId))],
            }, ct);

            foreach (PushResult r in response.Results.Where(r => r.Outcome == PushOutcome.Rejected))
            {
                refused++;
                firstReason ??= r.Reason;
            }

            // Every answered op leaves the outbox, refused ones included: they would be refused again forever.
            await using (LocalDbContext db = await contexts.CreateDbContextAsync(ct))
                await LocalOps.AcknowledgeAsync(db, [.. response.Results.Select(r => r.OpId)], ct);

            if (pending.Count < PushBatch)
                return (refused, firstReason);
        }
    }

    private static async Task<(bool, Guid, long)> PullAsync(
        IDbContextFactory<LocalDbContext> contexts, ISyncService server, Guid deviceId, CancellationToken ct)
    {
        bool changed = false;

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
                Limit           = PullBatch,
            }, ct);

            long seq;
            await using (LocalDbContext db = await contexts.CreateDbContextAsync(ct))
            {
                if (response.Snapshot is { } snapshot)
                {
                    await LocalOps.ApplySnapshotAsync(db, deviceId, response.ServerEpoch, snapshot.AsOfSeq,
                        [.. snapshot.Entities.Select(FromContract)], DateTimeOffset.UtcNow, ct);
                    changed = true;
                    seq     = snapshot.AsOfSeq;
                }
                else
                {
                    changed |= await LocalOps.ApplyPulledAsync(db, deviceId, response.ServerEpoch,
                        [.. response.Ops.Select(FromContract)], DateTimeOffset.UtcNow, ct);
                    seq = response.Ops.Count > 0 ? response.Ops[^1].Seq : state.PulledThroughSeq;
                }
            }

            if (!response.HasMore)
                return (changed, response.ServerEpoch, seq);
        }
    }

    private static SyncOp ToContract(OutboxOp op, Guid deviceId) => new()
    {
        OpId     = op.OpId,
        DeviceId = deviceId,
        Hlc      = op.Hlc,
        Entity   = op.Entity,
        EntityId = op.EntityId,
        Kind     = op.Kind,
        Fields   = [.. OpFieldsJson.Read(op.FieldsJson).Select(f => new SyncField { Name = f.Name, Value = f.Value })],
    };

    private static PulledOp FromContract(SyncOp op) =>
        new(op.OpId, Hlc.Parse(op.Hlc), op.Entity, op.EntityId, op.Kind, [.. op.Fields.Select(f => new OpField(f.Name, f.Value))], op.Seq);

    private static SnapshotRow FromContract(SnapshotEntity entity) =>
        new(entity.Entity, entity.EntityId, entity.Deleted, [.. entity.Fields.Select(f => (new OpField(f.Name, f.Value), Hlc.Parse(f.Hlc)))]);
}

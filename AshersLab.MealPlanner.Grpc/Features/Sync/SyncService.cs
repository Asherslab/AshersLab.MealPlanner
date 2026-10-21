using System.Data;
using System.Runtime.CompilerServices;
using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Data.Models.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Messages.Requests.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Messages.Responses.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ProtoBuf.Grpc;

namespace AshersLab.MealPlanner.Grpc.Features.Sync;

/// <summary>
/// Accepts ops into the log and the materialised tables, and hands them back out. See
/// docs/modules/sync/offline-sync.md for the protocol; <see cref="OpApplier"/> and <c>OpMerge</c> for what
/// an op does.
/// </summary>
public class SyncService(
    MealPlannerDbContext db,
    SyncSignal           signal,
    ILogger<SyncService> logger
) : ISyncService
{
    public const int MaxPushOps   = 500;
    public const int MaxPullOps   = 500;
    public const int DefaultPullOps = 200;

    /// <summary>
    /// How often an idle change stream repeats itself. Keeps a proxy in between (Home Assistant ingress) from
    /// closing a quiet stream, and lets the browser notice a dead one.
    /// </summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(25);

    public async Task<PushResponse> Push(PushRequest request, CallContext context = default)
    {
        RefuseUnsupported(request.ProtocolVersion);

        if (request.Ops.Count > MaxPushOps)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"At most {MaxPushOps} ops per push."));

        CancellationToken ct = context.CancellationToken;
        List<PushResult>  results = new(request.Ops.Count);
        DateTime          now = DateTime.UtcNow;
        long              latest = 0;

        // BEGIN IMMEDIATE: take the write lock up front, so a concurrent push waits instead of failing its
        // upgrade from read to write with SQLITE_BUSY. One transaction for the batch, per the design.
        await using (IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            // Read under the write lock, so no other push can log an op between this and the chain it extends.
            string chain = await db.Ops.OrderByDescending(o => o.Seq).Select(o => o.Chain).FirstOrDefaultAsync(ct)
                        ?? LogChain.Empty;

            foreach (SyncOp op in request.Ops)
            {
                long? seen = await db.Ops.Where(o => o.OpId == op.OpId).Select(o => (long?)o.Seq).FirstOrDefaultAsync(ct);
                if (seen is { } originalSeq)
                {
                    results.Add(new PushResult { OpId = op.OpId, Outcome = PushOutcome.Duplicate, Seq = originalSeq });
                    continue;
                }

                List<OpField> fields = [.. op.Fields.Select(f => new OpField(f.Name, f.Value))];

                if (Problem(op, fields, out SyncedEntity? type, out Hlc clock) is { } problem)
                {
                    logger.LogWarning("Rejected op {OpId} from device {DeviceId}: {Problem}", op.OpId, op.DeviceId, problem);
                    results.Add(new PushResult { OpId = op.OpId, Outcome = PushOutcome.Rejected, Reason = problem });
                    continue;
                }

                await OpApplier.ApplyAsync(db, type!, op.EntityId, op.Kind, clock, fields, ct);

                chain = LogChain.Next(chain, op.OpId);

                // Logged whether or not it won its merge: every browser must see the same ops to reach the same
                // answer, and a losing op is how they learn it lost.
                DbOp row = new()
                {
                    Chain         = chain,
                    OpId          = op.OpId,
                    DeviceId      = op.DeviceId,
                    Hlc           = op.Hlc,
                    Entity        = op.Entity,
                    EntityId      = op.EntityId,
                    Kind          = op.Kind,
                    FieldsJson    = OpFieldsJson.Write(fields),
                    ReceivedAtUtc = now,
                };
                db.Ops.Add(row);
                await db.SaveChangesAsync(ct);

                latest = row.Seq;
                results.Add(new PushResult { OpId = op.OpId, Outcome = PushOutcome.Accepted, Seq = row.Seq });
            }

            await transaction.CommitAsync(ct);
        }

        if (latest > 0)
            signal.Published(latest);

        return new PushResponse { Results = results, ServerEpoch = signal.Epoch };
    }

    public async Task<PullResponse> Pull(PullRequest request, CallContext context = default)
    {
        RefuseUnsupported(request.ProtocolVersion);

        CancellationToken ct    = context.CancellationToken;
        int               limit = request.Limit is > 0 and <= MaxPullOps ? request.Limit : DefaultPullOps;

        // One read transaction: the snapshot and the seq it is "as of" must come from the same instant.
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);

        if (await SnapshotReasonAsync(request, ct) is { } reason)
        {
            // Send everything, as of now.
            var head = await db.Ops.OrderByDescending(o => o.Seq).Select(o => new { o.Seq, o.Chain }).FirstOrDefaultAsync(ct);
            List<SnapshotRow> rows = await OpApplier.SnapshotAsync(db, ct);

            if (reason == SnapshotReason.HistoryLost)
                logger.LogWarning(
                    "A browser has pulled through seq {Seq}, and this log does not hold that history (head {Head}): the " +
                    "database was restored from an older backup. Sending a snapshot it will push its lost edits back from.",
                    request.SinceSeq, head?.Seq ?? 0);

            return new PullResponse
            {
                ServerEpoch = signal.Epoch,
                Snapshot = new SyncSnapshot
                {
                    AsOfSeq   = head?.Seq ?? 0,
                    AsOfChain = head?.Chain ?? LogChain.Empty,
                    Entities  = [.. rows.Select(ToContract)],
                    Reason    = reason,
                },
            };
        }

        List<DbOp> ops = await db.Ops.AsNoTracking()
            .Where(o => o.Seq > request.SinceSeq)
            .OrderBy(o => o.Seq)
            .Take(limit + 1)
            .ToListAsync(ct);

        return new PullResponse
        {
            ServerEpoch = signal.Epoch,
            Ops         = [.. ops.Take(limit).Select(ToContract)],
            HasMore     = ops.Count > limit,
        };
    }

    public async IAsyncEnumerable<ChangesNotice> Changes(ChangesRequest request, CallContext context = default)
    {
        RefuseUnsupported(request.ProtocolVersion);

        CancellationToken ct = context.CancellationToken;

        while (!ct.IsCancellationRequested)
        {
            long latest = signal.LatestSeq;
            yield return new ChangesNotice { ServerEpoch = signal.Epoch, LatestSeq = latest };

            await signal.WaitAsync(latest, Heartbeat, ct);
        }
    }

    /// <summary>
    /// Null when the cursor is a point in this log: same database, and the op at its seq has the chain the browser
    /// pulled with it - so the log up to there is exactly what the browser has seen.
    /// </summary>
    private async Task<SnapshotReason?> SnapshotReasonAsync(PullRequest request, CancellationToken ct)
    {
        if (request.ServerEpoch is null)
            return SnapshotReason.FirstLoad;
        if (request.ServerEpoch != signal.Epoch)
            return SnapshotReason.NewDatabase;
        if (request.SinceSeq == 0)
            return null;

        // Missing: the log ends before the cursor. Different: the seq was numbered again after a restore.
        string? held = await db.Ops.Where(o => o.Seq == request.SinceSeq).Select(o => o.Chain).FirstOrDefaultAsync(ct);
        return held is not null && held == request.SinceChain ? null : SnapshotReason.HistoryLost;
    }

    private static void RefuseUnsupported(int version)
    {
        if (version is < SyncProtocol.OldestSupported or > SyncProtocol.Current)
            throw new RpcException(
                new Status(StatusCode.FailedPrecondition, SyncProtocol.RefusedStatus),
                new Metadata
                {
                    { "sync-protocol-oldest", SyncProtocol.OldestSupported.ToString() },
                    { "sync-protocol-current", SyncProtocol.Current.ToString() },
                });
    }

    private static string? Problem(SyncOp op, IReadOnlyList<OpField> fields, out SyncedEntity? type, out Hlc clock)
    {
        type  = SyncedEntities.Find(op.Entity);
        clock = default;

        if (type is null)
            return $"Unknown entity '{op.Entity}'.";
        // What LocalOps mints itself. Everything about the op's content is type.Problem, the same check the
        // browser makes before it applies an edit.
        if (op.OpId == Guid.Empty || op.DeviceId == Guid.Empty)
            return "An op needs an op id and a device id.";
        if (!Hlc.TryParse(op.Hlc, out clock) || clock.DeviceId != op.DeviceId)
            return "The op's clock is unreadable or names another device.";

        return type.Problem(op.Kind, op.EntityId, fields);
    }

    private static SyncOp ToContract(DbOp op) => new()
    {
        OpId     = op.OpId,
        DeviceId = op.DeviceId,
        Hlc      = op.Hlc,
        Entity   = op.Entity,
        EntityId = op.EntityId,
        Kind     = op.Kind,
        Fields   = [.. OpFieldsJson.Read(op.FieldsJson).Select(f => new SyncField { Name = f.Name, Value = f.Value })],
        Seq      = op.Seq,
        Chain    = op.Chain,
    };

    private static SnapshotEntity ToContract(SnapshotRow row) => new()
    {
        Entity   = row.Entity,
        EntityId = row.EntityId,
        Deleted  = row.Deleted,
        Fields   = [.. row.Fields.Select(f => new SnapshotField { Name = f.Field.Name, Value = f.Field.Value, Hlc = f.Clock.ToString() })],
    };
}

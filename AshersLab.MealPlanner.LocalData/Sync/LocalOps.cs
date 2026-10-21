using System.Data;
using System.Security.Cryptography;
using System.Text;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AshersLab.MealPlanner.LocalData.Sync;

/// <summary>An op as pulled from the server, already off the wire.</summary>
public sealed record PulledOp(Guid OpId, Hlc Clock, string Entity, Guid EntityId, OpKind Kind, IReadOnlyList<OpField> Fields, long Seq, string Chain);

/// <summary>The server's answer for one pushed op: rejected, or logged at <paramref name="Seq"/>.</summary>
public sealed record PushAnswer(Guid OpId, bool Rejected, long Seq);

/// <summary>Why a snapshot came instead of ops - the wire's <c>SnapshotReason</c>, which this project does not see.</summary>
public enum SnapshotKind
{
    FirstLoad,
    NewDatabase,
    HistoryLost,
}

/// <summary>
/// Every write to the browser's synced tables. A page never sets an entity's properties and saves - it calls
/// <see cref="WriteAsync"/>, which applies the edit and queues it in one transaction.
/// </summary>
/// <remarks>
/// The context passed in must come from <see cref="SerialLocalDbContextFactory"/> in the browser - see there for why
/// a window must never have two open. Each write is one IMMEDIATE transaction (Serializable is how the SQLite providers spell it): every window
/// of the app shares this database, and the clock and cursor are read-modify-write. None of them waits on the
/// network inside its transaction - the shared database holds up every other window while one is open.
/// </remarks>
public static class LocalOps
{
    /// <summary>
    /// Makes an edit here and queues it for the server. Returns the queued op. The caller has already validated
    /// the fields with the entity's rules; this checks again with exactly the server's content check
    /// (<see cref="SyncedEntity.Problem"/>), because an edit the server then rejects would stay applied here.
    /// </summary>
    public static async Task<OutboxOp> WriteAsync(
        LocalDbContext         db,
        Guid                   deviceId,
        DateTimeOffset         now,
        SyncedEntity           type,
        Guid                   entityId,
        OpKind                 kind,
        IReadOnlyList<OpField> fields,
        CancellationToken      cancellationToken = default
    )
    {
        if (type.Problem(kind, entityId, fields) is { } problem)
            throw new ArgumentException(problem, nameof(fields));

        await using IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        SyncState state = await StateAsync(db, cancellationToken);
        Hlc       clock = Hlc.Next(state.Clock is null ? null : Hlc.Parse(state.Clock), now.ToUnixTimeMilliseconds(), deviceId);
        state.Clock = clock.ToString();

        await OpApplier.ApplyAsync(db, type, entityId, kind, clock, fields, cancellationToken);

        OutboxOp op = new()
        {
            OpId       = Guid.CreateVersion7(now),
            Hlc        = clock.ToString(),
            Entity     = type.Entity,
            EntityId   = entityId,
            Kind       = kind,
            FieldsJson = OpFieldsJson.Write(fields),
            CreatedAt  = now,
        };
        db.Outbox.Add(op);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return op;
    }

    /// <summary>Every queued op, in the order the edits were made.</summary>
    /// <remarks>
    /// All of them, not a page: an op stays queued after a push accepts it until the cursor passes it, so a
    /// "next page" of the outbox is not the ops after the first page's removal.
    /// </remarks>
    public static Task<List<OutboxOp>> PendingAsync(LocalDbContext db, CancellationToken cancellationToken = default) =>
        db.Outbox.AsNoTracking().OrderBy(o => o.Hlc).ThenBy(o => o.OpId).ToListAsync(cancellationToken);

    /// <summary>
    /// Takes a push's answers. Drops what the server rejected (it would be rejected forever) and what it holds at a
    /// seq this browser's cursor has already passed (a retry of something since pulled, or something a snapshot
    /// included). Everything else it accepted stays queued until a pull brings it back - see <see cref="OutboxOp"/>.
    /// </summary>
    public static async Task AcknowledgeAsync(
        LocalDbContext db, Guid serverEpoch, IReadOnlyCollection<PushAnswer> answers, CancellationToken cancellationToken = default)
    {
        if (answers.Count == 0)
            return;

        SyncState? state  = await db.SyncStates.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        bool       sameDb = state?.ServerEpoch == serverEpoch;

        List<Guid> done =
        [
            .. answers
                .Where(a => a.Rejected || (sameDb && a.Seq > 0 && a.Seq <= state!.PulledThroughSeq))
                .Select(a => a.OpId),
        ];

        if (done.Count > 0)
            await db.Outbox.Where(o => done.Contains(o.OpId)).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Applies a page of pulled ops, in seq order, and moves the cursor past them. Returns whether any changed
    /// what this browser shows.
    /// </summary>
    /// <remarks>
    /// This device's own ops come back too: their clocks equal the ones already held, so they change nothing,
    /// and their arrival confirms them - this is where an accepted op leaves the outbox.
    /// </remarks>
    public static async Task<bool> ApplyPulledAsync(
        LocalDbContext          db,
        Guid                    deviceId,
        Guid                    serverEpoch,
        IReadOnlyList<PulledOp> ops,
        DateTimeOffset          now,
        CancellationToken       cancellationToken = default
    )
    {
        await using IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        SyncState state   = await StateAsync(db, cancellationToken);
        Hlc?      clock   = state.Clock is null ? null : Hlc.Parse(state.Clock);
        bool      changed = false;

        foreach (PulledOp op in ops.OrderBy(o => o.Seq))
        {
            // Another window may have applied this page already; the cursor says so.
            if (op.Seq <= state.PulledThroughSeq)
                continue;

            SyncedEntity type = SyncedEntities.Find(op.Entity)
                             ?? throw new InvalidOperationException(
                                    $"Pulled an op for unknown entity '{op.Entity}' - the sync protocol version check should have refused this.");

            MergeResult result = await OpApplier.ApplyAsync(db, type, op.EntityId, op.Kind, op.Clock, op.Fields, cancellationToken);
            changed |= result.ChangesAnything;

            clock                    = Hlc.Observe(clock, op.Clock, deviceId);
            state.PulledThroughSeq   = op.Seq;
            state.PulledThroughChain = op.Chain;
        }

        List<Guid> confirmed = [.. ops.Select(o => o.OpId)];
        await db.Outbox.Where(o => confirmed.Contains(o.OpId)).ExecuteDeleteAsync(cancellationToken);

        state.ServerEpoch  = serverEpoch;
        state.Clock        = clock?.ToString();
        state.LastPulledAt = now;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return changed;
    }

    /// <summary>
    /// Takes a snapshot of the server's data and moves the cursor to it. Unsynced local edits survive: a snapshot is
    /// merged field by field like ops. What else happens depends on <paramref name="kind"/>:
    /// <list type="bullet">
    /// <item><see cref="SnapshotKind.NewDatabase"/>: everything synced is forgotten first (the old server's rows are
    ///   not in the new one) and the outbox is replayed on top.</item>
    /// <item><see cref="SnapshotKind.HistoryLost"/>: the server was restored from an older backup, so this browser
    ///   holds edits the server no longer has - its own and other devices'. Every field newer here than in the
    ///   snapshot, and every tombstone the snapshot lacks, is queued to be pushed back, with its original clock.</item>
    /// </list>
    /// Returns how many ops it queued; the caller pushes them.
    /// </summary>
    public static async Task<int> ApplySnapshotAsync(
        LocalDbContext             db,
        Guid                       deviceId,
        Guid                       serverEpoch,
        SnapshotKind               kind,
        long                       asOfSeq,
        string                     asOfChain,
        IReadOnlyList<SnapshotRow> rows,
        DateTimeOffset             now,
        CancellationToken          cancellationToken = default
    )
    {
        await using IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        SyncState state  = await StateAsync(db, cancellationToken);
        Hlc?      clock  = state.Clock is null ? null : Hlc.Parse(state.Clock);
        int       queued = 0;

        if (kind == SnapshotKind.NewDatabase)
        {
            // The server's database was replaced. Rows synced from the old one are not in the new one, so they go;
            // edits still queued are replayed and will be pushed to the new database.
            await OpApplier.ForgetAllAsync(db, cancellationToken);
            state = await StateAsync(db, cancellationToken);

            foreach (OutboxOp op in await PendingAsync(db, cancellationToken))
            {
                SyncedEntity? type = SyncedEntities.Find(op.Entity);
                if (type is not null)
                    await OpApplier.ApplyAsync(db, type, op.EntityId, op.Kind, Hlc.Parse(op.Hlc),
                        OpFieldsJson.Read(op.FieldsJson), cancellationToken);
            }
        }
        else if (kind == SnapshotKind.HistoryLost)
        {
            (queued, clock) = await QueueWhatTheServerLostAsync(db, deviceId, rows, clock, now, cancellationToken);
        }

        foreach (SnapshotRow row in rows)
        {
            await OpApplier.ApplySnapshotRowAsync(db, row, cancellationToken);
            foreach ((OpField _, Hlc fieldClock) in row.Fields)
                clock = Hlc.Observe(clock, fieldClock, deviceId);
        }

        state.ServerEpoch        = serverEpoch;
        state.PulledThroughSeq   = asOfSeq;
        state.PulledThroughChain = asOfChain;
        state.Clock              = clock?.ToString();
        state.LastPulledAt       = now;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return queued;
    }

    /// <summary>
    /// Compares this browser's synced rows with a restored server's snapshot and queues an op for everything the
    /// server is missing. Field values go back with the clock that set them, as an op from the device that made
    /// the edit, so they merge on the server exactly as the original ops did: newer than what the server has wins,
    /// older loses, equal changes nothing.
    /// </summary>
    /// <remarks>
    /// The op id is derived from the edit, not minted, so two browsers pushing back the same lost edit send one op
    /// id and the server logs it once. A lost delete has no clock to keep (a tombstone carries none, and needs
    /// none - delete wins), so it goes back as a new delete from this device.
    /// </remarks>
    private static async Task<(int Queued, Hlc? Clock)> QueueWhatTheServerLostAsync(
        LocalDbContext db, Guid deviceId, IReadOnlyList<SnapshotRow> server, Hlc? clock, DateTimeOffset now, CancellationToken ct)
    {
        Dictionary<(string, Guid), SnapshotRow> theirs = server.ToDictionary(r => (r.Entity, r.EntityId));
        HashSet<string> alreadyQueued = [.. await db.Outbox.Select(o => o.Hlc).ToListAsync(ct)];
        int queued = 0;

        foreach (SnapshotRow mine in await OpApplier.SnapshotAsync(db, ct))
        {
            theirs.TryGetValue((mine.Entity, mine.EntityId), out SnapshotRow? held);

            if (held is { Deleted: true })
                continue;

            if (mine.Deleted)
            {
                Hlc next = Hlc.Next(clock, now.ToUnixTimeMilliseconds(), deviceId);
                clock = next;
                db.Outbox.Add(new OutboxOp
                {
                    OpId = Guid.CreateVersion7(now), Hlc = next.ToString(), Entity = mine.Entity, EntityId = mine.EntityId,
                    Kind = OpKind.Delete, FieldsJson = OpFieldsJson.Write([]), CreatedAt = now,
                });
                queued++;
                continue;
            }

            Dictionary<string, Hlc> heldClocks = held?.Fields.ToDictionary(f => f.Field.Name, f => f.Clock, StringComparer.Ordinal) ?? [];

            IEnumerable<IGrouping<Hlc, (OpField Field, Hlc Clock)>> lost = mine.Fields
                .Where(f => !heldClocks.TryGetValue(f.Field.Name, out Hlc theirClock) || f.Clock > theirClock)
                .GroupBy(f => f.Clock);

            foreach (IGrouping<Hlc, (OpField Field, Hlc Clock)> edit in lost)
            {
                // Still queued from before: it goes back by itself.
                if (alreadyQueued.Contains(edit.Key.ToString()))
                    continue;

                List<OpField> fields = [.. edit.Select(f => f.Field).OrderBy(f => f.Name, StringComparer.Ordinal)];
                db.Outbox.Add(new OutboxOp
                {
                    OpId       = RecoveredOpId(mine.Entity, mine.EntityId, edit.Key, fields),
                    DeviceId   = edit.Key.DeviceId == deviceId ? null : edit.Key.DeviceId,
                    Hlc        = edit.Key.ToString(),
                    Entity     = mine.Entity,
                    EntityId   = mine.EntityId,
                    Kind       = OpKind.Update,
                    FieldsJson = OpFieldsJson.Write(fields),
                    CreatedAt  = now,
                });
                queued++;
            }
        }

        return (queued, clock);
    }

    /// <summary>A version-8 GUID from the edit itself: the same lost edit gets the same op id in every browser.</summary>
    private static Guid RecoveredOpId(string entity, Guid entityId, Hlc clock, IReadOnlyList<OpField> fields)
    {
        string key = $"recovered|{entity}|{entityId:N}|{clock}|{string.Join(',', fields.Select(f => f.Name))}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    public static async Task<SyncState> StateAsync(LocalDbContext db, CancellationToken cancellationToken = default)
    {
        SyncState? state = await db.SyncStates.SingleOrDefaultAsync(cancellationToken);
        if (state is not null)
            return state;

        state = new SyncState();
        db.SyncStates.Add(state);
        return state;
    }
}

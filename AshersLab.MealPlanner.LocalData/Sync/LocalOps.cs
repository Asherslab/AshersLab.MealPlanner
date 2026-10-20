using System.Data;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AshersLab.MealPlanner.LocalData.Sync;

/// <summary>An op as pulled from the server, already off the wire.</summary>
public sealed record PulledOp(Guid OpId, Hlc Clock, string Entity, Guid EntityId, OpKind Kind, IReadOnlyList<OpField> Fields, long Seq);

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
    /// the fields with the entity's rules; this checks again, since a queued invalid op would be rejected.
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
        if (type.OpProblem(kind, fields) is { } problem)
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

    /// <summary>The oldest queued ops, in the order they were made.</summary>
    public static Task<List<OutboxOp>> PendingAsync(LocalDbContext db, int limit, CancellationToken cancellationToken = default) =>
        db.Outbox.AsNoTracking().OrderBy(o => o.Hlc).Take(limit).ToListAsync(cancellationToken);

    /// <summary>Drops ops the server has answered for - accepted, already had, or refused.</summary>
    public static async Task AcknowledgeAsync(LocalDbContext db, IReadOnlyCollection<Guid> opIds, CancellationToken cancellationToken = default)
    {
        if (opIds.Count == 0)
            return;

        await db.Outbox.Where(o => opIds.Contains(o.OpId)).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Applies a page of pulled ops, in seq order, and moves the cursor past them. Returns whether any changed
    /// what this browser shows.
    /// </summary>
    /// <remarks>
    /// This device's own ops come back too: their clocks equal the ones already held, so they change nothing,
    /// and their arrival confirms them - they leave the outbox here as well as on a push's answer, which
    /// covers a push whose answer was lost.
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

            clock                  = Hlc.Observe(clock, op.Clock, deviceId);
            state.PulledThroughSeq = op.Seq;
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
    /// Takes a snapshot of the server's data and moves the cursor to it. Unsynced local edits survive: a snapshot
    /// is merged field by field like ops, and if the snapshot comes from a different server database than the one
    /// this browser last pulled from, everything synced is forgotten first and the outbox is replayed on top.
    /// </summary>
    public static async Task ApplySnapshotAsync(
        LocalDbContext             db,
        Guid                       deviceId,
        Guid                       serverEpoch,
        long                       asOfSeq,
        IReadOnlyList<SnapshotRow> rows,
        DateTimeOffset             now,
        CancellationToken          cancellationToken = default
    )
    {
        await using IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        SyncState state = await StateAsync(db, cancellationToken);
        Hlc?      clock = state.Clock is null ? null : Hlc.Parse(state.Clock);

        if (state.ServerEpoch is { } previous && previous != serverEpoch)
        {
            // The server's database was replaced. Rows synced from the old one are not in the new one and would
            // never be pushed again (their ops left the outbox long ago), so they go; edits still queued are
            // replayed and will be pushed to the new database.
            await OpApplier.ForgetAllAsync(db, cancellationToken);
            state = await StateAsync(db, cancellationToken);

            foreach (OutboxOp queued in await db.Outbox.AsNoTracking().OrderBy(o => o.Hlc).ToListAsync(cancellationToken))
            {
                SyncedEntity? type = SyncedEntities.Find(queued.Entity);
                if (type is not null)
                    await OpApplier.ApplyAsync(db, type, queued.EntityId, queued.Kind, Hlc.Parse(queued.Hlc),
                        OpFieldsJson.Read(queued.FieldsJson), cancellationToken);
            }
        }

        foreach (SnapshotRow row in rows)
        {
            await OpApplier.ApplySnapshotRowAsync(db, row, cancellationToken);
            foreach ((OpField _, Hlc fieldClock) in row.Fields)
                clock = Hlc.Observe(clock, fieldClock, deviceId);
        }

        state.ServerEpoch      = serverEpoch;
        state.PulledThroughSeq = asOfSeq;
        state.Clock            = clock?.ToString();
        state.LastPulledAt     = now;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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

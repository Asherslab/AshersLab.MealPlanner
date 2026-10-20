using AshersLab.MealPlanner.Shared.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Sync;

/// <summary>A row the op log writes. Its id is minted by the device that created it (GUIDv7).</summary>
public interface ISyncedRow
{
    Guid Id { get; set; }

    /// <summary>A tombstone: hidden everywhere, kept so a late op cannot recreate it.</summary>
    bool Deleted { get; set; }
}

/// <summary>
/// One entity type the op log knows: its name on the wire, which ops it accepts, and how fields land on its
/// row. Registered in <see cref="SyncedEntities"/>.
/// </summary>
public abstract class SyncedEntity
{
    /// <summary>The op log's name for this type. On the wire and in both databases - never rename.</summary>
    public abstract string Entity { get; }

    /// <summary>Null when an op of this kind with these fields is acceptable; otherwise why not.</summary>
    public abstract string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields);

    internal abstract ValueTask<ISyncedRow?> FindAsync(DbContext db, Guid id, CancellationToken cancellationToken);

    internal abstract ISyncedRow Add(DbContext db, Guid id);

    internal abstract void SetField(ISyncedRow row, OpField field);

    internal abstract IReadOnlyList<OpField> ReadFields(ISyncedRow row);

    internal abstract Task<List<ISyncedRow>> AllAsync(DbContext db, CancellationToken cancellationToken);

    internal abstract Task RemoveAllAsync(DbContext db, CancellationToken cancellationToken);
}

public abstract class SyncedEntity<TRow> : SyncedEntity
    where TRow : class, ISyncedRow, new()
{
    protected abstract void Set(TRow row, OpField field);

    /// <summary>Every field's current value, for a snapshot.</summary>
    protected abstract IReadOnlyList<OpField> Read(TRow row);

    internal override async ValueTask<ISyncedRow?> FindAsync(DbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.Set<TRow>().FindAsync([id], cancellationToken);

    internal override ISyncedRow Add(DbContext db, Guid id)
    {
        TRow row = new() { Id = id };
        db.Set<TRow>().Add(row);
        return row;
    }

    internal override void SetField(ISyncedRow row, OpField field) => Set((TRow)row, field);

    internal override IReadOnlyList<OpField> ReadFields(ISyncedRow row) => Read((TRow)row);

    internal override async Task<List<ISyncedRow>> AllAsync(DbContext db, CancellationToken cancellationToken) =>
        [.. await db.Set<TRow>().AsNoTracking().ToListAsync(cancellationToken)];

    internal override Task RemoveAllAsync(DbContext db, CancellationToken cancellationToken) =>
        db.Set<TRow>().ExecuteDeleteAsync(cancellationToken);
}

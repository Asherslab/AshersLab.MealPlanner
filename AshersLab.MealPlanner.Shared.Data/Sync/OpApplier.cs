using AshersLab.MealPlanner.Shared.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Sync;

/// <summary>A synced row with the clock behind each field - what a snapshot carries.</summary>
public sealed record SnapshotRow(string Entity, Guid EntityId, bool Deleted, IReadOnlyList<(OpField Field, Hlc Clock)> Fields);

/// <summary>
/// Applies ops to the synced tables of either database: the server when it accepts a push, a browser when it
/// writes its own edit and when it pulls. Same code on both ends, deciding with <see cref="OpMerge"/>.
/// </summary>
/// <remarks>
/// The caller owns the transaction. Each call saves, so the next op in the same transaction sees this one -
/// a create and a rename of the same ingredient in one push is the common case.
/// </remarks>
public static class OpApplier
{
    public static async Task<MergeResult> ApplyAsync(
        DbContext            db,
        SyncedEntity         type,
        Guid                 entityId,
        OpKind               kind,
        Hlc                  clock,
        IReadOnlyList<OpField> fields,
        CancellationToken    cancellationToken = default
    )
    {
        ISyncedRow? row = await type.FindAsync(db, entityId, cancellationToken);

        Dictionary<string, FieldClock> clocks = await db.Set<FieldClock>()
            .Where(c => c.Entity == type.Entity && c.EntityId == entityId)
            .ToDictionaryAsync(c => c.Field, StringComparer.Ordinal, cancellationToken);

        EntityClocks? current = row is null
            ? null
            : new EntityClocks(row.Deleted, clocks.ToDictionary(c => c.Key, c => Hlc.Parse(c.Value.Hlc), StringComparer.Ordinal));

        MergeResult result = OpMerge.Apply(current, kind, clock, fields);
        if (!result.ChangesAnything)
            return result;

        row ??= type.Add(db, entityId);

        if (result.Deletes)
            row.Deleted = true;

        foreach (OpField field in result.FieldsToApply)
        {
            type.SetField(row, field);

            if (clocks.TryGetValue(field.Name, out FieldClock? held))
                held.Hlc = clock.ToString();
            else
                db.Set<FieldClock>().Add(new FieldClock
                {
                    Entity   = type.Entity,
                    EntityId = entityId,
                    Field    = field.Name,
                    Hlc      = clock.ToString(),
                });
        }

        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Merges one snapshot row as if it were its ops: one update per field at that field's clock, or a delete.
    /// So a snapshot never overwrites a newer unsynced local edit.
    /// </summary>
    public static async Task ApplySnapshotRowAsync(DbContext db, SnapshotRow row, CancellationToken cancellationToken = default)
    {
        SyncedEntity type = SyncedEntities.Find(row.Entity)
                         ?? throw new InvalidOperationException($"The snapshot has an unknown entity '{row.Entity}'.");

        // A tombstone's fields first, then the delete: what was deleted is still known by name ("that meal was deleted
        // here") on a browser that only ever saw the snapshot. Once deleted here already, the updates change nothing.
        foreach ((OpField field, Hlc clock) in row.Fields)
            await ApplyAsync(db, type, row.EntityId, OpKind.Update, clock, [field], cancellationToken);

        // Delete wins whatever its clock, so any reading will do.
        if (row.Deleted)
            await ApplyAsync(db, type, row.EntityId, OpKind.Delete, default, [], cancellationToken);
    }

    /// <summary>Every synced row with its field clocks, tombstones included, in one read.</summary>
    public static async Task<List<SnapshotRow>> SnapshotAsync(DbContext db, CancellationToken cancellationToken = default)
    {
        ILookup<(string, Guid), FieldClock> clocks = (await db.Set<FieldClock>().AsNoTracking().ToListAsync(cancellationToken))
            .ToLookup(c => (c.Entity, c.EntityId));

        List<SnapshotRow> rows = [];
        foreach (SyncedEntity type in SyncedEntities.All)
        {
            foreach (ISyncedRow row in await type.AllAsync(db, cancellationToken))
            {
                Dictionary<string, FieldClock> held = clocks[(type.Entity, row.Id)].ToDictionary(c => c.Field, StringComparer.Ordinal);

                // Only fields an op set: a tombstone made by a delete of an unseen id has default values and no
                // clocks, and those defaults must not travel as if someone had chosen them. A tombstone of a row that
                // had fields keeps them, so a browser loading by snapshot knows what was deleted - by name, by date.
                List<(OpField, Hlc)> fields = [.. type.ReadFields(row)
                    .Where(f => held.ContainsKey(f.Name))
                    .Select(f => (f, Hlc.Parse(held[f.Name].Hlc)))];

                rows.Add(new SnapshotRow(type.Entity, row.Id, row.Deleted, fields));
            }
        }

        return rows;
    }

    /// <summary>Empties every synced table and the field clocks. The browser's reset when the server database is replaced.</summary>
    public static async Task ForgetAllAsync(DbContext db, CancellationToken cancellationToken = default)
    {
        foreach (SyncedEntity type in SyncedEntities.All)
            await type.RemoveAllAsync(db, cancellationToken);

        await db.Set<FieldClock>().ExecuteDeleteAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }
}

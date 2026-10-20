namespace AshersLab.MealPlanner.Shared.Domain.Sync;

/// <summary>
/// What an entity currently looks like to the merge: whether it exists, whether it is a tombstone, and the
/// clock of the op that last set each field.
/// </summary>
/// <param name="Deleted">A tombstone. Kept so that a late op cannot bring the entity back.</param>
/// <param name="FieldClocks">Per field, the clock of the op whose value it holds.</param>
public sealed record EntityClocks(bool Deleted, IReadOnlyDictionary<string, Hlc> FieldClocks);

/// <summary>What applying one op does. Nothing to do is a valid answer, and the op is still logged.</summary>
/// <param name="CreatesEntity">There is no row yet: insert one (a tombstone, for a delete).</param>
/// <param name="Deletes">Mark the entity deleted.</param>
/// <param name="FieldsToApply">Set these fields from the op, and record the op's clock against each.</param>
public sealed record MergeResult(bool CreatesEntity, bool Deletes, IReadOnlyList<OpField> FieldsToApply)
{
    public bool ChangesAnything => CreatesEntity || Deletes || FieldsToApply.Count > 0;
}

/// <summary>
/// The merge rules, run identically by the server when it accepts a push and by every browser when it pulls -
/// which is why they converge. Field-level last-writer-wins by <see cref="Hlc"/>; delete beats everything.
/// </summary>
/// <remarks>
/// The rules, and why, are in docs/modules/sync/offline-sync.md. In short:
/// <list type="bullet">
/// <item>Different fields of one entity: both apply - ops carry fields, not rows.</item>
/// <item>The same field: the higher clock wins. An equal clock is the same op again (a retry, or a device
///   pulling its own op back) and changes nothing.</item>
/// <item>Delete vs anything: delete wins, and the tombstone ignores every later op.</item>
/// <item>Create of an id that exists: an update of the fields it carries (only happens on retry).</item>
/// <item>Update or delete of an id never seen: creates it, so the outcome does not depend on arrival
///   order.</item>
/// </list>
/// The result is the same whatever order a set of ops arrives in, which is the property the tests check.
/// </remarks>
public static class OpMerge
{
    private static readonly MergeResult Nothing = new(false, false, []);

    /// <param name="current">The entity's state, or null when there is no row for its id.</param>
    public static MergeResult Apply(EntityClocks? current, OpKind kind, Hlc clock, IReadOnlyList<OpField> fields)
    {
        if (current is { Deleted: true })
            return Nothing;

        bool creates = current is null;

        if (kind == OpKind.Delete)
            return new MergeResult(creates, true, []);

        List<OpField> winners = [];
        foreach (OpField field in fields)
        {
            if (current is not null
             && current.FieldClocks.TryGetValue(field.Name, out Hlc held)
             && held >= clock)
                continue;

            winners.Add(field);
        }

        return creates || winners.Count > 0 ? new MergeResult(creates, false, winners) : Nothing;
    }
}

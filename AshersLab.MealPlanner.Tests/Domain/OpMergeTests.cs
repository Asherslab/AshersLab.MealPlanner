using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

/// <summary>
/// The merge rules' truth table (docs/modules/sync/offline-sync.md), then the property they exist for: the same
/// set of ops gives the same result in any arrival order.
/// </summary>
public class OpMergeTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static Hlc At(long ms, Guid? device = null) => new(ms, 0, device ?? A);

    private static EntityClocks Live(params (string Field, Hlc Clock)[] clocks) =>
        new(false, clocks.ToDictionary(c => c.Field, c => c.Clock));

    [Fact]
    public void Create_of_an_unseen_id_creates_it_with_every_field()
    {
        MergeResult result = OpMerge.Apply(null, OpKind.Create, At(1), [new("Name", "mince")]);

        Assert.True(result.CreatesEntity);
        Assert.False(result.Deletes);
        Assert.Equal(["Name"], result.FieldsToApply.Select(f => f.Name));
    }

    [Fact]
    public void Newer_update_of_the_same_field_wins()
    {
        MergeResult result = OpMerge.Apply(Live(("Name", At(1))), OpKind.Update, At(2), [new("Name", "beef mince")]);

        Assert.False(result.CreatesEntity);
        Assert.Equal("beef mince", Assert.Single(result.FieldsToApply).Value);
    }

    [Fact]
    public void Older_update_of_the_same_field_loses()
    {
        MergeResult result = OpMerge.Apply(Live(("Name", At(5))), OpKind.Update, At(2), [new("Name", "stale")]);

        Assert.False(result.ChangesAnything);
    }

    [Fact]
    public void The_same_op_again_changes_nothing()
    {
        // A retried push, or a device pulling its own op back.
        MergeResult result = OpMerge.Apply(Live(("Name", At(5))), OpKind.Update, At(5), [new("Name", "x")]);

        Assert.False(result.ChangesAnything);
    }

    [Fact]
    public void Updates_to_different_fields_both_apply()
    {
        EntityClocks current = Live(("Name", At(9)));

        MergeResult result = OpMerge.Apply(current, OpKind.Update, At(2), [new("Name", "older"), new("Category", "Meat")]);

        Assert.Equal(["Category"], result.FieldsToApply.Select(f => f.Name));
    }

    [Fact]
    public void Create_of_an_id_that_exists_is_an_update_of_its_fields()
    {
        MergeResult result = OpMerge.Apply(Live(("Name", At(1))), OpKind.Create, At(3), [new("Name", "again")]);

        Assert.False(result.CreatesEntity);
        Assert.Equal("again", Assert.Single(result.FieldsToApply).Value);
    }

    [Fact]
    public void Update_of_an_unseen_id_creates_it()
    {
        MergeResult result = OpMerge.Apply(null, OpKind.Update, At(3), [new("Name", "x")]);

        Assert.True(result.CreatesEntity);
        Assert.Single(result.FieldsToApply);
    }

    [Fact]
    public void Delete_wins_over_a_newer_update()
    {
        MergeResult delete = OpMerge.Apply(Live(("Name", At(9))), OpKind.Delete, At(1), []);
        Assert.True(delete.Deletes);

        MergeResult lateUpdate = OpMerge.Apply(new EntityClocks(true, new Dictionary<string, Hlc>()), OpKind.Update, At(99), [new("Name", "back?")]);
        Assert.False(lateUpdate.ChangesAnything);
    }

    [Fact]
    public void Nothing_resurrects_a_tombstone()
    {
        EntityClocks tombstone = new(true, new Dictionary<string, Hlc>());

        Assert.False(OpMerge.Apply(tombstone, OpKind.Create, At(99), [new("Name", "x")]).ChangesAnything);
        Assert.False(OpMerge.Apply(tombstone, OpKind.Delete, At(99), []).ChangesAnything);
    }

    [Fact]
    public void Delete_of_an_unseen_id_leaves_a_tombstone()
    {
        MergeResult result = OpMerge.Apply(null, OpKind.Delete, At(1), []);

        Assert.True(result.CreatesEntity);
        Assert.True(result.Deletes);
    }

    /// <summary>A stand-in for a database: one entity's fields, clocks and tombstone, applied by the rules.</summary>
    private sealed class Replica
    {
        public bool                       Exists;
        public bool                       Deleted;
        public readonly Dictionary<string, string?> Values = [];
        public readonly Dictionary<string, Hlc>     Clocks = [];

        public void Apply((OpKind Kind, Hlc Clock, OpField[] Fields) op)
        {
            MergeResult r = OpMerge.Apply(Exists ? new EntityClocks(Deleted, Clocks) : null, op.Kind, op.Clock, op.Fields);
            if (r.CreatesEntity) Exists = true;
            if (r.Deletes) Deleted = true;
            foreach (OpField f in r.FieldsToApply)
            {
                Values[f.Name] = f.Value;
                Clocks[f.Name] = op.Clock;
            }
        }

        // A tombstone's leftover values are not compared: nothing reads them, and which ones a replica holds
        // depends on whether the delete arrived before or after the create.
        public string Describe() => Deleted
            ? "deleted"
            : $"{Exists}/" + string.Join(",", Values.OrderBy(v => v.Key).Select(v => $"{v.Key}={v.Value}"));
    }

    public static TheoryData<string> Scenarios => ["concurrent renames", "rename and other field", "delete in the middle", "retries"];

    private static (OpKind, Hlc, OpField[])[] Ops(string scenario) => scenario switch
    {
        "concurrent renames" =>
        [
            (OpKind.Create, At(1), [new("Name", "mince")]),
            (OpKind.Update, At(5, A), [new("Name", "beef mince")]),
            (OpKind.Update, At(5, B), [new("Name", "minced beef")]),
            (OpKind.Update, At(4, B), [new("Name", "mince (Costco)")]),
        ],
        "rename and other field" =>
        [
            (OpKind.Create, At(1), [new("Name", "cheese")]),
            (OpKind.Update, At(3, A), [new("Name", "grated cheese")]),
            (OpKind.Update, At(2, B), [new("Category", "Dairy")]),
        ],
        "delete in the middle" =>
        [
            (OpKind.Create, At(1), [new("Name", "bagels")]),
            (OpKind.Update, At(2), [new("Name", "plain bagels")]),
            (OpKind.Delete, At(3, B), []),
            (OpKind.Update, At(4), [new("Name", "too late")]),
        ],
        "retries" =>
        [
            (OpKind.Create, At(1), [new("Name", "eggs")]),
            (OpKind.Create, At(1), [new("Name", "eggs")]),
            (OpKind.Update, At(2, B), [new("Name", "free-range eggs")]),
            (OpKind.Update, At(2, B), [new("Name", "free-range eggs")]),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Every_arrival_order_converges(string scenario)
    {
        (OpKind, Hlc, OpField[])[] ops = Ops(scenario);
        HashSet<string> outcomes = [];

        foreach (int[] order in Permutations(Enumerable.Range(0, ops.Length).ToArray()))
        {
            Replica replica = new();
            foreach (int i in order)
                replica.Apply(ops[i]);
            outcomes.Add(replica.Describe());
        }

        Assert.Single(outcomes);
    }

    [Fact]
    public void Concurrent_renames_end_on_the_later_clock()
    {
        Replica replica = new();
        foreach ((OpKind, Hlc, OpField[]) op in Ops("concurrent renames"))
            replica.Apply(op);

        // Two renames in the same millisecond: device B's id sorts higher, so B's wins - on every replica.
        Assert.Equal("minced beef", replica.Values["Name"]);
    }

    [Fact]
    public void Rename_and_category_both_survive()
    {
        Replica replica = new();
        foreach ((OpKind, Hlc, OpField[]) op in Ops("rename and other field").Reverse())
            replica.Apply(op);

        Assert.Equal("grated cheese", replica.Values["Name"]);
        Assert.Equal("Dairy", replica.Values["Category"]);
    }

    private static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (int i = 0; i < items.Length; i++)
        {
            int[] rest = [.. items.Take(i), .. items.Skip(i + 1)];
            foreach (int[] tail in Permutations(rest))
                yield return [items[i], .. tail];
        }
    }
}

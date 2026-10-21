using AshersLab.MealPlanner.Shared.Domain;
using AshersLab.MealPlanner.Shared.Domain.Categories;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

public class ListPositionTests
{
    /// <summary>Applies the changes and returns the item labels in their new order (ties by label, as the page sorts by name).</summary>
    private static string Order(string labels, decimal[] positions, IReadOnlyList<(int Index, decimal Position)> changes)
    {
        decimal[] after = [.. positions];
        foreach ((int index, decimal position) in changes)
            after[index] = position;
        return string.Concat(labels.Select((l, i) => (l, p: after[i])).OrderBy(x => x.p).ThenBy(x => x.l).Select(x => x.l));
    }

    [Theory]
    [InlineData(1, -1, "BACD")]   // B up past A, to the top
    [InlineData(2, -1, "ACBD")]   // C up between A and B
    [InlineData(0, +1, "BACD")]   // A down between B and C
    [InlineData(2, +1, "ABDC")]   // C down past D, to the bottom
    public void A_move_writes_one_position_between_the_new_neighbours(int index, int direction, string expected)
    {
        decimal[] positions = [1, 2, 3, 4];

        IReadOnlyList<(int Index, decimal Position)> changes = ListPosition.Move(positions, index, direction);

        Assert.Equal(index, Assert.Single(changes).Index);
        Assert.Equal(expected, Order("ABCD", positions, changes));
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(3, +1)]
    public void Nothing_moves_past_the_end(int index, int direction)
    {
        Assert.Empty(ListPosition.Move([1, 2, 3, 4], index, direction));
    }

    [Fact]
    public void Moving_between_two_equal_positions_renumbers_the_list()
    {
        // Two devices each added a category at the end offline: B and C share position 2.
        decimal[] positions = [1, 2, 2, 3];

        IReadOnlyList<(int Index, decimal Position)> changes = ListPosition.Move(positions, 3, -1);   // D up, between B and C

        // Numbered 1..4 in the new order; only what changes is written - here C, from 2 to 4 (D is already 3).
        Assert.Equal([(2, 4m)], changes);
        Assert.Equal("ABDC", Order("ABCD", positions, changes));

        // Two equal positions with nothing to number around: every item moves.
        decimal[] crowded = [5, 5, 5];
        IReadOnlyList<(int Index, decimal Position)> renumbered = ListPosition.Move(crowded, 2, -1);
        Assert.Equal(3, renumbered.Count);
        Assert.Equal("ACB", Order("ABC", crowded, renumbered));
        Assert.DoesNotContain(changes, c => positions[c.Index] == c.Position);
    }

    [Fact]
    public void Repeated_moves_into_the_same_gap_renumber_rather_than_collide()
    {
        // Keep moving the last item up into the gap just below the first: the gap halves every time.
        decimal[] positions = [1, 2, 3];
        string labels = "ABC";
        for (int i = 0; i < 200; i++)
        {
            IReadOnlyList<(int Index, decimal Position)> changes = ListPosition.Move(positions, 2, -1);
            Assert.NotEmpty(changes);
            string order = Order(labels, positions, changes);
            foreach ((int index, decimal position) in changes)
                positions[index] = position;

            // Re-index to the new display order, as the page reloads it.
            (char l, decimal p)[] sorted = [.. labels.Select((l, k) => (l, positions[k])).OrderBy(x => x.Item2).ThenBy(x => x.l)];
            Assert.Equal(order, string.Concat(sorted.Select(x => x.l)));
            labels    = order;
            positions = [.. sorted.Select(x => x.p)];
            Assert.Equal(positions.Length, positions.Distinct().Count());
        }
    }

    [Fact]
    public void A_new_item_goes_after_the_last()
    {
        Assert.Equal(1, ListPosition.AtEnd([]));
        Assert.Equal(10, ListPosition.AtEnd([1, 9, 3]));
        Assert.Equal(3, ListPosition.AtEnd([1, 2.5m]));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("1.5", true)]
    [InlineData("-2", true)]
    [InlineData("1.0", true)]
    [InlineData("1,5", false)]
    [InlineData(" 1", false)]
    [InlineData("1e3", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Reads_only_its_own_format(string? value, bool ok)
    {
        Assert.Equal(ok, ListPosition.TryParse(value, out _));
    }

    [Fact]
    public void A_category_op_needs_a_name_and_a_position_to_create()
    {
        Assert.Null(CategoryRules.OpProblem(OpKind.Create, [new("Name", "Bakery"), new("Position", "10")]));
        Assert.Null(CategoryRules.OpProblem(OpKind.Update, [new("Position", "2.5")]));
        Assert.Null(CategoryRules.OpProblem(OpKind.Update, [new("Name", "Deli")]));

        Assert.NotNull(CategoryRules.OpProblem(OpKind.Create, [new("Name", "Bakery")]));
        Assert.NotNull(CategoryRules.OpProblem(OpKind.Update, [new("Position", "first")]));
        Assert.NotNull(CategoryRules.OpProblem(OpKind.Update, [new("Position", null)]));
        Assert.NotNull(CategoryRules.OpProblem(OpKind.Update, [new("Store", "x")]));
    }
}

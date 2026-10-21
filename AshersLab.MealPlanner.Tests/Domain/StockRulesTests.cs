using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Pantry;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

/// <summary>Stock movements and a staple's stock as the op log sees them: what is accepted, and each field's one spelling.</summary>
public class StockRulesTests
{
    private static readonly Guid Mince = new("01900000-0000-7000-8000-0000000000a1");
    private static readonly DateOnly Mon = new(2026, 10, 26);

    private static List<OpField> Fields(decimal quantity, StockReason reason) => [.. new StockEntry(Mince, quantity, Mon, reason).Fields()];

    [Fact]
    public void A_movement_carries_its_ingredient_amount_day_and_reason()
    {
        Assert.Null(StockRules.OpProblem(OpKind.Create, Fields(2000, StockReason.Bought)));
        Assert.Equal(new[] { "IngredientId", "Quantity", "OccurredOn", "Reason" }, Fields(2000, StockReason.Bought).Select(f => f.Name));
        Assert.Equal(new[] { OpValues.FormatId(Mince), "1.5", "2026-10-26", "Counted" }, Fields(1.50m, StockReason.Counted).Select(f => f.Value));

        foreach (string field in StockRules.Fields.Where(f => f != StockRules.ShoppingItemField))
            Assert.NotNull(StockRules.OpProblem(OpKind.Create, [.. Fields(1, StockReason.Bought).Where(f => f.Name != field)]));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, [.. Fields(1, StockReason.Bought), new OpField("Note", "x")]));
        Assert.Null(StockRules.OpProblem(OpKind.Delete, []));
    }

    [Fact]
    public void A_movement_a_shopping_list_tick_wrote_names_the_tick()
    {
        Guid tick = new("01900000-0000-7000-8000-0000000000b1");
        List<OpField> fields = [.. new StockEntry(Mince, 2000, Mon, StockReason.Bought, tick).Fields()];
        Assert.Equal(OpValues.FormatId(tick), fields.Single(f => f.Name == "ShoppingItemId").Value);
        Assert.Null(StockRules.OpProblem(OpKind.Create, fields));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, [.. Fields(1, StockReason.Bought), new OpField(StockRules.ShoppingItemField, "mince")]));
    }

    [Fact]
    public void The_amount_fits_the_reason()
    {
        // Bought: more than nothing. Counted: nothing or more. Adjusted: anything but nothing.
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, Fields(0, StockReason.Bought)));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, Fields(-5, StockReason.Bought)));
        Assert.Null(StockRules.OpProblem(OpKind.Create, Fields(0, StockReason.Counted)));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, Fields(-1, StockReason.Counted)));
        Assert.Null(StockRules.OpProblem(OpKind.Create, Fields(-250, StockReason.Adjusted)));
        Assert.Null(StockRules.OpProblem(OpKind.Create, Fields(250, StockReason.Adjusted)));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, Fields(0, StockReason.Adjusted)));

        // Limits every quantity shares.
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, Fields(Quantity.Max + 1, StockReason.Bought)));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, Fields(-Quantity.Max - 1, StockReason.Adjusted)));
        Assert.Null(StockRules.OpProblem(OpKind.Create, Fields(0.125m, StockReason.Bought)));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, Fields(0.1255m, StockReason.Bought)));
    }

    [Fact]
    public void Each_field_has_one_spelling()
    {
        List<OpField> good = Fields(500, StockReason.Bought);
        List<OpField> With(string name, string? value) => [.. good.Select(f => f.Name == name ? f with { Value = value } : f)];

        Assert.NotNull(StockRules.OpProblem(OpKind.Create, With("Quantity", "500.0")));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, With("Quantity", "1e3")));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, With("Quantity", null)));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, With("Reason", "bought")));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, With("Reason", "0")));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, With("Reason", "Cooked")));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, With("OccurredOn", "26/10/2026")));
        Assert.NotNull(StockRules.OpProblem(OpKind.Create, With("IngredientId", null)));

        // An update - a given-back field after a restore - may carry one field alone.
        Assert.Null(StockRules.OpProblem(OpKind.Update, [new OpField("Quantity", "-3")]));
        Assert.True(StockReasons.TryParse("Adjusted", out StockReason reason) && reason == StockReason.Adjusted);
    }

    [Fact]
    public void A_staples_stock_is_one_of_three_by_name_and_two_of_them_go_on_the_list()
    {
        Assert.Null(IngredientRules.OpProblem(OpKind.Update, [new OpField("StapleStock", "RunningLow")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new OpField("StapleStock", "running low")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new OpField("StapleStock", null)]));
        Assert.Equal(["Have", "RunningLow", "Out"], Enum.GetValues<StapleStock>().Select(StapleStocks.Format));
        Assert.Equal(["Have", "Running low", "Out"], Enum.GetValues<StapleStock>().Select(s => s.Label()));
        Assert.Equal([false, true, true], Enum.GetValues<StapleStock>().Select(s => s.NeedsBuying()));
    }
}

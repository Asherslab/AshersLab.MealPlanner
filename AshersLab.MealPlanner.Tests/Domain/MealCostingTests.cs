using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Prices;

namespace AshersLab.MealPlanner.Tests.Domain;

public class MealCostingTests
{
    // $24.99 for a 2 kg pack of mince, $1.20 an onion, $3.00 for 500 g of pasta.
    private static readonly UnitPrice Mince = new(2499, 2000);
    private static readonly UnitPrice Onion = new(120, 1);
    private static readonly UnitPrice Pasta = new(300, 500);

    private static CostLine Line(decimal? quantity, UnitPrice? price) => new(quantity, price);

    /// <summary>A cost with no lines - compared by value, not by the lines list's reference.</summary>
    private static void Equal(Cost? batch, Cost? serve, MealCost actual)
    {
        Assert.Equal((batch, serve), (actual.Batch, actual.PerServe));
        Assert.Empty(actual.Lines);
    }

    [Fact]
    public void A_line_is_its_quantity_times_the_unit_price_rounded_once_half_away_from_zero()
    {
        Assert.Equal(new LineCost(LineState.Costed, 625), MealCosting.Line(Line(500, Mince)));      // 624.75
        Assert.Equal(new LineCost(LineState.Costed, 937), MealCosting.Line(Line(750, Mince)));      // 937.125
        Assert.Equal(new LineCost(LineState.Costed, 938), MealCosting.Line(Line(750.4m, Mince)));   // 937.62
        Assert.Equal(new LineCost(LineState.Costed, 1), MealCosting.Line(Line(1, new UnitPrice(1, 2))));   // 0.5 rounds up
        Assert.Equal(new LineCost(LineState.Costed, 0), MealCosting.Line(Line(1, new UnitPrice(1, 3))));   // 0.33 rounds down
        Assert.Equal(new LineCost(LineState.Costed, 240), MealCosting.Line(Line(2, Onion)));
    }

    [Fact]
    public void A_line_with_no_quantity_or_no_price_is_not_costed_and_says_which()
    {
        Assert.Equal(new LineCost(LineState.NoQuantity, null), MealCosting.Line(Line(null, Mince)));
        Assert.Equal(new LineCost(LineState.Unpriced, null), MealCosting.Line(Line(500, null)));
        // Both missing: the quantity is what the planner can fix on this page, so that is what it says.
        Assert.Equal(new LineCost(LineState.NoQuantity, null), MealCosting.Line(Line(null, null)));
    }

    [Fact]
    public void A_cooked_batch_is_the_sum_of_its_lines_and_a_serve_its_share()
    {
        MealCost cost = MealCosting.Cost(MealKind.Cooked, 4, null, [Line(500, Mince), Line(1, Onion), Line(250, Pasta)]);

        Assert.Equal(new Cost(625 + 120 + 150, 0), cost.Batch);
        Assert.Equal(new Cost(224, 0), cost.PerServe);    // 895 / 4 = 223.75
        Assert.Equal([625L, 120L, 150L], cost.Lines.Select(l => l.Cents!.Value));
        Assert.Equal("$8.95", cost.Batch!.Value.Format());
        Assert.Equal("$2.24 a serve", cost.PerServe!.Value.Format(" a serve"));
    }

    [Fact]
    public void The_lines_shown_add_up_to_the_batch_exactly()
    {
        // Two lines of half a cent each: rounded one by one they show 1 c each, so the batch is 2 c, not the exact 1 c.
        // The planner sees the lines and the batch on one page; they must agree.
        UnitPrice halfACent = new(1, 2);
        MealCost cost = MealCosting.Cost(MealKind.Cooked, 1, null, [Line(1, halfACent), Line(1, halfACent)]);

        Assert.Equal(cost.Lines.Sum(l => l.Cents), cost.Batch!.Value.Cents);
        Assert.Equal(2, cost.Batch.Value.Cents);
    }

    [Theory]
    [InlineData(10, 4, 3)]       // 2.5 rounds away from zero
    [InlineData(1000, 3, 333)]   // 333.33
    [InlineData(2000, 3, 667)]   // 666.67
    [InlineData(895, 1, 895)]
    [InlineData(1, 99, 0)]
    public void A_serve_is_the_batch_divided_once_and_rounded_half_away_from_zero(long batch, int serves, long serve) =>
        Assert.Equal(new Cost(serve, 0), MealCosting.PerServe(new Cost(batch, 0), serves));

    [Fact]
    public void Any_line_missing_a_quantity_or_a_price_makes_the_batch_and_the_serve_incomplete_never_a_lower_number()
    {
        MealCost cost = MealCosting.Cost(MealKind.Cooked, 4, null, [Line(500, Mince), Line(null, Onion), Line(250, null)]);

        Assert.Equal(new Cost(625, 2), cost.Batch);
        Assert.Equal(new Cost(156, 2), cost.PerServe);    // 625 / 4 = 156.25
        Assert.False(cost.Batch!.Value.Complete);
        Assert.Equal([LineState.Costed, LineState.NoQuantity, LineState.Unpriced], cost.Lines.Select(l => l.State));
        Assert.Equal("from $6.25 + 2 unpriced", cost.Batch.Value.Format());
        Assert.Equal("from $1.56 a serve + 2 unpriced", cost.PerServe!.Value.Format(" a serve"));
    }

    [Fact]
    public void Nothing_priced_at_all_is_from_nothing_plus_every_line_never_zero()
    {
        MealCost cost = MealCosting.Cost(MealKind.Cooked, 4, null, [Line(null, null), Line(500, null), Line(null, Mince)]);

        Assert.Equal(new Cost(0, 3), cost.Batch);
        Assert.Equal("3 unpriced", cost.Batch!.Value.Format());
        Assert.NotEqual("$0.00", cost.PerServe!.Value.Format());
    }

    [Fact]
    public void A_meal_made_from_ingredients_with_none_yet_has_no_cost_not_zero()
    {
        Equal(null, null, MealCosting.Cost(MealKind.Cooked, 4, null, []));
        Equal(null, null, MealCosting.Cost(MealKind.Simple, 1, null, []));
    }

    [Fact]
    public void A_single_serve_meal_is_one_serve_whatever_its_serves_field_holds()
    {
        // Serves left over from when it was a batch are kept in the row, and ignored.
        MealCost cost = MealCosting.Cost(MealKind.Simple, 4, null, [Line(2, Onion), Line(100, Pasta)]);

        Assert.Equal(new Cost(240 + 60, 0), cost.Batch);
        Assert.Equal(cost.Batch, cost.PerServe);
    }

    [Fact]
    public void A_fixed_cost_meal_is_its_typed_amount_shared_over_the_serves_it_feeds_and_ignores_ingredients()
    {
        MealCost pizza = MealCosting.Cost(MealKind.FixedCost, 3, 4500, [Line(500, Mince)]);

        Equal(new Cost(4500, 0), new Cost(1500, 0), pizza);
        Assert.Equal(new Cost(1125, 0), MealCosting.Cost(MealKind.FixedCost, 4, 4500, []).PerServe);
        Assert.Equal(new Cost(1667, 0), MealCosting.Cost(MealKind.FixedCost, 3, 5000, []).PerServe);   // 1666.67
    }

    [Fact]
    public void A_fixed_cost_meal_with_no_amount_is_incomplete()
    {
        Equal(new Cost(0, 1), new Cost(0, 1), MealCosting.Cost(MealKind.FixedCost, 3, null, []));
    }

    [Fact]
    public void A_no_cost_meal_costs_nothing_and_is_complete_whatever_ingredients_it_kept()
    {
        MealCost cost = MealCosting.Cost(MealKind.NoCost, 1, 4500, [Line(500, Mince), Line(null, null)]);

        Equal(new Cost(0, 0), new Cost(0, 0), cost);
        Assert.Equal("$0.00", cost.PerServe!.Value.Format());
    }

    [Fact]
    public void Leftovers_cost_nothing_of_their_own()
    {
        // The batch they eat from was bought and costed when it was cooked (slice 10 links them).
        Equal(null, null, MealCosting.Cost(MealKind.Leftovers, 1, 4500, [Line(500, Mince)]));
    }

    [Fact]
    public void Variable_weight_meat_priced_by_the_kilo_costs_by_the_gram_rounding_half_away_from_zero()
    {
        // Rump steak sold as "about 1 kg" trays: this one weighed 1.234 kg and cost $18.50 at $14.99/kg. The per-kg
        // price is what a gram costs - the tray's price is for that tray, not the 1 kg pack size - so 1.5 kg is
        // 2248.5 c, not 2775 c by the pack.
        PriceReading tray = new(Guid.Empty, 1850, new DateOnly(2026, 10, 14), PriceSource.Manual, PerKgCents: 1499);
        UnitPrice perGram = IngredientPricing.PerUnit(BaseUnit.Gram, 1000, tray);

        MealCost cost = MealCosting.Cost(MealKind.Cooked, 2, null, [Line(1500, perGram)]);

        // Both halves land on an even floor, so banker's rounding would say 2248 and 1124.
        Assert.Equal(new Cost(2249, 0), cost.Batch);      // 2248.5
        Assert.Equal(new Cost(1125, 0), cost.PerServe);   // 2249 / 2 = 1124.5
    }

    [Fact]
    public void The_largest_quantities_and_prices_cost_to_the_exact_cent()
    {
        // At the limits the pages allow: $99,999.99 for the smallest pack (0.001 g), a tonne less a milligram of it.
        // Absurd, but it must neither throw nor drift: 9,999,999 x 999,999,999 c is odd and past 2^53, where a double
        // no longer counts every cent.
        UnitPrice dearest = new(Money.MaxCents - 1, 0.001m);
        MealCost cost = MealCosting.Cost(MealKind.Cooked, 1, null, [Line(Quantity.Max - 0.001m, dearest)]);

        Assert.Equal(9_999_998_990_000_001L, cost.Batch!.Value.Cents);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_batch_of_no_serves_is_a_bug_not_a_cost(int serves) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MealCosting.Cost(MealKind.Cooked, serves, null, [Line(500, Mince)]));

    [Theory]
    [InlineData(0, 0, "", "$0.00")]
    [InlineData(1234, 0, " a serve", "$12.34 a serve")]
    [InlineData(1234, 1, "", "from $12.34 + 1 unpriced")]
    [InlineData(5, 3, " a serve", "from $0.05 a serve + 3 unpriced")]
    // Nothing known: "from $0.00" reads as a price. Seen on a phone, 2026-10-22, on a new meal before any prices.
    [InlineData(0, 4, "", "4 unpriced")]
    [InlineData(0, 1, " a serve", "1 unpriced")]
    public void A_cost_reads_as_money_or_as_from_and_how_many_are_missing(long cents, int missing, string per, string text) =>
        Assert.Equal(text, new Cost(cents, missing).Format(per));
}

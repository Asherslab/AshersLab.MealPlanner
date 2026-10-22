using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Pantry;
using AshersLab.MealPlanner.Shared.Domain.Shopping;

namespace AshersLab.MealPlanner.Tests.Domain;

/// <summary>
/// The shopping list's arithmetic: the shortfall after the pantry, whole packs and their spare, which pack, and what the
/// shop and the food eaten cost - incomplete, never low, when something is unpriced.
/// </summary>
public class ShoppingPlanTests
{
    private static readonly Guid Costco     = new("01900000-0000-7000-8000-0000000000c1");
    private static readonly Guid Woolworths = new("01900000-0000-7000-8000-0000000000c2");

    private static readonly Guid MinceTwoKilos  = new("01900000-0000-7000-8000-0000000000d1");
    private static readonly Guid MinceHalfKilo  = new("01900000-0000-7000-8000-0000000000d2");
    private static readonly Guid MinceWoolies   = new("01900000-0000-7000-8000-0000000000d3");

    private static StockLevel InStock(decimal quantity) => new(quantity, StockState.InStock);

    private static PackOption Pack(Guid product, Guid store, decimal size, long? cents) => new(product, store, size, cents);

    [Fact]
    public void Need_750g_of_mince_in_2kg_packs_is_one_pack_with_1250g_spare()
    {
        LinePlan line = ShoppingPlan.Line(750, StockLevel.NotTracked, Costco, [Pack(MinceTwoKilos, Costco, 2000, 2499)]);

        Assert.Equal(LineNeed.Buy, line.Need);
        Assert.Equal(750, line.Shortfall);
        Assert.Equal(new PackChoice(MinceTwoKilos, Costco, 2000, 1, 1250, 2499), line.Pack);
        Assert.Equal(2000, line.Pack!.Quantity);
        Assert.Equal(new Cost(2499, 0), ShoppingPlan.CostOf(line));
    }

    [Fact]
    public void The_pantry_is_subtracted_first_and_only_the_shortfall_is_rounded_up()
    {
        // "Need 300 g mayo, have about 250 g" -> 50 g short -> one 400 g jar, 350 g spare.
        LinePlan mayo = ShoppingPlan.Line(300, InStock(250), Woolworths, [Pack(MinceWoolies, Woolworths, 400, 450)]);
        Assert.Equal((LineNeed.Buy, 300m, 250m, 50m), (mayo.Need, mayo.Needed, mayo.Have, mayo.Shortfall));
        Assert.Equal((1, 350m), (mayo.Pack!.Packs, mayo.Pack.Spare));

        // Exactly a multiple: no spare, no extra pack.
        LinePlan exact = ShoppingPlan.Line(1500, InStock(500), Costco, [Pack(MinceHalfKilo, Costco, 500, 700)]);
        Assert.Equal((2, 0m, 1400L), (exact.Pack!.Packs, exact.Pack.Spare, exact.Pack.Cents));

        // A gram over is a whole pack more.
        LinePlan over = ShoppingPlan.Line(1001, StockLevel.NotTracked, Costco, [Pack(MinceHalfKilo, Costco, 500, 700)]);
        Assert.Equal((3, 499m), (over.Pack!.Packs, over.Pack.Spare));
    }

    [Fact]
    public void Covered_entirely_by_stock_is_in_the_pantry_and_costs_nothing()
    {
        LinePlan covered = ShoppingPlan.Line(500, InStock(500), Costco, [Pack(MinceTwoKilos, Costco, 2000, 2499)]);
        Assert.Equal(LineNeed.InPantry, covered.Need);
        Assert.Equal(0, covered.Shortfall);
        Assert.Null(covered.Pack);
        Assert.Equal(new Cost(0, 0), ShoppingPlan.CostOf(covered));

        Assert.Equal(LineNeed.InPantry, ShoppingPlan.Line(500, InStock(2000), Costco, []).Need);
    }

    [Fact]
    public void Out_probably_out_and_never_counted_hold_nothing()
    {
        foreach (StockLevel nothing in new[] { new StockLevel(0, StockState.Out), new StockLevel(0, StockState.ProbablyOut), StockLevel.NotTracked })
        {
            LinePlan line = ShoppingPlan.Line(750, nothing, Costco, [Pack(MinceTwoKilos, Costco, 2000, 2499)]);
            Assert.Equal((LineNeed.Buy, 0m, 750m), (line.Need, line.Have, line.Shortfall));
        }
    }

    [Fact]
    public void The_usual_store_wins_then_the_cheapest_for_the_packs_needed_not_the_cheapest_per_gram()
    {
        PackOption big   = Pack(MinceTwoKilos, Costco, 2000, 2000);  // $10/kg
        PackOption small = Pack(MinceHalfKilo, Costco, 500, 700);    // $14/kg
        PackOption other = Pack(MinceWoolies, Woolworths, 500, 500); // cheaper, but not where it is bought

        // 300 g: one small pack ($7) beats one big ($20), though the big one is cheaper per gram.
        Assert.Equal(MinceHalfKilo, ShoppingPlan.Choose(300, Costco, [big, small, other])!.ProductId);
        // 1800 g: one big ($20) beats four small ($28).
        Assert.Equal(MinceTwoKilos, ShoppingPlan.Choose(1800, Costco, [big, small, other])!.ProductId);
        // No product at the usual store: anywhere.
        Assert.Equal(MinceWoolies, ShoppingPlan.Choose(300, Guid.NewGuid(), [other, Pack(MinceHalfKilo, Costco, 500, 700)])!.ProductId);
        Assert.Equal(MinceWoolies, ShoppingPlan.Choose(300, null, [other, Pack(MinceHalfKilo, Costco, 500, 700)])!.ProductId);
    }

    [Fact]
    public void A_priced_pack_beats_an_unpriced_one_then_the_least_spare_then_the_id()
    {
        PackOption unpriced = Pack(MinceHalfKilo, Costco, 500, null);
        PackOption priced   = Pack(MinceTwoKilos, Costco, 2000, 2499);
        Assert.Equal(MinceTwoKilos, ShoppingPlan.Choose(300, Costco, [unpriced, priced])!.ProductId);

        // Neither priced: the least spare.
        Assert.Equal(MinceHalfKilo, ShoppingPlan.Choose(300, Costco, [Pack(MinceTwoKilos, Costco, 2000, null), unpriced])!.ProductId);
        // The same cost and spare: the lower id (…d1), in any input order.
        PackOption a = Pack(MinceHalfKilo, Costco, 500, 700), b = Pack(MinceTwoKilos, Costco, 500, 700);
        Assert.Equal(MinceTwoKilos, ShoppingPlan.Choose(300, Costco, [a, b])!.ProductId);
        Assert.Equal(MinceTwoKilos, ShoppingPlan.Choose(300, Costco, [b, a])!.ProductId);
    }

    [Fact]
    public void No_pack_size_buys_the_amount_and_its_cost_is_unknown_never_zero()
    {
        LinePlan line = ShoppingPlan.Line(750, StockLevel.NotTracked, Costco, [Pack(MinceTwoKilos, Costco, 0, 2499)]);
        Assert.Equal(LineNeed.Buy, line.Need);
        Assert.Null(line.Pack);
        Assert.Equal(new Cost(0, 1), ShoppingPlan.CostOf(line));

        LinePlan unpriced = ShoppingPlan.Line(750, StockLevel.NotTracked, Costco, [Pack(MinceTwoKilos, Costco, 2000, null)]);
        Assert.Equal(1, unpriced.Pack!.Packs);
        Assert.Equal(new Cost(0, 1), ShoppingPlan.CostOf(unpriced));
    }

    [Fact]
    public void A_pack_size_that_would_need_more_than_999_packs_is_not_offered()
    {
        // An 8 g pack typed for 8 kg: 10 kg would be 1250 packs.
        Assert.Null(ShoppingPlan.Choose(10_000, Costco, [Pack(MinceHalfKilo, Costco, 8, 100)]));
        Assert.Equal(MinceTwoKilos, ShoppingPlan.Choose(10_000, Costco, [Pack(MinceHalfKilo, Costco, 8, 100), Pack(MinceTwoKilos, Costco, 2000, 2499)])!.ProductId);
        Assert.Equal(999, ShoppingPlan.Choose(999 * 8, Costco, [Pack(MinceHalfKilo, Costco, 8, 100)])!.Packs);
    }

    [Fact]
    public void A_staple_running_low_is_one_pack_of_the_cheapest()
    {
        LinePlan soy = ShoppingPlan.Staple(Woolworths, [Pack(MinceTwoKilos, Woolworths, 1000, 900), Pack(MinceHalfKilo, Woolworths, 250, 400)]);
        Assert.Equal(LineNeed.Staple, soy.Need);
        Assert.Equal(new PackChoice(MinceHalfKilo, Woolworths, 250, 1, 0, 400), soy.Pack);
        Assert.Equal(new Cost(400, 0), ShoppingPlan.CostOf(soy));

        Assert.Equal(new Cost(0, 1), ShoppingPlan.CostOf(ShoppingPlan.Staple(Woolworths, [])));
    }

    [Fact]
    public void Quantities_stay_exact_decimals()
    {
        // 0.3 L of milk short, in 0.1 L "packs": exactly three, nothing spare - no binary rounding.
        PackChoice choice = ShoppingPlan.Choose(0.3m, Costco, [Pack(MinceHalfKilo, Costco, 0.1m, 10)])!;
        Assert.Equal((3, 0m, 30L), (choice.Packs, choice.Spare, choice.Cents));

        LinePlan line = ShoppingPlan.Line(1.25m, InStock(0.5m), Costco, [Pack(MinceHalfKilo, Costco, 0.5m, 100)]);
        Assert.Equal((0.75m, 2, 0.25m), (line.Shortfall, line.Pack!.Packs, line.Pack.Spare));
    }

    [Fact]
    public void Nothing_to_buy_has_no_packs_and_meals_never_use_less_than_nothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShoppingPlan.Choose(0, Costco, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShoppingPlan.Line(-1, StockLevel.NotTracked, Costco, []));
        Assert.Equal(LineNeed.InPantry, ShoppingPlan.Line(0, StockLevel.NotTracked, Costco, []).Need);
    }

    [Fact]
    public void Serves_are_multiplied_and_an_incomplete_or_unknown_serve_is_one_missing()
    {
        Assert.Equal(new Cost(960, 0), ShoppingPlan.Serves(3, new Cost(320, 0)));
        // "From $3.20 a serve + 2 unpriced", eaten three times: one entry that could not be fully costed.
        Assert.Equal(new Cost(960, 1), ShoppingPlan.Serves(3, new Cost(320, 2)));
        // No ingredients yet: nothing to cost it by - missing, not $0.
        Assert.Equal(new Cost(0, 1), ShoppingPlan.Serves(2, null));
        // Leftovers with nothing to eat: nothing eaten, nothing missing.
        Assert.Equal(new Cost(0, 0), ShoppingPlan.Serves(0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShoppingPlan.Serves(-1, new Cost(1, 0)));
    }

    [Fact]
    public void A_part_serve_costs_its_part_rounded_to_the_cent_once()
    {
        // Two adults and a toddler on half a serve: 2½ serves at $3.20.
        Assert.Equal(new Cost(800, 0), ShoppingPlan.Serves(2.5m, new Cost(320, 0)));
        // A quarter of $3.33 is 83.25c: 83c, not 84 or 83.25.
        Assert.Equal(new Cost(83, 0), ShoppingPlan.Serves(0.25m, new Cost(333, 0)));
        Assert.Equal(new Cost(167, 0), ShoppingPlan.Serves(0.5m, new Cost(333, 0)));   // 166.5c, rounded away from zero
        Assert.Equal(new Cost(160, 1), ShoppingPlan.Serves(0.5m, new Cost(320, 3)));   // still incomplete
    }

    [Fact]
    public void Costs_add_their_cents_and_their_missing()
    {
        Assert.Equal(new Cost(3699, 2), ShoppingPlan.Sum([new Cost(2499, 0), ShoppingPlan.Priced(1200), ShoppingPlan.Priced(null), new Cost(0, 1)]));
        Assert.Equal(new Cost(0, 0), ShoppingPlan.Sum([]));
        Assert.Equal("from $36.99 + 2 unpriced", ShoppingPlan.Sum([new Cost(2499, 0), new Cost(1200, 2)]).Format());
    }
}

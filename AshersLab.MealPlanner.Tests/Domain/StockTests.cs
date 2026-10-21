using AshersLab.MealPlanner.Shared.Domain.Pantry;

namespace AshersLab.MealPlanner.Tests.Domain;

/// <summary>
/// Stock on hand: movements and the calendar's meals folded in order, never below zero, a count setting the level, and
/// the hand changes of a day before its meals - all counted, nothing stored.
/// </summary>
public class StockTests
{
    /// <summary>Monday 26 October 2026.</summary>
    private static readonly DateOnly Mon = new(2026, 10, 26);

    private static readonly Guid Mince = new("01900000-0000-7000-8000-0000000000a1");
    private static readonly Guid Mayo  = new("01900000-0000-7000-8000-0000000000a2");

    private static int _next;

    /// <summary>Ids in the order they are made, like GUIDv7s minted one after another.</summary>
    private static Guid Id() => new($"01900000-0000-7000-8000-{Interlocked.Increment(ref _next):D12}");

    private static StockEvent Bought(DateOnly day, decimal quantity, Guid? ingredient = null) => new(ingredient ?? Mince, day, StockChange.Bought, quantity, Id());

    private static StockEvent Counted(DateOnly day, decimal quantity, Guid? ingredient = null) => new(ingredient ?? Mince, day, StockChange.Counted, quantity, Id());

    private static StockEvent Adjusted(DateOnly day, decimal quantity) => new(Mince, day, StockChange.Adjusted, quantity, Id());

    private static StockEvent Used(DateOnly day, decimal quantity, Guid? ingredient = null) => new(ingredient ?? Mince, day, StockChange.Used, quantity, Id());

    [Fact]
    public void A_purchase_is_drawn_down_by_the_meals_after_it_and_a_day_is_read_before_its_meals()
    {
        StockLedger ledger = Stock.Run([Bought(Mon, 2000), Used(Mon, 500), Used(Mon.AddDays(2), 750)]);

        // Monday's shopping list sees the 2 kg; Monday's dinner then takes 500 g.
        Assert.Equal(new StockLevel(2000, StockState.InStock), ledger.At(Mince, Mon));
        Assert.Equal(new StockLevel(1500, StockState.InStock), ledger.After(Mince, Mon));
        // A day with nothing of its own is the end of the last day that had something.
        Assert.Equal(1500, ledger.At(Mince, Mon.AddDays(1)).Quantity);
        Assert.Equal(1500, ledger.At(Mince, Mon.AddDays(2)).Quantity);
        Assert.Equal(750, ledger.After(Mince, Mon.AddDays(2)).Quantity);
        Assert.Equal(750, ledger.At(Mince, Mon.AddDays(30)).Quantity);
    }

    [Fact]
    public void Stock_never_goes_below_zero_and_the_next_purchase_starts_from_nothing()
    {
        StockLedger ledger = Stock.Run([Bought(Mon, 500), Used(Mon.AddDays(1), 800), Bought(Mon.AddDays(3), 2000), Used(Mon.AddDays(4), 500)]);

        StockDay tuesday = ledger.DaysOf(Mince).Single(d => d.Day == Mon.AddDays(1));
        // "Probably out", not -300 g: the estimate drifted.
        Assert.Equal(new StockLevel(0, StockState.ProbablyOut), tuesday.End);
        Assert.Equal(300, tuesday.Short);
        Assert.Equal(new StockLevel(0, StockState.ProbablyOut), ledger.At(Mince, Mon.AddDays(2)));
        // The drift never eats what was bought after it: 2 kg, not 1.7 kg.
        Assert.Equal(new StockLevel(2000, StockState.InStock), ledger.At(Mince, Mon.AddDays(3)));
        Assert.Equal(1500, ledger.After(Mince, Mon.AddDays(4)).Quantity);
    }

    [Fact]
    public void Using_exactly_what_is_left_is_probably_out_and_short_by_nothing()
    {
        StockLedger ledger = Stock.Run([Bought(Mon, 500), Used(Mon, 500)]);

        StockDay day = ledger.DaysOf(Mince).Single();
        Assert.Equal((new StockLevel(0, StockState.ProbablyOut), 0m), (day.End, day.Short));
    }

    [Fact]
    public void A_count_sets_the_level_whatever_the_estimate_said()
    {
        // The estimate says 1.5 kg; the planner looks: about half a pack, 1 kg. Then "we're out".
        StockLedger ledger = Stock.Run([Bought(Mon, 2000), Used(Mon, 500), Counted(Mon.AddDays(1), 1000), Counted(Mon.AddDays(2), 0)]);

        Assert.Equal(new StockLevel(1000, StockState.InStock), ledger.At(Mince, Mon.AddDays(1)));
        Assert.Equal(new StockLevel(0, StockState.Out), ledger.At(Mince, Mon.AddDays(2)));

        // And a count after a drift resets it: no shortfall carried.
        StockLedger drifted = Stock.Run([Bought(Mon, 100), Used(Mon, 900), Counted(Mon.AddDays(1), 400)]);
        Assert.Equal(new StockLevel(400, StockState.InStock), drifted.At(Mince, Mon.AddDays(1)));
    }

    [Fact]
    public void An_adjustment_adds_or_takes_off_and_taking_off_more_than_there_is_is_out()
    {
        StockLedger ledger = Stock.Run([Bought(Mon, 1000), Adjusted(Mon.AddDays(1), -300), Adjusted(Mon.AddDays(2), 50), Adjusted(Mon.AddDays(3), -5000)]);

        Assert.Equal(700, ledger.At(Mince, Mon.AddDays(1)).Quantity);
        Assert.Equal(750, ledger.At(Mince, Mon.AddDays(2)).Quantity);
        // "Threw it out", more than the estimate thought there was: out, by the planner's word.
        Assert.Equal(new StockLevel(0, StockState.Out), ledger.At(Mince, Mon.AddDays(3)));
    }

    [Fact]
    public void On_one_day_the_hand_changes_come_before_the_meals_in_the_order_they_were_made()
    {
        // Counted then bought: 0 + 2000. Bought then counted: the count wins.
        StockEvent count = Counted(Mon, 0);
        StockEvent buy   = Bought(Mon, 2000);
        StockEvent meal  = Used(Mon, 500);
        Assert.Equal(1500, Stock.Run([meal, buy, count]).After(Mince, Mon).Quantity);
        Assert.Equal(2000, Stock.Run([meal, buy, count]).At(Mince, Mon).Quantity);

        StockEvent buyFirst   = Bought(Mon.AddDays(1), 2000);
        StockEvent countAfter = Counted(Mon.AddDays(1), 300);
        Assert.Equal(300, Stock.Run([countAfter, buyFirst]).At(Mince, Mon.AddDays(1)).Quantity);

        // A count made after the evening's dinner still has that dinner taken off: low, never high.
        StockLedger evening = Stock.Run([Bought(Mon.AddDays(2), 1000), Used(Mon.AddDays(3), 400), Counted(Mon.AddDays(3), 600)]);
        Assert.Equal(200, evening.After(Mince, Mon.AddDays(3)).Quantity);
    }

    [Fact]
    public void Two_counts_on_one_day_from_two_phones_end_on_the_later_one_in_any_order()
    {
        StockEvent first  = Counted(Mon, 250);
        StockEvent second = Counted(Mon, 400);
        Assert.Equal(400, Stock.Run([first, second]).At(Mince, Mon).Quantity);
        Assert.Equal(400, Stock.Run([second, first]).At(Mince, Mon).Quantity);

        // Two purchases from two phones: both count.
        Assert.Equal(1000, Stock.Run([Bought(Mon, 500), Bought(Mon, 500)]).At(Mince, Mon).Quantity);
    }

    [Fact]
    public void Meals_before_the_first_movement_draw_nothing_and_the_ingredient_is_not_tracked_till_then()
    {
        StockLedger ledger = Stock.Run([Used(Mon, 500), Used(Mon.AddDays(1), 500), Bought(Mon.AddDays(2), 1000), Used(Mon.AddDays(3), 300)]);

        Assert.Equal(StockLevel.NotTracked, ledger.At(Mince, Mon));
        Assert.Equal(StockLevel.NotTracked, ledger.At(Mince, Mon.AddDays(1)));
        Assert.Equal(1000, ledger.At(Mince, Mon.AddDays(2)).Quantity);
        Assert.Equal(700, ledger.After(Mince, Mon.AddDays(3)).Quantity);

        // Only ever used: not tracked at all.
        Assert.Equal(StockLevel.NotTracked, Stock.Run([Used(Mon, 5)]).At(Mince, Mon));
        Assert.Empty(Stock.Run([Used(Mon, 5)]).Tracked);
        Assert.Equal(StockLevel.NotTracked, Stock.Run([]).At(Mayo, Mon));
    }

    [Fact]
    public void Out_by_the_planners_word_stays_out_when_a_meal_wants_it()
    {
        StockLedger ledger = Stock.Run([Counted(Mon, 0), Used(Mon.AddDays(1), 200)]);

        StockDay tuesday = ledger.DaysOf(Mince).Single(d => d.Day == Mon.AddDays(1));
        Assert.Equal((new StockLevel(0, StockState.Out), 200m), (tuesday.End, tuesday.Short));
    }

    [Fact]
    public void A_future_day_is_a_projection_through_the_plan_and_runs_out_names_the_meal_that_takes_the_last()
    {
        StockLedger ledger = Stock.Run([Bought(Mon, 1000), Used(Mon.AddDays(1), 400), Used(Mon.AddDays(3), 400), Used(Mon.AddDays(5), 400)]);

        Assert.Equal(200, ledger.At(Mince, Mon.AddDays(4)).Quantity);
        Assert.Equal(Mon.AddDays(5), ledger.RunsOut(Mince, Mon, Mon.AddDays(6))!.Day);
        Assert.Null(ledger.RunsOut(Mince, Mon, Mon.AddDays(4)));
        // Already out before the range: it does not run out in it.
        Assert.Null(ledger.RunsOut(Mince, Mon.AddDays(6), Mon.AddDays(20)));
        Assert.Null(Stock.Run([Bought(Mon, 1000), Used(Mon.AddDays(1), 400)]).RunsOut(Mince, Mon, Mon.AddDays(6)));
    }

    [Fact]
    public void Ingredients_are_counted_apart_and_the_input_order_never_matters()
    {
        StockEvent[] events =
        [
            Bought(Mon, 2000), Used(Mon, 500), Counted(Mon.AddDays(1), 1200), Used(Mon.AddDays(2), 1500), Bought(Mon.AddDays(2), 500),
            Bought(Mon, 500, Mayo), Used(Mon.AddDays(1), 30, Mayo), Used(Mon.AddDays(1), 20, Mayo),
        ];
        StockLedger one = Stock.Run(events);
        StockLedger two = Stock.Run(events.Reverse());
        StockLedger three = Stock.Run(events.OrderBy(e => e.Quantity));

        foreach (StockLedger ledger in new[] { one, two, three })
        {
            Assert.Equal(200, ledger.After(Mince, Mon.AddDays(2)).Quantity);
            Assert.Equal(450, ledger.After(Mayo, Mon.AddDays(1)).Quantity);
            Assert.Equal(one.DaysOf(Mince), ledger.DaysOf(Mince));
        }
        Assert.Equal([Mince, Mayo], one.Tracked.Order());
    }

    [Fact]
    public void Quantities_stay_exact_decimals()
    {
        StockLedger ledger = Stock.Run([Bought(Mon, 0.3m), Used(Mon, 0.1m), Used(Mon, 0.1m)]);

        Assert.Equal(0.1m, ledger.After(Mince, Mon).Quantity);
    }
}

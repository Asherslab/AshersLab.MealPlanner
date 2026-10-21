using AshersLab.MealPlanner.Shared.Domain.Planning;

namespace AshersLab.MealPlanner.Tests.Domain;

/// <summary>
/// Batches and serves: what a cooked entry makes, what leftovers eat from it in calendar order, and what a skip does - all
/// counted from the entries, nothing stored.
/// </summary>
public class ServesTests
{
    /// <summary>Monday 26 October 2026.</summary>
    private static readonly DateOnly Mon = new(2026, 10, 26);

    private const decimal Lunch  = 3;
    private const decimal Dinner = 5;

    private static int _next;

    private static Guid Id() => new($"00000000-0000-7000-8000-{Interlocked.Increment(ref _next):D12}");

    private static ServesEntry Cook(DateOnly day, int made, int eaters = 2, bool skipped = false, decimal slot = Dinner) =>
        new(Id(), day, slot, ServesRole.Cooked, skipped, eaters, made);

    private static ServesEntry Eat(DateOnly day, int eaters = 2, Guid? from = null, bool skipped = false, decimal slot = Lunch) =>
        new(Id(), day, slot, ServesRole.Leftovers, skipped, eaters, EatsFrom: from);

    [Fact]
    public void A_batch_feeds_its_own_meal_first_then_each_leftovers_entry_in_order()
    {
        ServesEntry cook = Cook(Mon, made: 6, eaters: 2);
        ServesEntry tue  = Eat(Mon.AddDays(1));
        ServesEntry wed  = Eat(Mon.AddDays(2));
        ServesLedger ledger = Serves.Count([wed, cook, tue]);

        BatchServes batch = ledger.Batches[cook.Id];
        Assert.Equal([4, 2, 0], batch.Draws.Select(d => d.LeftAfter));
        Assert.Equal([cook.Id, tue.Id, wed.Id], batch.Draws.Select(d => d.EntryId));
        Assert.Equal(0, batch.Left);
        Assert.Equal((2, LeftoversProblem.None, 2), (ledger.Leftovers[tue.Id].Got, ledger.Leftovers[tue.Id].Problem, ledger.Leftovers[tue.Id].LeftAfter));
        Assert.Equal(cook.Id, ledger.Leftovers[wed.Id].BatchId);
    }

    [Fact]
    public void Each_person_eats_a_serve_so_three_at_a_table_take_three()
    {
        ServesEntry cook  = Cook(Mon, made: 8, eaters: 3);
        ServesEntry lunch = Eat(Mon.AddDays(1), eaters: 3);
        ServesEntry solo  = Eat(Mon.AddDays(1), eaters: 1, slot: Dinner);
        ServesLedger ledger = Serves.Count([cook, lunch, solo]);

        Assert.Equal([5, 2, 1], ledger.Batches[cook.Id].Draws.Select(d => d.LeftAfter));
        // "1 serve will go uneaten"
        Assert.Equal(1, ledger.Batches[cook.Id].Left);
        Assert.Equal(3, Serves.EatersOf([Id(), Id(), Id()], household: 2));
        Assert.Equal(2, Serves.EatersOf(null, household: 2));
        Assert.Equal(1, Serves.EatersOf(null, household: 0));
        Assert.Equal(2, Serves.EatersOf([], household: 2));
    }

    [Fact]
    public void Leftovers_with_nothing_left_say_so_and_a_part_says_how_much()
    {
        ServesEntry cook = Cook(Mon, made: 4, eaters: 2);
        ServesEntry tue  = Eat(Mon.AddDays(1), eaters: 1);
        ServesEntry wed  = Eat(Mon.AddDays(2), eaters: 2);
        ServesEntry sat  = Eat(Mon.AddDays(5), eaters: 2);
        ServesLedger ledger = Serves.Count([cook, tue, wed, sat]);

        Assert.Equal((1, LeftoversProblem.NotEnough), (ledger.Leftovers[wed.Id].Got, ledger.Leftovers[wed.Id].Problem));
        // "Saturday's leftovers has nothing to eat"
        Assert.Equal((0, LeftoversProblem.NothingLeft, (Guid?)null), (ledger.Leftovers[sat.Id].Got, ledger.Leftovers[sat.Id].Problem, ledger.Leftovers[sat.Id].BatchId));
        Assert.Equal(0, ledger.Batches[cook.Id].Left);
    }

    [Fact]
    public void A_skipped_batch_made_nothing_and_flags_the_leftovers_that_ate_it()
    {
        ServesEntry cook   = Cook(Mon, made: 6, skipped: true);
        ServesEntry chosen = Eat(Mon.AddDays(1), from: cook.Id);
        ServesEntry auto   = Eat(Mon.AddDays(2));
        ServesLedger ledger = Serves.Count([cook, chosen, auto]);

        BatchServes batch = ledger.Batches[cook.Id];
        Assert.False(batch.Cooked);
        Assert.Equal((0, 0), (batch.Made, batch.Left));
        Assert.Empty(batch.Draws);
        // Never left silently pointing at serves that do not exist.
        Assert.Equal((cook.Id, 0, LeftoversProblem.NotCooked), (ledger.Leftovers[chosen.Id].BatchId, ledger.Leftovers[chosen.Id].Got, ledger.Leftovers[chosen.Id].Problem));
        Assert.Equal((cook.Id, LeftoversProblem.NotCooked), (ledger.Leftovers[auto.Id].BatchId, ledger.Leftovers[auto.Id].Problem));
    }

    [Fact]
    public void Leftovers_eating_whatever_is_left_move_to_another_batch_when_one_is_skipped()
    {
        // Oldest first would eat Sunday's; it was skipped, so Monday's.
        ServesEntry sunday = Cook(Mon.AddDays(-1), made: 6, skipped: true);
        ServesEntry monday = Cook(Mon, made: 4);
        ServesEntry tue    = Eat(Mon.AddDays(1));
        ServesLedger ledger = Serves.Count([sunday, monday, tue]);

        Assert.Equal((monday.Id, LeftoversProblem.None), (ledger.Leftovers[tue.Id].BatchId, ledger.Leftovers[tue.Id].Problem));
        Assert.Equal(0, ledger.Batches[monday.Id].Left);
    }

    [Fact]
    public void A_skipped_leftovers_entry_returns_its_serve_to_the_next_one()
    {
        ServesEntry cook = Cook(Mon, made: 4);
        ServesEntry tue  = Eat(Mon.AddDays(1), skipped: true);
        ServesEntry wed  = Eat(Mon.AddDays(2));
        ServesLedger ledger = Serves.Count([cook, tue, wed]);

        Assert.False(ledger.Leftovers.ContainsKey(tue.Id));
        Assert.Equal(LeftoversProblem.None, ledger.Leftovers[wed.Id].Problem);
        Assert.Equal(0, ledger.Batches[cook.Id].Left);

        // Not skipped, it would have taken them and Wednesday would have nothing.
        ServesLedger eaten = Serves.Count([cook, tue with { Skipped = false }, wed]);
        Assert.Equal(LeftoversProblem.NothingLeft, eaten.Leftovers[wed.Id].Problem);
    }

    [Fact]
    public void Calendar_order_is_the_day_then_the_slot_so_lunch_does_not_eat_that_nights_dinner()
    {
        ServesEntry dinner = Cook(Mon, made: 4, slot: Dinner);
        ServesEntry lunch  = Eat(Mon, slot: Lunch);
        ServesEntry late   = Eat(Mon, eaters: 1, slot: Dinner + 1);
        ServesLedger ledger = Serves.Count([late, dinner, lunch]);

        Assert.Equal(LeftoversProblem.NothingLeft, ledger.Leftovers[lunch.Id].Problem);
        Assert.Equal((LeftoversProblem.None, 1), (ledger.Leftovers[late.Id].Problem, ledger.Leftovers[late.Id].LeftAfter));
    }

    [Fact]
    public void Moving_entries_changes_who_eats_first()
    {
        // Planned: Tuesday and Thursday lunch on a 4-serve batch eaten by two the night it is cooked.
        ServesEntry cook = Cook(Mon, made: 4);
        ServesEntry tue  = Eat(Mon.AddDays(1));
        ServesEntry thu  = Eat(Mon.AddDays(3));
        Assert.Equal(LeftoversProblem.NothingLeft, Serves.Count([cook, tue, thu]).Leftovers[thu.Id].Problem);

        // Tuesday's moved to Friday: Thursday eats now, Friday has nothing.
        ServesLedger moved = Serves.Count([cook, tue with { Day = Mon.AddDays(4) }, thu]);
        Assert.Equal(LeftoversProblem.None, moved.Leftovers[thu.Id].Problem);
        Assert.Equal(LeftoversProblem.NothingLeft, moved.Leftovers[tue.Id].Problem);

        // The cooking replanned onto Wednesday: Tuesday's lunch comes before it.
        ServesLedger later = Serves.Count([cook with { Day = Mon.AddDays(2) }, tue, thu]);
        Assert.Equal(LeftoversProblem.NothingLeft, later.Leftovers[tue.Id].Problem);
        Assert.Equal(LeftoversProblem.None, later.Leftovers[thu.Id].Problem);
    }

    [Fact]
    public void Two_entries_in_one_slot_on_one_day_eat_in_id_order_whatever_order_they_arrive()
    {
        // Two phones each added Tuesday's lunch: same day, same slot. The lower id eats first, on every device.
        ServesEntry cook = Cook(Mon, made: 4);
        ServesEntry low  = Eat(Mon.AddDays(1));
        ServesEntry high = Eat(Mon.AddDays(1));
        Assert.True(low.Id.CompareTo(high.Id) < 0);

        foreach (ServesEntry[] order in new[] { new[] { cook, low, high }, [high, cook, low], [cook, high, low] })
        {
            ServesLedger ledger = Serves.Count(order);
            Assert.Equal(LeftoversProblem.None, ledger.Leftovers[low.Id].Problem);
            Assert.Equal(LeftoversProblem.NothingLeft, ledger.Leftovers[high.Id].Problem);
        }
    }

    [Fact]
    public void Whatever_is_left_reaches_back_exactly_seven_days()
    {
        ServesEntry week  = Cook(Mon.AddDays(-7), made: 4);
        ServesEntry eaten = Eat(Mon);
        Assert.Equal(week.Id, Serves.Count([week, eaten]).Leftovers[eaten.Id].BatchId);

        ServesEntry eight = Eat(Mon.AddDays(1));
        Assert.Equal(LeftoversProblem.NothingLeft, Serves.Count([week, eight]).Leftovers[eight.Id].Problem);
    }

    [Fact]
    public void A_batch_cooked_on_Sunday_is_eaten_on_the_next_days()
    {
        DateOnly sunday = Mon.AddDays(6);
        ServesEntry cook = Cook(sunday, made: 6);
        ServesEntry mon  = Eat(sunday.AddDays(1));
        ServesEntry tue  = Eat(sunday.AddDays(2));
        ServesLedger ledger = Serves.Count([cook, mon, tue]);

        Assert.Equal([4, 2, 0], ledger.Batches[cook.Id].Draws.Select(d => d.LeftAfter));
        Assert.All(new[] { mon, tue }, e => Assert.Equal(cook.Id, ledger.Leftovers[e.Id].BatchId));
    }

    [Fact]
    public void Whatever_is_left_is_the_oldest_batch_that_has_enough_and_none_older_than_a_week()
    {
        ServesEntry old    = Cook(Mon.AddDays(-8), made: 10);
        ServesEntry sunday = Cook(Mon.AddDays(-1), made: 3);   // 1 left after its own dinner
        ServesEntry monday = Cook(Mon, made: 6);               // 4 left
        ServesEntry tue    = Eat(Mon.AddDays(1));               // needs 2: Sunday's has 1, so Monday's
        ServesEntry wed    = Eat(Mon.AddDays(2), eaters: 1);    // Sunday's last serve
        ServesLedger ledger = Serves.Count([old, sunday, monday, tue, wed]);

        Assert.Equal(monday.Id, ledger.Leftovers[tue.Id].BatchId);
        Assert.Equal(sunday.Id, ledger.Leftovers[wed.Id].BatchId);
        // Eight days old: not eaten by "whatever is left", so its 8 will go uneaten.
        Assert.Equal(8, ledger.Batches[old.Id].Left);
    }

    [Fact]
    public void Only_some_left_anywhere_takes_the_oldest_with_any()
    {
        ServesEntry cook = Cook(Mon, made: 3);
        ServesEntry tue  = Eat(Mon.AddDays(1));
        ServesLedger ledger = Serves.Count([cook, tue]);

        Assert.Equal((cook.Id, 1, LeftoversProblem.NotEnough, 0), (ledger.Leftovers[tue.Id].BatchId, ledger.Leftovers[tue.Id].Got, ledger.Leftovers[tue.Id].Problem, ledger.Leftovers[tue.Id].LeftAfter));
    }

    [Fact]
    public void A_chosen_batch_is_eaten_even_when_an_older_one_has_serves()
    {
        ServesEntry sunday = Cook(Mon.AddDays(-1), made: 6);
        ServesEntry monday = Cook(Mon, made: 6);
        ServesEntry tue    = Eat(Mon.AddDays(1), from: monday.Id);
        ServesEntry wed    = Eat(Mon.AddDays(2));
        ServesLedger ledger = Serves.Count([sunday, monday, tue, wed]);

        Assert.Equal(monday.Id, ledger.Leftovers[tue.Id].BatchId);
        Assert.Equal(sunday.Id, ledger.Leftovers[wed.Id].BatchId);
        Assert.Equal((2, 2), (ledger.Batches[sunday.Id].Left, ledger.Batches[monday.Id].Left));
    }

    [Fact]
    public void A_chosen_batch_that_is_gone_or_cooked_later_is_flagged_and_eats_nothing()
    {
        ServesEntry thursday = Cook(Mon.AddDays(3), made: 6);
        ServesEntry tue      = Eat(Mon.AddDays(1), from: thursday.Id);
        ServesEntry wed      = Eat(Mon.AddDays(2), from: Id());
        ServesLedger ledger  = Serves.Count([thursday, tue, wed]);

        Assert.Equal((0, LeftoversProblem.CookedLater), (ledger.Leftovers[tue.Id].Got, ledger.Leftovers[tue.Id].Problem));
        Assert.Equal((0, LeftoversProblem.BatchGone), (ledger.Leftovers[wed.Id].Got, ledger.Leftovers[wed.Id].Problem));
        Assert.Equal(4, ledger.Batches[thursday.Id].Left);
    }

    [Fact]
    public void Fewer_made_than_eat_it_the_night_it_is_cooked_is_short_and_leaves_nothing()
    {
        ServesEntry cook = Cook(Mon, made: 2, eaters: 3);
        ServesLedger ledger = Serves.Count([cook]);

        Assert.Equal((0, 1), (ledger.Batches[cook.Id].Left, ledger.Batches[cook.Id].Short));
        Assert.Equal(2, ledger.Batches[cook.Id].Draws.Single().Serves);
    }

    [Fact]
    public void Two_leftovers_entries_added_on_two_phones_are_two_draws_in_any_order()
    {
        // Each phone added a lunch offline; once both are synced the calendar holds both, whichever arrived first.
        ServesEntry cook = Cook(Mon, made: 6);
        ServesEntry a    = Eat(Mon.AddDays(1));
        ServesEntry b    = Eat(Mon.AddDays(1), slot: Lunch + 1);
        ServesLedger one = Serves.Count([cook, a, b]);
        ServesLedger two = Serves.Count([b, a, cook]);

        Assert.Equal(0, one.Batches[cook.Id].Left);
        Assert.Equal(one.Batches[cook.Id].Draws, two.Batches[cook.Id].Draws);
        Assert.Equal(one.Leftovers.OrderBy(l => l.Key).Select(l => l.Value), two.Leftovers.OrderBy(l => l.Key).Select(l => l.Value));
    }

    [Fact]
    public void Entries_that_neither_cook_nor_eat_leftovers_change_nothing()
    {
        ServesEntry cook  = Cook(Mon, made: 4);
        ServesEntry other = new(Id(), Mon.AddDays(1), Lunch, ServesRole.Other, false, 2);
        ServesLedger ledger = Serves.Count([cook, other]);

        Assert.Single(ledger.Batches);
        Assert.Empty(ledger.Leftovers);
        Assert.Equal(2, ledger.Batches[cook.Id].Left);
    }
}

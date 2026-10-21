using AshersLab.MealPlanner.Shared.Domain.Planning;

namespace AshersLab.MealPlanner.Tests.Domain;

/// <summary>A plan's rotation laid onto dates: Mondays, weeks wrapping both ways, generating entries and applying them again.</summary>
public class RotationTests
{
    private static readonly Guid Breakfast = new("01900000-0000-7000-8000-0000000000b1");
    private static readonly Guid Dinner    = new("01900000-0000-7000-8000-0000000000d1");
    private static readonly Guid E1        = new("01900000-0000-7000-8000-000000000e01");
    private static readonly Guid E2        = new("01900000-0000-7000-8000-000000000e02");
    private static readonly Guid E3        = new("01900000-0000-7000-8000-000000000e03");

    /// <summary>Week 1 of the rotation began on Monday 5 October 2026.</summary>
    private static readonly DateOnly Start = new(2026, 10, 5);

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    [InlineData(2026, 10, 26, 2026, 10, 26)]   // a Monday is its own
    [InlineData(2026, 10, 27, 2026, 10, 26)]
    [InlineData(2026, 11, 1, 2026, 10, 26)]    // Sunday belongs to the week before it, not after
    [InlineData(2027, 1, 1, 2026, 12, 28)]     // across a year
    [InlineData(2028, 3, 1, 2028, 2, 28)]      // across a leap day
    public void The_week_of_a_date_begins_on_its_Monday(int y, int m, int d, int my, int mm, int md) =>
        Assert.Equal(D(my, mm, md), Rotation.MondayOf(D(y, m, d)));

    [Theory]
    [InlineData(2026, 10, 5, 1)]
    [InlineData(2026, 10, 11, 1)]   // Sunday of week 1
    [InlineData(2026, 10, 12, 2)]
    [InlineData(2026, 11, 1, 4)]
    [InlineData(2026, 11, 2, 1)]    // wraps after four weeks
    [InlineData(2026, 12, 28, 1)]   // twelve weeks on: week 1 again
    [InlineData(2027, 1, 4, 2)]     // across a year
    [InlineData(2026, 10, 4, 4)]    // the day before the start is the last week, not week 1
    [InlineData(2026, 9, 28, 4)]
    [InlineData(2026, 9, 27, 3)]
    [InlineData(2026, 9, 7, 1)]     // a whole rotation earlier
    [InlineData(2025, 10, 6, 1)]    // 52 weeks earlier: 52 is a multiple of 4
    public void A_date_falls_in_one_week_of_a_four_week_rotation(int y, int m, int d, int week) =>
        Assert.Equal(week, Rotation.WeekOf(Start, 4, D(y, m, d)));

    [Fact]
    public void Every_date_of_a_one_week_plan_is_week_one()
    {
        for (DateOnly date = D(2026, 1, 1); date < D(2027, 1, 1); date = date.AddDays(1))
            Assert.Equal(1, Rotation.WeekOf(Start, 1, date));
    }

    [Fact]
    public void Each_week_is_seven_days_through_daylight_saving_changes()
    {
        // Sydney's clocks change on 4 October 2026 and 4 April 2027; London's on 25 October 2026 and 28 March 2027. Dates
        // have no hours, so each week is still exactly seven days and every week number lasts Monday to Sunday.
        int previous = Rotation.WeekOf(Start, 3, D(2026, 9, 28));
        int run = 0;
        for (DateOnly date = D(2026, 9, 28); date <= D(2027, 4, 18); date = date.AddDays(1))
        {
            int week = Rotation.WeekOf(Start, 3, date);
            if (week != previous)
            {
                Assert.Equal(DayOfWeek.Monday, date.DayOfWeek);
                Assert.Equal(previous % 3 + 1, week);
                run = 0;
            }
            run++;
            Assert.InRange(run, 1, 7);
            previous = week;
        }
    }

    [Fact]
    public void Saying_which_week_this_week_is_gives_the_start()
    {
        DateOnly starts = Rotation.StartFor(D(2026, 10, 26), 3, 4);
        Assert.Equal(D(2026, 10, 12), starts);
        Assert.Equal(3, Rotation.WeekOf(starts, 4, D(2026, 10, 26)));
        Assert.Equal(3, Rotation.WeekOf(starts, 4, D(2026, 11, 1)));
        Assert.Equal(1, Rotation.WeekOf(Rotation.StartFor(D(2026, 10, 26), 1, 4), 4, D(2026, 10, 26)));

        Assert.Throws<ArgumentException>(() => Rotation.StartFor(D(2026, 10, 27), 1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rotation.StartFor(D(2026, 10, 26), 5, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rotation.StartFor(D(2026, 10, 26), 0, 4));
        Assert.Throws<ArgumentException>(() => Rotation.WeekOf(D(2026, 10, 6), 4, D(2026, 10, 6)));
    }

    [Fact]
    public void Generating_puts_each_entry_on_its_week_and_day_and_wraps()
    {
        RotationEntry[] entries =
        [
            new(E1, 1, DayOfWeek.Monday, Dinner),
            new(E2, 2, DayOfWeek.Sunday, Breakfast),
            new(E3, 3, DayOfWeek.Monday, Dinner),   // beyond a two-week plan: kept, put nowhere
        ];

        IReadOnlyList<GeneratedEntry> made = Rotation.Generate(Start, 2, entries, D(2026, 10, 5), D(2026, 11, 1));

        Assert.Equal(
        [
            (E1, D(2026, 10, 5), Dinner), (E2, D(2026, 10, 18), Breakfast),
            (E1, D(2026, 10, 19), Dinner), (E2, D(2026, 11, 1), Breakfast),
        ], made.Select(g => (g.TemplateEntryId, g.Date, g.SlotId)));
        Assert.All(made, g => Assert.Equal(Rotation.EntryId(g.TemplateEntryId, g.Date), g.Id));
        Assert.False(Rotation.Puts(Start, 2, entries[2], D(2026, 10, 19)));
        Assert.True(Rotation.Puts(Start, 2, entries[0], D(2026, 10, 19)));
        Assert.False(Rotation.Puts(Start, 2, entries[0], D(2026, 10, 12)));
    }

    [Fact]
    public void Generating_from_mid_week_and_before_the_start_follows_the_same_rotation()
    {
        RotationEntry[] entries = [new(E1, 1, DayOfWeek.Wednesday, Dinner), new(E2, 4, DayOfWeek.Wednesday, Dinner)];

        // Wednesday 30 September to Wednesday 14 October: the week before the start is week 4.
        IReadOnlyList<GeneratedEntry> made = Rotation.Generate(Start, 4, entries, D(2026, 9, 30), D(2026, 10, 14));

        Assert.Equal([(E2, D(2026, 9, 30)), (E1, D(2026, 10, 7))], made.Select(g => (g.TemplateEntryId, g.Date)));
        Assert.Empty(Rotation.Generate(Start, 4, entries, D(2026, 10, 8), D(2026, 10, 7)));
    }

    [Fact]
    public void Every_day_gets_one_entry_from_a_daily_plan_across_a_daylight_saving_change()
    {
        RotationEntry[] daily = [.. PlanDays.Week.Select(day => new RotationEntry(Guid.CreateVersion7(), 1, day, Dinner))];

        IReadOnlyList<GeneratedEntry> made = Rotation.Generate(D(2027, 3, 29), 1, daily, D(2027, 3, 29), D(2027, 4, 11));

        Assert.Equal(Enumerable.Range(0, 14).Select(i => D(2027, 3, 29).AddDays(i)), made.Select(g => g.Date));
        Assert.Equal(14, made.Select(g => g.Id).Distinct().Count());
    }

    [Fact]
    public void Too_many_days_at_once_is_refused()
    {
        RotationEntry[] entries = [new(E1, 1, DayOfWeek.Monday, Dinner)];
        Assert.Equal(12, Rotation.Generate(Start, 1, entries, Start, Start.AddDays(Rotation.MaxDays - 1)).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => Rotation.Generate(Start, 1, entries, Start, Start.AddDays(Rotation.MaxDays)));
    }

    [Fact]
    public void An_entry_on_a_date_has_one_id_everywhere()
    {
        Guid id = Rotation.EntryId(E1, D(2026, 10, 5));
        Assert.Equal(id, Rotation.EntryId(E1, D(2026, 10, 5)));
        Assert.NotEqual(id, Rotation.EntryId(E1, D(2026, 10, 6)));
        Assert.NotEqual(id, Rotation.EntryId(E2, D(2026, 10, 5)));
        Assert.Equal(8, id.Version);
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void Applying_again_creates_only_what_the_calendar_never_held()
    {
        RotationEntry[] entries = [new(E1, 1, DayOfWeek.Monday, Dinner), new(E2, 1, DayOfWeek.Tuesday, Dinner)];
        IReadOnlyList<GeneratedEntry> twoWeeks = Rotation.Generate(Start, 1, entries, D(2026, 10, 5), D(2026, 10, 18));

        ApplyResult first = Rotation.Apply(twoWeeks, new HashSet<Guid>());
        Assert.Equal(4, first.Create.Count);
        Assert.Equal(0, first.AlreadyThere);

        // Everything held - skipped, moved, given another meal or removed (a tombstone) alike - is left as it is.
        ApplyResult again = Rotation.Apply(twoWeeks, first.Create.Select(g => g.Id).ToHashSet());
        Assert.Empty(again.Create);
        Assert.Equal(4, again.AlreadyThere);

        // A plan entry added since: only its entries are new.
        RotationEntry[] more = [.. entries, new(E3, 1, DayOfWeek.Wednesday, Breakfast)];
        ApplyResult added = Rotation.Apply(Rotation.Generate(Start, 1, more, D(2026, 10, 5), D(2026, 10, 18)), first.Create.Select(g => g.Id).ToHashSet());
        Assert.Equal([D(2026, 10, 7), D(2026, 10, 14)], added.Create.Select(g => g.Date));
        Assert.Equal(4, added.AlreadyThere);
    }
}

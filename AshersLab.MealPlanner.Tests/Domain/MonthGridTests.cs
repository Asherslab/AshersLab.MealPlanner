using AshersLab.MealPlanner.Shared.Domain.Planning;

namespace AshersLab.MealPlanner.Tests.Domain;

/// <summary>The month at a glance: the dates its grid shows, each day's slots marked, and what is left to plan.</summary>
public class MonthGridTests
{
    private static readonly Guid Breakfast = new("01900000-0000-7000-8000-0000000000b1");
    private static readonly Guid Snack     = new("01900000-0000-7000-8000-0000000000c1");
    private static readonly Guid Dinner    = new("01900000-0000-7000-8000-0000000000d1");
    private static readonly Guid Removed   = new("01900000-0000-7000-8000-0000000000f1");

    private static readonly Guid[] Slots = [Breakfast, Snack, Dinner];

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    [InlineData(2026, 10, 2026, 9, 28, 2026, 11, 1, 35)]    // Thu 1st, Sat 31st: five weeks
    [InlineData(2026, 2, 2026, 1, 26, 2026, 3, 1, 35)]      // 28 days from a Sunday the 1st
    [InlineData(2027, 2, 2027, 2, 1, 2027, 2, 28, 28)]      // 28 days from a Monday: exactly four weeks
    [InlineData(2026, 8, 2026, 7, 27, 2026, 9, 6, 42)]      // Sat 1st, Mon 31st: six weeks
    [InlineData(2028, 2, 2028, 1, 31, 2028, 3, 5, 35)]      // a leap February
    [InlineData(2026, 12, 2026, 11, 30, 2027, 1, 3, 35)]    // across a year
    public void A_months_grid_is_whole_weeks_from_the_week_of_the_1st_to_the_week_of_the_last(
        int y, int m, int fy, int fm, int fd, int ty, int tm, int td, int count)
    {
        IReadOnlyList<DateOnly> days = MonthGrid.Days(D(y, m, 17));

        Assert.Equal((D(fy, fm, fd), D(ty, tm, td), count), (days[0], days[^1], days.Count));
        Assert.Equal(DayOfWeek.Monday, days[0].DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, days[^1].DayOfWeek);
        Assert.Equal(Enumerable.Range(0, count).Select(i => days[0].AddDays(i)), days);
    }

    [Fact]
    public void A_slot_is_planned_when_anything_there_is_not_skipped_and_skipped_only_when_everything_is()
    {
        MonthDay day = MonthGrid.Mark(D(2026, 10, 27), Slots,
        [
            (Breakfast, false),
            (Snack, true),                     // only skipped: waiting to be replanned
            (Dinner, true), (Dinner, false),   // a skip beside something planned is still planned
        ]);

        Assert.Equal([SlotMark.Planned, SlotMark.Skipped, SlotMark.Planned], day.Slots);
        Assert.Equal(DayCover.Partial, day.Cover);
    }

    [Fact]
    public void A_day_is_full_only_when_every_slot_has_something_planned()
    {
        Assert.Equal(DayCover.Full, MonthGrid.Mark(D(2026, 10, 27), Slots, [(Breakfast, false), (Snack, false), (Dinner, false)]).Cover);
        Assert.Equal(DayCover.Partial, MonthGrid.Mark(D(2026, 10, 27), Slots, [(Dinner, false)]).Cover);
        Assert.Equal(DayCover.Empty, MonthGrid.Mark(D(2026, 10, 27), Slots, []).Cover);
        Assert.Equal(DayCover.Empty, MonthGrid.Mark(D(2026, 10, 27), Slots, [(Snack, true)]).Cover);
        // An entry in a slot since deleted is not shown, so it does not count; no slots at all is nothing to plan.
        Assert.Equal(DayCover.Empty, MonthGrid.Mark(D(2026, 10, 27), Slots, [(Removed, false)]).Cover);
        Assert.Equal(DayCover.Empty, MonthGrid.Mark(D(2026, 10, 27), [], [(Dinner, false)]).Cover);
    }

    [Fact]
    public void What_is_left_to_plan_counts_the_months_days_from_today_and_none_of_the_neighbours()
    {
        DateOnly october = D(2026, 10, 1);
        List<MonthDay> days = [.. MonthGrid.Days(october).Select(d => MonthGrid.Mark(d, Slots,
            d.Day % 3 == 0 ? [(Breakfast, false), (Snack, false), (Dinner, false)]
            : d.Day % 3 == 1 ? [(Dinner, false)]
            : []))];

        // From Thu 22 Oct: the 22nd to the 31st, ten days - 24, 27 and 30 full; 22, 25, 28 and 31 partial; the rest empty.
        Assert.Equal(new MonthToCome(10, 3, 4, 3), MonthGrid.ToCome(october, days, D(2026, 10, 22)));
        // A month to come: every one of its days. A month gone: none.
        Assert.Equal(31, MonthGrid.ToCome(october, days, D(2026, 9, 15)).Days);
        Assert.Equal(new MonthToCome(0, 0, 0, 0), MonthGrid.ToCome(october, days, D(2026, 11, 2)));
    }
}

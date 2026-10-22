namespace AshersLab.MealPlanner.Shared.Domain.Planning;

/// <summary>One slot of a day as the month view marks it.</summary>
public enum SlotMark
{
    /// <summary>Nothing on the calendar there.</summary>
    Empty,

    /// <summary>Something planned (and not skipped) - or eaten, for a day gone by.</summary>
    Planned,

    /// <summary>Only skipped entries: it needs replanning.</summary>
    Skipped,
}

/// <summary>How much of a day is planned.</summary>
public enum DayCover
{
    /// <summary>Nothing planned in any slot.</summary>
    Empty,

    /// <summary>Some slots planned, not all.</summary>
    Partial,

    /// <summary>Every slot of the day has something planned.</summary>
    Full,
}

/// <summary>A day of the month view: each slot's mark, in the day's slot order, and what they add up to.</summary>
public sealed record MonthDay(DateOnly Date, IReadOnlyList<SlotMark> Slots)
{
    /// <summary>
    /// Full when every slot has something planned; a skipped slot is not planned - it is waiting to be replanned. A day
    /// with no slots at all is empty: there is nothing to plan, and nothing planned.
    /// </summary>
    public DayCover Cover =>
        Slots.Count > 0 && Slots.All(s => s == SlotMark.Planned) ? DayCover.Full
        : Slots.Any(s => s == SlotMark.Planned) ? DayCover.Partial
        : DayCover.Empty;
}

/// <summary>The days still to come in a month, by how planned they are: what is left to plan.</summary>
public sealed record MonthToCome(int Days, int Full, int Partial, int Empty);

/// <summary>
/// The calendar a month at a glance, for planning a month ahead: which dates the grid shows, each day's slots marked
/// planned, skipped or empty, and how many of the days to come are planned.
/// </summary>
public static class MonthGrid
{
    /// <summary>The first of the month <paramref name="date"/> is in.</summary>
    public static DateOnly MonthOf(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>The last day of the month that starts on <paramref name="first"/>.</summary>
    public static DateOnly LastOf(DateOnly first) => first.AddMonths(1).AddDays(-1);

    /// <summary>
    /// The dates the month's grid shows: whole weeks, Monday to Sunday, from the week the 1st is in to the week the last
    /// day is in - four to six weeks, the neighbouring months' days filling the first and last rows.
    /// </summary>
    public static IReadOnlyList<DateOnly> Days(DateOnly month)
    {
        DateOnly first = MonthOf(month), from = Rotation.MondayOf(first), to = Rotation.MondayOf(LastOf(first)).AddDays(6);
        return [.. Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays)];
    }

    /// <summary>
    /// A day's mark for each slot, in <paramref name="slots"/>' order: planned when any entry there is not skipped,
    /// skipped when every entry there is, otherwise empty. Entries in a slot not listed (a deleted one) are not counted.
    /// </summary>
    public static MonthDay Mark(DateOnly date, IReadOnlyList<Guid> slots, IEnumerable<(Guid Slot, bool Skipped)> entries)
    {
        ILookup<Guid, bool> bySlot = entries.ToLookup(e => e.Slot, e => e.Skipped);
        return new MonthDay(date, [.. slots.Select(s =>
            bySlot[s].Any(skipped => !skipped) ? SlotMark.Planned
            : bySlot[s].Any() ? SlotMark.Skipped
            : SlotMark.Empty)]);
    }

    /// <summary>
    /// The month's days from <paramref name="today"/> on (all of them for a month to come, none for one gone), by how
    /// planned they are - only what is still to come can still be planned.
    /// </summary>
    public static MonthToCome ToCome(DateOnly month, IEnumerable<MonthDay> days, DateOnly today)
    {
        DateOnly first = MonthOf(month), last = LastOf(first);
        List<MonthDay> coming = [.. days.Where(d => d.Date >= first && d.Date <= last && d.Date >= today)];
        return new MonthToCome(coming.Count,
            coming.Count(d => d.Cover == DayCover.Full),
            coming.Count(d => d.Cover == DayCover.Partial),
            coming.Count(d => d.Cover == DayCover.Empty));
    }
}

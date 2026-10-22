using AshersLab.MealPlanner.Shared.Data.Meals;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>A batch a leftovers entry can be set to eat: cooked before it, and what it has left by then.</summary>
public sealed record BatchChoice(Guid Id, Guid? MealId, DateOnly Day, decimal LeftBefore);

/// <summary>
/// The serves around some days, from either database: every batch cooked and every leftovers entry, counted by
/// <see cref="Serves"/> from the calendar as <see cref="CalendarBook"/> shows it - a removed or hidden entry is not there, so
/// it cooks and eats nothing, exactly like a skipped one. Nothing here is stored.
/// </summary>
/// <remarks>
/// <b>It reads the calendar from its first entry</b> to <see cref="ChoosableDays"/> after the days asked for, so a week's
/// numbers do not change with which week is on screen. No shorter window is right: each "whatever is left" pick depends on
/// what every earlier pick left, and those chain back without limit - a window of three weeks gave "nothing to eat" where
/// the whole calendar gave a serve. And a lunch can be set to eat a batch up to two weeks old, so a batch's last eater can
/// be two weeks after it. A household's calendar is a few thousand rows a year; counting them is cheap.
/// </remarks>
public sealed class ServesBook
{
    /// <summary>A leftovers entry can be set to eat a batch cooked up to this many days before it.</summary>
    public const int ChoosableDays = 14;

    private readonly Dictionary<Guid, PlannedEntry> _entries;
    private readonly Dictionary<Guid, ServesEntry>  _counted;

    private ServesBook(ServesLedger ledger, Dictionary<Guid, PlannedEntry> entries, Dictionary<Guid, ServesEntry> counted)
    {
        Ledger   = ledger;
        _entries = entries;
        _counted = counted;
    }

    public ServesLedger Ledger { get; }

    public BatchServes? BatchOf(Guid entry) => Ledger.Batches.GetValueOrDefault(entry);

    public LeftoversServes? LeftoversOf(Guid entry) => Ledger.Leftovers.GetValueOrDefault(entry);

    /// <summary>The calendar entry behind a batch or a draw, if it was read.</summary>
    public PlannedEntry? Entry(Guid id) => _entries.GetValueOrDefault(id);

    public static async Task<ServesBook> LoadAsync(DbContext db, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        DateOnly first = await FirstDayAsync(db, from, cancellationToken);
        DateOnly last  = to.AddDays(ChoosableDays);
        CalendarBook calendar = await CalendarBook.LoadAsync(db, first, last, cancellationToken);
        Dictionary<Guid, Meal> meals = await db.Set<Meal>().AsNoTracking().Where(m => !m.Deleted).ToDictionaryAsync(m => m.Id, cancellationToken);
        Dictionary<Guid, decimal> slots = await db.Set<Slot>().AsNoTracking().Where(s => !s.Deleted).ToDictionaryAsync(s => s.Id, s => s.Position, cancellationToken);
        IReadOnlyDictionary<Guid, decimal> household = await Household.PortionsAsync(db, cancellationToken);

        Dictionary<Guid, PlannedEntry> entries = [];
        Dictionary<Guid, ServesEntry>  counted = [];
        for (DateOnly day = first; day <= last; day = day.AddDays(1))
        {
            foreach (PlannedEntry entry in calendar.On(day))
            {
                entries[entry.Id] = entry;
                Meal? meal = entry.MealId is { } id ? meals.GetValueOrDefault(id) : null;
                ServesRole role = meal?.Kind switch
                {
                    MealKind.Cooked    => ServesRole.Cooked,
                    MealKind.Leftovers => ServesRole.Leftovers,
                    _                  => ServesRole.Other,
                };
                if (role == ServesRole.Other)
                    continue;
                counted[entry.Id] = new ServesEntry(entry.Id, entry.Day, slots.GetValueOrDefault(entry.SlotId, decimal.MaxValue), role,
                    entry.Skipped, Serves.PortionsOf(entry.People, household), entry.Row.ServesMade ?? meal!.ServesPerBatch, entry.Row.EatsFrom);
            }
        }
        return new ServesBook(Serves.Count(counted.Values), entries, counted);
    }

    /// <summary>The calendar's first day - where it was planned or moved to - or <paramref name="from"/> when that is earlier.</summary>
    public static async Task<DateOnly> FirstDayAsync(DbContext db, DateOnly from, CancellationToken cancellationToken = default)
    {
        IQueryable<CalendarEntry> live = db.Set<CalendarEntry>().AsNoTracking().Where(e => !e.Deleted);
        // Dates are text in the table ("2026-10-26"), so ordering them is ordering the text - the same order.
        DateOnly? planned = await live.OrderBy(e => e.Date).Select(e => (DateOnly?)e.Date).FirstOrDefaultAsync(cancellationToken);
        DateOnly? moved   = await live.Where(e => e.MovedDate != null).OrderBy(e => e.MovedDate).Select(e => e.MovedDate).FirstOrDefaultAsync(cancellationToken);
        DateOnly first = from;
        if (planned is { } p && p < first) first = p;
        if (moved is { } m && m < first) first = m;
        return first;
    }

    /// <summary>
    /// The batches <paramref name="leftovers"/> can be set to eat: cooked, not skipped, in the <see cref="ChoosableDays"/>
    /// days before it - most recent first - each with what it has left before this entry eats.
    /// </summary>
    public IReadOnlyList<BatchChoice> ChoicesFor(PlannedEntry leftovers)
    {
        (DateOnly, decimal, Guid) at = KeyOf(leftovers.Id, leftovers.Day);
        return [.. Ledger.Batches.Values
            .Where(b => b.Cooked && b.Day >= leftovers.Day.AddDays(-ChoosableDays) && Before(KeyOf(b.Id, b.Day), at))
            .OrderByDescending(b => b.Day)
            .Select(b => new BatchChoice(b.Id, _entries.GetValueOrDefault(b.Id)?.MealId, b.Day,
                b.Made - b.Draws.Where(d => d.EntryId != leftovers.Id && Before(KeyOf(d.EntryId, d.Day), at)).Sum(d => d.Serves)))];
    }

    private (DateOnly Day, decimal Slot, Guid Id) KeyOf(Guid id, DateOnly day) =>
        (day, _counted.TryGetValue(id, out ServesEntry? e) ? e.SlotPosition : decimal.MaxValue, id);

    private static bool Before((DateOnly Day, decimal Slot, Guid Id) a, (DateOnly Day, decimal Slot, Guid Id) b) =>
        a.Day != b.Day ? a.Day < b.Day : a.Slot != b.Slot ? a.Slot < b.Slot : a.Id.CompareTo(b.Id) < 0;
}

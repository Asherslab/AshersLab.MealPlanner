using AshersLab.MealPlanner.Shared.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>
/// A calendar entry as the pages show it: where it is now, the meal it eats, who eats it and whether it was skipped -
/// each "as planned" field resolved against its template entry. <see cref="Now"/> is what the entry sheet's diff needs.
/// </summary>
public sealed record PlannedEntry(CalendarEntry Row, CalendarEntryNow Now, TemplateEntry? TemplateEntry, PlanTemplate? Template, IReadOnlyList<Guid> Choices)
{
    public Guid Id => Row.Id;

    public DateOnly Day => Now.Day;

    public Guid SlotId => Now.InSlot;

    /// <summary>Null when there is none: a plan entry whose choices are all gone, or whose template was deleted.</summary>
    public Guid? MealId => Now.Meal;

    /// <summary>Null for everyone.</summary>
    public IReadOnlyList<Guid>? People => Now.People;

    public bool Skipped => Row.Status == CalendarStatus.Skipped;

    /// <summary>Its date has passed and it was not skipped: it counts as eaten.</summary>
    public bool EatenBy(DateOnly today) => !Skipped && Day < today;
}

/// <summary>
/// The calendar as the pages read it, from either database: the entries on some days, and every skipped entry waiting to be
/// replanned. <b>An untouched entry a plan made is shown only while the plan still puts it there</b> - its template entry
/// live, the template's start and week count mapping its date to that entry's week and day. So removing an entry from the
/// plan, or moving the plan's start, takes untouched entries off the calendar without an op of its own, on every device at
/// once; anything the planner changed (a meal, people, a skip, a move) stays. Entries added by hand are always shown.
/// </summary>
public sealed class CalendarBook
{
    private readonly ILookup<DateOnly, PlannedEntry> _byDay;

    private CalendarBook(PlanBook plans, List<PlannedEntry> inRange, List<PlannedEntry> skipped)
    {
        Plans    = plans;
        _byDay   = inRange.ToLookup(e => e.Day);
        ToReplan = [.. skipped.OrderBy(e => e.Day).ThenBy(e => e.Row.TemplateEntryId ?? e.Id)];
    }

    public PlanBook Plans { get; }

    /// <summary>Every skipped entry, wherever it is, earliest first: "to replan".</summary>
    public IReadOnlyList<PlannedEntry> ToReplan { get; }

    /// <summary>The live entries in the days loaded, where they are now (moved ones on the day they moved to).</summary>
    public static async Task<CalendarBook> LoadAsync(DbContext db, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        PlanBook plans = await PlanBook.LoadAsync(db, cancellationToken);
        // Dates are text in the table ("2026-10-26"), so comparing them is comparing the text - which orders the same.
        List<CalendarEntry> rows = await db.Set<CalendarEntry>().AsNoTracking()
            .Where(e => !e.Deleted && ((e.Date >= from && e.Date <= to) || (e.MovedDate >= from && e.MovedDate <= to)
                                       || e.Status == CalendarStatus.Skipped))
            .ToListAsync(cancellationToken);

        List<PlannedEntry> shown = [.. rows.Select(r => Resolve(plans, r)).OfType<PlannedEntry>()];
        return new CalendarBook(plans,
            [.. shown.Where(e => e.Day >= from && e.Day <= to)],
            [.. shown.Where(e => e.Skipped)]);
    }

    /// <summary>A day's entries in the order they were planned: by template entry (GUIDv7), or their own id when added by hand.</summary>
    public IEnumerable<PlannedEntry> On(DateOnly day) => _byDay[day].OrderBy(e => e.Row.TemplateEntryId ?? e.Id);

    public IEnumerable<PlannedEntry> On(DateOnly day, Guid slot) => On(day).Where(e => e.SlotId == slot);

    /// <summary>The entry as shown, or null when it is not: an untouched entry its plan no longer puts on its date.</summary>
    public static PlannedEntry? Resolve(PlanBook plans, CalendarEntry row)
    {
        TemplateEntry? source   = row.TemplateEntryId is { } id ? plans.Entry(id) : null;
        PlanTemplate?  template = source is null ? null : plans.Template(source.TemplateId);
        IReadOnlyList<Guid> choices = source is null ? [] : [.. plans.ChoicesOf(source.Id).Select(c => c.MealId)];

        if (row.TemplateEntryId is not null && !row.Touched
            && !(source is not null && template?.StartsOn is { } starts
                 && Rotation.Puts(starts, template.WeekCount, new RotationEntry(source.Id, source.Week, source.Day, source.SlotId), row.Date)))
            return null;

        CalendarEntryNow now = new(row.TemplateEntryId is not null, choices.Count > 0 ? choices[0] : null, source?.PeopleIds(),
            row.MealId, row.People, row.Status, row.Date, row.SlotId, row.MovedDate, row.MovedSlotId);
        return new PlannedEntry(row, now, source, template, choices);
    }

    /// <summary>
    /// What applying <paramref name="template"/> from <paramref name="startsOn"/> over the days writes: a create for every
    /// entry it puts there that the calendar has never held. Tombstones count as held - an entry removed stays removed.
    /// </summary>
    public static async Task<ApplyResult> ApplyAsync(DbContext db, PlanBook plans, PlanTemplate template, DateOnly startsOn,
                                                     DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GeneratedEntry> generated = Rotation.Generate(startsOn, template.WeekCount,
            plans.EntriesOf(template.Id).Select(e => new RotationEntry(e.Id, e.Week, e.Day, e.SlotId)), from, to);
        HashSet<Guid> ids = [.. generated.Select(g => g.Id)];
        HashSet<Guid> held = [.. await db.Set<CalendarEntry>().AsNoTracking()
            .Where(e => e.TemplateEntryId != null && e.Date >= from && e.Date <= to)
            .Select(e => e.Id).ToListAsync(cancellationToken)];
        held.IntersectWith(ids);
        return Rotation.Apply(generated, held);
    }

    /// <summary>
    /// The live entries from <paramref name="today"/> on that name something themselves - a meal, a slot moved to, people -
    /// for "is it still used?" before a delete. What a plan puts there untouched is the plan's (<see cref="PlanBook"/>).
    /// </summary>
    public static async Task<List<CalendarEntry>> ComingAsync(DbContext db, DateOnly today, CancellationToken cancellationToken = default) =>
        await db.Set<CalendarEntry>().AsNoTracking()
            .Where(e => !e.Deleted && e.Status == CalendarStatus.Planned && (e.Date >= today || e.MovedDate >= today))
            .ToListAsync(cancellationToken);
}

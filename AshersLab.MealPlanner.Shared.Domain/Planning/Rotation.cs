using System.Security.Cryptography;
using System.Text;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Planning;

/// <summary>One template entry as applying a plan reads it: where in the rotation it falls.</summary>
public sealed record RotationEntry(Guid Id, int Week, DayOfWeek Day, Guid SlotId);

/// <summary>A calendar entry applying a plan makes: its id is <see cref="Rotation.EntryId"/>, the same on every device.</summary>
public sealed record GeneratedEntry(Guid Id, Guid TemplateEntryId, DateOnly Date, Guid SlotId);

/// <summary>What applying a plan over some days writes: the entries the calendar lacks, and how many it already had.</summary>
public sealed record ApplyResult(IReadOnlyList<GeneratedEntry> Create, int AlreadyThere);

/// <summary>
/// A plan's rotation laid onto real dates. <b>Dates, never datetimes</b>: a week is seven <see cref="DateOnly"/> days, so
/// a daylight-saving change (a 23- or 25-hour day) cannot move an entry to the wrong day.
/// </summary>
/// <remarks>
/// A template's <c>StartsOn</c> is the Monday its week 1 began; every Monday since (and before) is the next (or previous)
/// week, wrapping after <c>WeekCount</c> weeks. Applying a plan never edits an entry the calendar already has, so it is
/// safe to repeat and to run on two devices at once: each entry's id comes from its template entry and date
/// (<see cref="EntryId"/>), so two devices applying the same plan create the same rows, and the merge keeps one of each.
/// </remarks>
public static class Rotation
{
    /// <summary>More days than this in one apply is a mistake: twelve weeks, the longest plan.</summary>
    public const int MaxDays = PlanTemplateRules.MaxWeeks * 7;

    /// <summary>The Monday of the week <paramref name="date"/> is in - itself, when it is a Monday.</summary>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// Which week of the rotation (from 1) <paramref name="date"/> falls in, when week 1 began on <paramref name="startsOn"/>.
    /// Dates before the start count backwards, so the week before week 1 is the last week.
    /// </summary>
    public static int WeekOf(DateOnly startsOn, int weekCount, DateOnly date)
    {
        Check(startsOn, weekCount);
        int days  = date.DayNumber - startsOn.DayNumber;
        // Floor division: day -1 is in week index -1, not 0.
        int index = days >= 0 ? days / 7 : -((-days + 6) / 7);
        return ((index % weekCount) + weekCount) % weekCount + 1;
    }

    /// <summary>The <c>StartsOn</c> that makes the week of <paramref name="monday"/> week <paramref name="week"/>.</summary>
    public static DateOnly StartFor(DateOnly monday, int week, int weekCount)
    {
        if (monday.DayOfWeek != DayOfWeek.Monday)
            throw new ArgumentException("Weeks begin on a Monday.", nameof(monday));
        if (week < 1 || week > weekCount)
            throw new ArgumentOutOfRangeException(nameof(week), week, $"The plan has weeks 1 to {weekCount}.");
        // Any Monday a whole rotation earlier or later would do; the nearest one at or before is the one shown.
        return monday.AddDays(-7 * (week - 1));
    }

    /// <summary>Whether the rotation puts <paramref name="entry"/> on <paramref name="date"/>. An entry beyond the week count is shown nowhere.</summary>
    public static bool Puts(DateOnly startsOn, int weekCount, RotationEntry entry, DateOnly date) =>
        entry.Week <= weekCount && date.DayOfWeek == entry.Day && WeekOf(startsOn, weekCount, date) == entry.Week;

    /// <summary>Every calendar entry the rotation puts on the days from <paramref name="from"/> to <paramref name="to"/>, both included, by date.</summary>
    public static IReadOnlyList<GeneratedEntry> Generate(DateOnly startsOn, int weekCount, IEnumerable<RotationEntry> entries, DateOnly from, DateOnly to)
    {
        Check(startsOn, weekCount);
        if (to.DayNumber - from.DayNumber + 1 > MaxDays)
            throw new ArgumentOutOfRangeException(nameof(to), to, $"Apply at most {MaxDays} days at once.");

        ILookup<(int Week, DayOfWeek Day), RotationEntry> byDay = entries.Where(e => e.Week <= weekCount).ToLookup(e => (e.Week, e.Day));
        List<GeneratedEntry> made = [];
        for (DateOnly date = from; date <= to; date = date.AddDays(1))
            foreach (RotationEntry entry in byDay[(WeekOf(startsOn, weekCount, date), date.DayOfWeek)])
                made.Add(new GeneratedEntry(EntryId(entry.Id, date), entry.Id, date, entry.SlotId));
        return made;
    }

    /// <summary>
    /// What applying writes: a create for each generated entry whose id the calendar has never had. <paramref name="existing"/>
    /// is every calendar entry id held, <b>tombstones included</b> - an entry removed from the calendar stays removed, and one
    /// skipped, moved or changed is left exactly as it is.
    /// </summary>
    public static ApplyResult Apply(IReadOnlyList<GeneratedEntry> generated, IReadOnlySet<Guid> existing)
    {
        List<GeneratedEntry> create = [.. generated.Where(g => !existing.Contains(g.Id))];
        return new ApplyResult(create, generated.Count - create.Count);
    }

    /// <summary>
    /// The calendar entry a template entry makes on a date: a version-8 GUID from the two, so every device applying the plan
    /// mints the same one. Not time-ordered - order calendar entries by their template entry (<c>CalendarBook</c>).
    /// </summary>
    public static Guid EntryId(Guid templateEntry, DateOnly date)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"calendar|{templateEntry:N}|{OpValues.FormatDate(date)}"));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    private static void Check(DateOnly startsOn, int weekCount)
    {
        if (startsOn.DayOfWeek != DayOfWeek.Monday)
            throw new ArgumentException("A rotation starts on a Monday.", nameof(startsOn));
        if (weekCount < 1)
            throw new ArgumentOutOfRangeException(nameof(weekCount), weekCount, "A plan runs for at least one week.");
    }
}

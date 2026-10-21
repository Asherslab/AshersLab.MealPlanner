using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Planning;

/// <summary>A rotation a plan had before a change: on the days before <paramref name="Until"/>, week 1 began <paramref name="StartsOn"/>.</summary>
public sealed record RotationPhase(DateOnly Until, DateOnly StartsOn, int WeekCount);

/// <summary>
/// <b>Changing a plan's start or week count changes the calendar from a date on, never before it.</b> A plan's
/// <c>StartsOn</c> and <c>WeekCount</c> are its rotation now; <see cref="PlanTemplateRules.EarlierField"/> lists the
/// rotations it had before, each until the day it was changed. So "the week of 2 Nov is week 1" moves the plan from
/// 2 November on, and the untouched entries before it - what was eaten, which batches and serves count - stay where the
/// rotation of their day put them.
/// </summary>
/// <remarks>
/// One field, written with the change in the same op, so the start and its history arrive together. Two devices changing
/// the same plan at once: the later op's start and history win whole, as for any one field.
/// </remarks>
public static class RotationPhases
{
    /// <summary>"2026-11-02|2026-10-05|4;..." - until, start and weeks, earliest first; null for none.</summary>
    public static string? Format(IReadOnlyList<RotationPhase> phases) =>
        phases.Count == 0 ? null
            : string.Join(';', phases.Select(p => $"{OpValues.FormatDate(p.Until)}|{OpValues.FormatDate(p.StartsOn)}|{OpValues.FormatInt(p.WeekCount)}"));

    /// <summary>Reads <see cref="Format"/>'s output, and only that: Mondays for starts, weeks in range, untils strictly rising.</summary>
    public static bool TryParse(string? value, out IReadOnlyList<RotationPhase> phases)
    {
        phases = [];
        if (value is null)
            return true;
        List<RotationPhase> read = [];
        foreach (string part in value.Split(';'))
        {
            string[] bits = part.Split('|');
            if (bits.Length != 3
                || !OpValues.TryParseDate(bits[0], out DateOnly until)
                || !OpValues.TryParseDate(bits[1], out DateOnly starts) || starts.DayOfWeek != DayOfWeek.Monday
                || !OpValues.TryParseInt(bits[2], out int weeks) || PlanTemplateRules.WeekCountProblem(weeks) is not null
                || (read.Count > 0 && until <= read[^1].Until))
                return false;
            read.Add(new RotationPhase(until, starts, weeks));
        }
        if (read.Count == 0)
            return false;
        phases = read;
        return true;
    }

    /// <summary>The rotation in effect on <paramref name="date"/>: the first earlier phase not yet over, else the current one.</summary>
    public static (DateOnly? StartsOn, int WeekCount) On(IReadOnlyList<RotationPhase> earlier, DateOnly? startsOn, int weekCount, DateOnly date)
    {
        foreach (RotationPhase phase in earlier)
            if (date < phase.Until)
                return (phase.StartsOn, phase.WeekCount);
        return (startsOn, weekCount);
    }

    /// <summary>
    /// The earlier phases once the rotation becomes <paramref name="newStart"/> and <paramref name="newWeeks"/> from
    /// <paramref name="from"/> on: every day before it keeps the rotation it had - cut at <paramref name="from"/> - and every
    /// phase after it is gone, the new rotation replacing them. A phase the same as the new rotation, right before it, is
    /// folded into it, so changing nothing leaves nothing.
    /// </summary>
    public static IReadOnlyList<RotationPhase> Change(IReadOnlyList<RotationPhase> earlier, DateOnly? startsOn, int weekCount,
                                                      DateOnly from, DateOnly newStart, int newWeeks)
    {
        List<RotationPhase> kept = [];
        DateOnly? begins = null;   // where the phase being looked at begins; null for "always"
        foreach (RotationPhase phase in earlier)
        {
            if (begins is null || begins < from)
                kept.Add(phase.Until <= from ? phase : phase with { Until = from });
            begins = phase.Until;
        }
        if (startsOn is { } current && (begins is null || begins < from))
            kept.Add(new RotationPhase(from, current, weekCount));
        while (kept.Count > 0 && kept[^1].StartsOn == newStart && kept[^1].WeekCount == newWeeks)
            kept.RemoveAt(kept.Count - 1);
        return kept;
    }
}

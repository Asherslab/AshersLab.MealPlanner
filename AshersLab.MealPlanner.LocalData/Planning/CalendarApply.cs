using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Data.Planning;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.LocalData.Planning;

/// <summary>What applying a plan writes, and the counts the planner is shown before it is written.</summary>
/// <param name="Week">Which week of the plan <c>From</c> falls in.</param>
public sealed record CalendarApplyPlan(ApplyResult Result, IReadOnlyList<LocalEdit> Edits, int Week);

/// <summary>
/// Applying a plan from the browser: its start (when it changed) and a create for every entry the calendar has never held,
/// as one write (<see cref="LocalOps.WriteAllAsync"/>). The calendar page and the tests both build it here, so what they
/// test is what the page sends.
/// </summary>
public static class CalendarApply
{
    /// <returns>Null when the template is gone.</returns>
    /// <remarks>
    /// A different start changes the plan's rotation from <paramref name="from"/> on, keeping every day before it as it was
    /// (<see cref="RotationPhases"/>): "the week of 2 Nov is week 1" never moves what was eaten the week before.
    /// </remarks>
    public static async Task<CalendarApplyPlan?> PlanAsync(LocalDbContext db, Guid templateId, DateOnly startsOn, DateOnly from, DateOnly to,
                                                           CancellationToken cancellationToken = default)
    {
        PlanBook plans = await PlanBook.LoadAsync(db, cancellationToken);
        if (plans.Template(templateId) is not { } template)
            return null;

        List<LocalEdit> edits = [];
        IReadOnlyList<OpField> rotation = PlanTemplateRules.RotationChange(template.Earlier, template.StartsOn, template.WeekCount,
            from, startsOn, template.WeekCount);
        PlanTemplate after = template;
        if (rotation.Count > 0)
        {
            edits.Add(new LocalEdit(SyncedEntities.PlanTemplate, template.Id, OpKind.Update, rotation));
            after = new PlanTemplate { Id = template.Id, Name = template.Name, WeekCount = template.WeekCount, StartsOn = startsOn, Earlier = template.Earlier };
            if (rotation.FirstOrDefault(f => f.Name == PlanTemplateRules.EarlierField) is { } earlier)
                after.Earlier = earlier.Value;
        }

        ApplyResult result = await CalendarBook.ApplyAsync(db, plans, after, from, to, cancellationToken);
        edits.AddRange(result.Create.Select(g => new LocalEdit(SyncedEntities.CalendarEntry, g.Id, OpKind.Create, CalendarEntryRules.CreateFields(g))));
        return new CalendarApplyPlan(result, edits, Rotation.WeekOf(startsOn, template.WeekCount, from));
    }
}

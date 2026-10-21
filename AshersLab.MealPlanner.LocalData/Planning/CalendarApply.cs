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
    public static async Task<CalendarApplyPlan?> PlanAsync(LocalDbContext db, Guid templateId, DateOnly startsOn, DateOnly from, DateOnly to,
                                                           CancellationToken cancellationToken = default)
    {
        PlanBook plans = await PlanBook.LoadAsync(db, cancellationToken);
        if (plans.Template(templateId) is not { } template)
            return null;

        ApplyResult result = await CalendarBook.ApplyAsync(db, plans, template, startsOn, from, to, cancellationToken);
        List<LocalEdit> edits = [];
        if (template.StartsOn != startsOn)
            edits.Add(new LocalEdit(SyncedEntities.PlanTemplate, template.Id, OpKind.Update,
                [new OpField(PlanTemplateRules.StartsOnField, OpValues.FormatDate(startsOn))]));
        edits.AddRange(result.Create.Select(g => new LocalEdit(SyncedEntities.CalendarEntry, g.Id, OpKind.Create, CalendarEntryRules.CreateFields(g))));
        return new CalendarApplyPlan(result, edits, Rotation.WeekOf(startsOn, template.WeekCount, from));
    }
}

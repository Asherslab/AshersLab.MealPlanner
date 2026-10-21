using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Data.Import;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Import;

namespace AshersLab.MealPlanner.LocalData.Import;

/// <summary>
/// Runs the spreadsheet import against this browser's database: what is here now, the planner's answers, then every
/// op in one transaction (<see cref="LocalOps.WriteAllAsync"/>) - all of it or none.
/// </summary>
public static class SheetImportWriter
{
    /// <summary>
    /// Plans against what the database holds at this moment - not when the review was opened, since a sync may have
    /// brought the same rows from another device meanwhile - and writes the plan. Returns what was written.
    /// </summary>
    public static async Task<ImportPlan> WriteAsync(
        LocalDbContext db, Guid deviceId, DateTimeOffset now, DateOnly today, ParsedSheet sheet, ImportChoices choices,
        CancellationToken cancellationToken = default)
    {
        ImportKnown known = await ImportKnownReader.ReadAsync(db, cancellationToken);
        // A millisecond apart, so the ids sort in the order the sheet gave them: a meal's ingredients list by id.
        int        minted = 0;
        ImportPlan plan   = new SheetImport(sheet, known).Plan(choices, today, () => Guid.CreateVersion7(now.AddMilliseconds(minted++)));
        if (plan.NothingToDo)
            return plan;

        await LocalOps.WriteAllAsync(db, deviceId, now,
            [.. plan.Edits.Select(e => new LocalEdit(
                SyncedEntities.Find(e.Entity) ?? throw new InvalidOperationException($"The import wrote an unknown entity '{e.Entity}'."),
                e.Id, e.Kind, e.Fields))],
            cancellationToken);
        return plan;
    }
}

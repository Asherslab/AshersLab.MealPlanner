using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Planning;

/// <summary>A plan entry as the entry sheet holds it: its meals in order (the usual one first), and who eats it (null: everyone).</summary>
public sealed record TemplateEntryDraft(IReadOnlyList<Guid> Meals, IReadOnlyList<Guid>? People)
{
    /// <summary>
    /// What saving this over <paramref name="before"/> sends - only what changed, since an op that changes nothing still
    /// wins its merge and would undo an edit made meanwhile elsewhere.
    /// </summary>
    /// <param name="before">The entry's people, and its choice rows (id, meal) in order; null for a new entry.</param>
    public TemplateEntryChange ChangesFrom((IReadOnlyList<Guid>? People, IReadOnlyList<(Guid Id, Guid Meal)> Choices)? before)
    {
        List<Guid> meals = [.. Meals.Distinct()];
        if (before is not { } was)
            return new TemplateEntryChange(PlanPeople.Format(People) is { } people ? [new OpField(TemplateEntryRules.PeopleField, people)] : [], [], meals);

        List<OpField> fields = [];
        if (PlanPeople.Format(People) is var after && after != PlanPeople.Format(was.People))
            fields.Add(new OpField(TemplateEntryRules.PeopleField, after));

        // Order is the rows' ids, and a new row sorts last. So a meal's row is kept while the kept rows stay in the new
        // order; from the first meal that is new or out of order, every later meal gets a new row and its old one goes.
        // A second row of one meal (two phones adding it offline) goes too.
        List<(Guid Id, Guid Meal)> rows = [.. was.Choices];
        HashSet<Guid> keptRows = [];
        List<Guid>    added    = [];
        int  lastKept = -1;
        foreach (Guid meal in meals)
        {
            int at = rows.FindIndex(r => r.Meal == meal);
            if (added.Count == 0 && at > lastKept)
            {
                keptRows.Add(rows[at].Id);
                lastKept = at;
            }
            else
                added.Add(meal);
        }
        return new TemplateEntryChange(fields, [.. rows.Where(r => !keptRows.Contains(r.Id)).Select(r => r.Id)], added);
    }
}

/// <param name="EntryFields">The entry's own fields to send - for a new entry, those beyond where it is (its people).</param>
/// <param name="RemoveChoices">Choice rows to delete.</param>
/// <param name="AddMeals">Meals to add a choice row for, in order.</param>
public sealed record TemplateEntryChange(IReadOnlyList<OpField> EntryFields, IReadOnlyList<Guid> RemoveChoices, IReadOnlyList<Guid> AddMeals)
{
    public bool IsEmpty => EntryFields.Count == 0 && RemoveChoices.Count == 0 && AddMeals.Count == 0;
}

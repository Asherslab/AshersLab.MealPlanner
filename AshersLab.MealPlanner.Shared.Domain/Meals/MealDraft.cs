using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Meals;

/// <summary>A meal as an editor holds it: its own fields, not its ingredients.</summary>
public sealed record MealDraft(string Name, Guid? GroupId, MealKind Kind, int ServesPerBatch, long? FixedCostCents, string? Notes)
{
    /// <summary>
    /// The fields to send: everything set, for a new meal (null <paramref name="before"/>); otherwise only what
    /// differs - an op that changes nothing still wins its merge and would undo an edit made meanwhile elsewhere.
    /// Empty when nothing changed: send no op at all.
    /// </summary>
    public List<OpField> FieldsChangedFrom(MealDraft? before)
    {
        List<OpField> fields = [];
        if (before is null || before.Name != Name)
            fields.Add(new(MealRules.NameField, Name));
        if (before is null ? GroupId is not null : before.GroupId != GroupId)
            fields.Add(new(MealRules.GroupField, OpValues.FormatId(GroupId)));
        if (before is null || before.Kind != Kind)
            fields.Add(new(MealRules.KindField, MealKinds.Format(Kind)));
        if (before is null || before.ServesPerBatch != ServesPerBatch)
            fields.Add(new(MealRules.ServesPerBatchField, OpValues.FormatInt(ServesPerBatch)));
        if (before is null ? FixedCostCents is not null : before.FixedCostCents != FixedCostCents)
            fields.Add(new(MealRules.FixedCostCentsField, FixedCostCents is { } cents ? OpValues.FormatCents(cents) : null));
        if (before is null ? Notes is not null : before.Notes != Notes)
            fields.Add(new(MealRules.NotesField, Notes));
        return fields;
    }
}

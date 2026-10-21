using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Ingredients;

/// <summary>An ingredient as an editor holds it. <see cref="Unit"/> is null until chosen: there is no default.</summary>
public sealed record IngredientDraft(string Name, BaseUnit? Unit, Guid? CategoryId, Guid? UsualStoreId, bool IsStaple)
{
    /// <summary>
    /// The fields to send: everything set, for a new ingredient (null <paramref name="before"/>); otherwise only what
    /// differs - an op that changes nothing still wins its merge and would undo an edit made meanwhile elsewhere.
    /// Empty when nothing changed: send no op at all.
    /// </summary>
    public List<OpField> FieldsChangedFrom(IngredientDraft? before)
    {
        List<OpField> fields = [];
        if (before is null || before.Name != Name)
            fields.Add(new(IngredientRules.NameField, Name));
        if (Unit is { } unit && (before is null || before.Unit != unit))
            fields.Add(new(IngredientRules.BaseUnitField, BaseUnits.Format(unit)));
        if (before is null ? CategoryId is not null : before.CategoryId != CategoryId)
            fields.Add(new(IngredientRules.CategoryField, OpValues.FormatId(CategoryId)));
        if (before is null ? UsualStoreId is not null : before.UsualStoreId != UsualStoreId)
            fields.Add(new(IngredientRules.UsualStoreField, OpValues.FormatId(UsualStoreId)));
        if (before is null ? IsStaple : before.IsStaple != IsStaple)
            fields.Add(new(IngredientRules.IsStapleField, OpValues.FormatBool(IsStaple)));
        return fields;
    }
}

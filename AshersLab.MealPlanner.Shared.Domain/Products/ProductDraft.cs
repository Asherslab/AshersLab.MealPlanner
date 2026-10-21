using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Products;

/// <summary>A product as an editor holds it.</summary>
public sealed record ProductDraft(string Name, Guid StoreId, string? StoreCode, decimal PackQuantity, Guid? IngredientId)
{
    /// <summary>
    /// The fields to send: everything set, for a new product (null <paramref name="before"/>); otherwise only what
    /// differs - an op that changes nothing still wins its merge and would undo an edit made meanwhile elsewhere.
    /// Empty when nothing changed: send no op at all.
    /// </summary>
    public List<OpField> FieldsChangedFrom(ProductDraft? before)
    {
        List<OpField> fields = [];
        if (before is null || before.Name != Name)
            fields.Add(new(ProductRules.NameField, Name));
        if (before is null || before.StoreId != StoreId)
            fields.Add(new(ProductRules.StoreField, OpValues.FormatId(StoreId)));
        if (before is null ? StoreCode is not null : before.StoreCode != StoreCode)
            fields.Add(new(ProductRules.StoreCodeField, StoreCode));
        if (before is null || before.PackQuantity != PackQuantity)
            fields.Add(new(ProductRules.PackQuantityField, OpValues.FormatDecimal(PackQuantity)));
        if (before is null ? IngredientId is not null : before.IngredientId != IngredientId)
            fields.Add(new(ProductRules.IngredientField, OpValues.FormatId(IngredientId)));
        return fields;
    }
}

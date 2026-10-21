using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Ingredients;

/// <summary>How an op's fields map onto an <see cref="Ingredient"/> row.</summary>
public sealed class IngredientEntity : SyncedEntity<Ingredient>
{
    public override string Entity => IngredientRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => IngredientRules.OpProblem(kind, fields);

    protected override void Set(Ingredient row, OpField field)
    {
        switch (field.Name)
        {
            case IngredientRules.NameField:
                row.Name = field.Value ?? "";
                break;
            case IngredientRules.BaseUnitField:
                row.BaseUnit = BaseUnits.TryParse(field.Value, out BaseUnit unit) ? unit : throw Unreadable(field);
                break;
            case IngredientRules.CategoryField:
                row.CategoryId = OpValues.TryParseId(field.Value, out Guid? category) ? category : throw Unreadable(field);
                break;
            case IngredientRules.UsualStoreField:
                row.UsualStoreId = OpValues.TryParseId(field.Value, out Guid? store) ? store : throw Unreadable(field);
                break;
            case IngredientRules.IsStapleField:
                row.IsStaple = OpValues.TryParseBool(field.Value, out bool staple) ? staple : throw Unreadable(field);
                break;
            default:
                throw new ArgumentException($"An ingredient has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(Ingredient row) =>
    [
        new(IngredientRules.NameField, row.Name),
        new(IngredientRules.BaseUnitField, BaseUnits.Format(row.BaseUnit)),
        new(IngredientRules.CategoryField, OpValues.FormatId(row.CategoryId)),
        new(IngredientRules.UsualStoreField, OpValues.FormatId(row.UsualStoreId)),
        new(IngredientRules.IsStapleField, OpValues.FormatBool(row.IsStaple)),
    ];

    private static ArgumentException Unreadable(OpField field) =>
        new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}

using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Meals;

/// <summary>How an op's fields map onto a <see cref="MealIngredient"/> row.</summary>
public sealed class MealIngredientEntity : SyncedEntity<MealIngredient>
{
    public override string Entity => MealIngredientRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => MealIngredientRules.OpProblem(kind, fields);

    protected override void Set(MealIngredient row, OpField field)
    {
        switch (field.Name)
        {
            case MealIngredientRules.MealField:
                row.MealId = OpValues.TryParseId(field.Value, out Guid? meal) && meal is { } mealId ? mealId : throw Unreadable(field);
                break;
            case MealIngredientRules.IngredientField:
                row.IngredientId = OpValues.TryParseId(field.Value, out Guid? ingredient) && ingredient is { } id ? id : throw Unreadable(field);
                break;
            case MealIngredientRules.QuantityField:
                row.Quantity = field.Value is null ? null
                    : OpValues.TryParseDecimal(field.Value, out decimal quantity) ? quantity
                    : throw Unreadable(field);
                break;
            case MealIngredientRules.NoteField:
                row.Note = field.Value;
                break;
            default:
                throw new ArgumentException($"A meal's ingredient has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(MealIngredient row) =>
    [
        new(MealIngredientRules.MealField, OpValues.FormatId(row.MealId)),
        new(MealIngredientRules.IngredientField, OpValues.FormatId(row.IngredientId)),
        new(MealIngredientRules.QuantityField, row.Quantity is { } quantity ? OpValues.FormatDecimal(quantity) : null),
        new(MealIngredientRules.NoteField, row.Note),
    ];

    private static ArgumentException Unreadable(OpField field) =>
        new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}

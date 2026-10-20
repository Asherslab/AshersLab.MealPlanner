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
            default:
                throw new ArgumentException($"An ingredient has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(Ingredient row) => [new(IngredientRules.NameField, row.Name)];
}

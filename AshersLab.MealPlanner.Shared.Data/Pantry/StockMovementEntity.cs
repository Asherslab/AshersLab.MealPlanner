using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Pantry;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Pantry;

/// <summary>How an op's fields map onto a <see cref="StockMovement"/> row.</summary>
public sealed class StockMovementEntity : SyncedEntity<StockMovement>
{
    public override string Entity => StockRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => StockRules.OpProblem(kind, fields);

    protected override void Set(StockMovement row, OpField field)
    {
        switch (field.Name)
        {
            case StockRules.IngredientField:
                row.IngredientId = OpValues.TryParseId(field.Value, out Guid? ingredient) && ingredient is { } id ? id : throw Unreadable(field);
                break;
            case StockRules.QuantityField:
                row.Quantity = OpValues.TryParseDecimal(field.Value, out decimal quantity) ? quantity : throw Unreadable(field);
                break;
            case StockRules.OccurredOnField:
                row.OccurredOn = OpValues.TryParseDate(field.Value, out DateOnly date) ? date : throw Unreadable(field);
                break;
            case StockRules.ReasonField:
                row.Reason = StockReasons.TryParse(field.Value, out StockReason reason) ? reason : throw Unreadable(field);
                break;
            default:
                throw new ArgumentException($"A stock change has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(StockMovement row) =>
    [
        new(StockRules.IngredientField, OpValues.FormatId(row.IngredientId)),
        new(StockRules.QuantityField, OpValues.FormatDecimal(row.Quantity)),
        new(StockRules.OccurredOnField, OpValues.FormatDate(row.OccurredOn)),
        new(StockRules.ReasonField, StockReasons.Format(row.Reason)),
    ];

    private static ArgumentException Unreadable(OpField field) =>
        new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}

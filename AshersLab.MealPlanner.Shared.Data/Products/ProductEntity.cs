using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Products;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Products;

/// <summary>How an op's fields map onto a <see cref="Product"/> row.</summary>
public sealed class ProductEntity : SyncedEntity<Product>
{
    public override string Entity => ProductRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => ProductRules.OpProblem(kind, fields);

    protected override void Set(Product row, OpField field)
    {
        switch (field.Name)
        {
            case ProductRules.NameField:
                row.Name = field.Value ?? "";
                break;
            case ProductRules.StoreField:
                row.StoreId = OpValues.TryParseId(field.Value, out Guid? store) && store is { } id ? id : throw Unreadable(field);
                break;
            case ProductRules.StoreCodeField:
                row.StoreCode = field.Value;
                break;
            case ProductRules.PackQuantityField:
                row.PackQuantity = OpValues.TryParseDecimal(field.Value, out decimal quantity) ? quantity : throw Unreadable(field);
                break;
            case ProductRules.IngredientField:
                row.IngredientId = OpValues.TryParseId(field.Value, out Guid? ingredient) ? ingredient : throw Unreadable(field);
                break;
            default:
                throw new ArgumentException($"A product has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(Product row) =>
    [
        new(ProductRules.NameField, row.Name),
        new(ProductRules.StoreField, OpValues.FormatId(row.StoreId)),
        new(ProductRules.StoreCodeField, row.StoreCode),
        new(ProductRules.PackQuantityField, OpValues.FormatDecimal(row.PackQuantity)),
        new(ProductRules.IngredientField, OpValues.FormatId(row.IngredientId)),
    ];

    private static ArgumentException Unreadable(OpField field) =>
        new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}

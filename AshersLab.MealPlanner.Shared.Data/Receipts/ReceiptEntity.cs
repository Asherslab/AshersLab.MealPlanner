using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Receipts;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Receipts;

/// <summary>How an op's fields map onto a <see cref="Receipt"/> row.</summary>
public sealed class ReceiptEntity : SyncedEntity<Receipt>
{
    public override string Entity => ReceiptRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => ReceiptRules.OpProblem(kind, fields);

    protected override void Set(Receipt row, OpField field)
    {
        switch (field.Name)
        {
            case ReceiptRules.StoreField:
                row.StoreId = OpValues.TryParseId(field.Value, out Guid? store) && store is { } id ? id : throw Unreadable(field);
                break;
            case ReceiptRules.OrderNumberField:
                row.OrderNumber = field.Value ?? throw Unreadable(field);
                break;
            case ReceiptRules.PurchasedOnField:
                row.PurchasedOn = OpValues.TryParseDate(field.Value, out DateOnly bought) ? bought : throw Unreadable(field);
                break;
            case ReceiptRules.ImportedOnField:
                row.ImportedOn = OpValues.TryParseDate(field.Value, out DateOnly imported) ? imported : throw Unreadable(field);
                break;
            case ReceiptRules.SubtotalCentsField:
                row.SubtotalCents = Cents(field);
                break;
            case ReceiptRules.FeesCentsField:
                row.FeesCents = Cents(field);
                break;
            case ReceiptRules.TotalCentsField:
                row.TotalCents = Cents(field);
                break;
            case ReceiptRules.PaidCentsField:
                row.PaidCents = field.Value is null ? null : Cents(field);
                break;
            case ReceiptRules.RefundCentsField:
                row.RefundCents = field.Value is null ? null : Cents(field);
                break;
            default:
                throw new ArgumentException($"A receipt has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(Receipt row) => ReceiptFields.Of(row);

    internal static long Cents(OpField field) => OpValues.TryParseCents(field.Value, out long cents) ? cents : throw Unreadable(field);

    internal static ArgumentException Unreadable(OpField field) => new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}

/// <summary>How an op's fields map onto a <see cref="ReceiptLine"/> row.</summary>
public sealed class ReceiptLineEntity : SyncedEntity<ReceiptLine>
{
    public override string Entity => ReceiptLineRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => ReceiptLineRules.OpProblem(kind, fields);

    protected override void Set(ReceiptLine row, OpField field)
    {
        switch (field.Name)
        {
            case ReceiptLineRules.ReceiptField:
                row.ReceiptId = OpValues.TryParseId(field.Value, out Guid? receipt) && receipt is { } id ? id : throw ReceiptEntity.Unreadable(field);
                break;
            case ReceiptLineRules.PositionField:
                row.Position = OpValues.TryParseInt(field.Value, out int position) ? position : throw ReceiptEntity.Unreadable(field);
                break;
            case ReceiptLineRules.DescriptionField:
                row.Description = field.Value ?? throw ReceiptEntity.Unreadable(field);
                break;
            case ReceiptLineRules.StoreCodeField:
                row.StoreCode = field.Value;
                break;
            case ReceiptLineRules.CategoryField:
                row.Category = field.Value;
                break;
            case ReceiptLineRules.QuantityField:
                row.Quantity = OpValues.TryParseDecimal(field.Value, out decimal quantity) ? quantity : throw ReceiptEntity.Unreadable(field);
                break;
            case ReceiptLineRules.WeightGramsField:
                row.WeightGrams = field.Value is null ? null
                    : OpValues.TryParseDecimal(field.Value, out decimal grams) ? grams : throw ReceiptEntity.Unreadable(field);
                break;
            case ReceiptLineRules.UnitPriceCentsField:
                row.UnitPriceCents = field.Value is null ? null : ReceiptEntity.Cents(field);
                break;
            case ReceiptLineRules.AmountCentsField:
                row.AmountCents = ReceiptEntity.Cents(field);
                break;
            case ReceiptLineRules.PerKgCentsField:
                row.PerKgCents = field.Value is null ? null : ReceiptEntity.Cents(field);
                break;
            case ReceiptLineRules.DiscountCentsField:
                row.DiscountCents = ReceiptEntity.Cents(field);
                break;
            case ReceiptLineRules.ProductField:
                row.ProductId = OpValues.TryParseId(field.Value, out Guid? product) ? product : throw ReceiptEntity.Unreadable(field);
                break;
            case ReceiptLineRules.KindField:
                row.Kind = ReceiptLineKinds.TryParse(field.Value, out ReceiptLineKind kind) ? kind : throw ReceiptEntity.Unreadable(field);
                break;
            default:
                throw new ArgumentException($"A receipt line has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(ReceiptLine row) => ReceiptFields.Of(row);
}

/// <summary>How an op's fields map onto a <see cref="ProductAlias"/> row.</summary>
public sealed class ProductAliasEntity : SyncedEntity<ProductAlias>
{
    public override string Entity => ProductAliasRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => ProductAliasRules.OpProblem(kind, fields);

    protected override void Set(ProductAlias row, OpField field)
    {
        switch (field.Name)
        {
            case ProductAliasRules.StoreField:
                row.StoreId = OpValues.TryParseId(field.Value, out Guid? store) && store is { } s ? s : throw ReceiptEntity.Unreadable(field);
                break;
            case ProductAliasRules.TextField:
                row.Text = field.Value ?? throw ReceiptEntity.Unreadable(field);
                break;
            case ProductAliasRules.ProductField:
                row.ProductId = OpValues.TryParseId(field.Value, out Guid? product) && product is { } p ? p : throw ReceiptEntity.Unreadable(field);
                break;
            default:
                throw new ArgumentException($"An alias has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(ProductAlias row) => ReceiptFields.Of(row);
}

/// <summary>Every field of each row, as one create carries them: what an import writes and what a snapshot sends.</summary>
public static class ReceiptFields
{
    public static IReadOnlyList<OpField> Of(Receipt r) =>
    [
        new(ReceiptRules.StoreField, OpValues.FormatId(r.StoreId)),
        new(ReceiptRules.OrderNumberField, r.OrderNumber),
        new(ReceiptRules.PurchasedOnField, OpValues.FormatDate(r.PurchasedOn)),
        new(ReceiptRules.SubtotalCentsField, OpValues.FormatCents(r.SubtotalCents)),
        new(ReceiptRules.FeesCentsField, OpValues.FormatCents(r.FeesCents)),
        new(ReceiptRules.TotalCentsField, OpValues.FormatCents(r.TotalCents)),
        new(ReceiptRules.PaidCentsField, r.PaidCents is { } paid ? OpValues.FormatCents(paid) : null),
        new(ReceiptRules.RefundCentsField, r.RefundCents is { } refund ? OpValues.FormatCents(refund) : null),
        new(ReceiptRules.ImportedOnField, OpValues.FormatDate(r.ImportedOn)),
    ];

    public static IReadOnlyList<OpField> Of(ReceiptLine l) =>
    [
        new(ReceiptLineRules.ReceiptField, OpValues.FormatId(l.ReceiptId)),
        new(ReceiptLineRules.PositionField, OpValues.FormatInt(l.Position)),
        new(ReceiptLineRules.DescriptionField, l.Description),
        new(ReceiptLineRules.StoreCodeField, l.StoreCode),
        new(ReceiptLineRules.CategoryField, l.Category),
        new(ReceiptLineRules.QuantityField, OpValues.FormatDecimal(l.Quantity)),
        new(ReceiptLineRules.UnitPriceCentsField, l.UnitPriceCents is { } unit ? OpValues.FormatCents(unit) : null),
        new(ReceiptLineRules.AmountCentsField, OpValues.FormatCents(l.AmountCents)),
        new(ReceiptLineRules.WeightGramsField, l.WeightGrams is { } grams ? OpValues.FormatDecimal(grams) : null),
        new(ReceiptLineRules.PerKgCentsField, l.PerKgCents is { } perKg ? OpValues.FormatCents(perKg) : null),
        new(ReceiptLineRules.DiscountCentsField, OpValues.FormatCents(l.DiscountCents)),
        new(ReceiptLineRules.ProductField, OpValues.FormatId(l.ProductId)),
        new(ReceiptLineRules.KindField, ReceiptLineKinds.Format(l.Kind)),
    ];

    public static IReadOnlyList<OpField> Of(ProductAlias a) =>
    [
        new(ProductAliasRules.StoreField, OpValues.FormatId(a.StoreId)),
        new(ProductAliasRules.TextField, a.Text),
        new(ProductAliasRules.ProductField, OpValues.FormatId(a.ProductId)),
    ];
}

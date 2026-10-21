using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Shopping;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Receipts;

/// <summary>What a receipt line turned out to be. On the wire and in both databases by <b>name</b> - never rename a member.</summary>
public enum ReceiptLineKind
{
    /// <summary>Something to eat: matched to a product of an ingredient, it prices it and goes into the pantry.</summary>
    Food,

    /// <summary>Bin bags, deodorant: priced against its product, never in the pantry.</summary>
    Household,

    /// <summary>Paper bags, a collection fee: part of the shop's cost, not a product.</summary>
    Fee,

    /// <summary>The planner said to leave it out.</summary>
    Ignored,
}

public static class ReceiptLineKinds
{
    public static string Format(ReceiptLineKind kind) => kind.ToString();

    /// <summary>Reads exactly a member's name, case and all - nothing else, numbers included.</summary>
    public static bool TryParse(string? value, out ReceiptLineKind kind)
    {
        foreach (ReceiptLineKind candidate in Enum.GetValues<ReceiptLineKind>())
        {
            if (candidate.ToString() == value)
            {
                kind = candidate;
                return true;
            }
        }
        kind = default;
        return false;
    }
}

/// <summary>
/// A receipt, its lines and the aliases they teach, as the op log sees them (docs/modules/receipts/receipts.md). <b>Every
/// id here is derived, never minted</b>: a receipt's from its store and order number, a line's from its receipt and place,
/// and the price and stock movement a line writes from the line. So importing one invoice on two phones offline makes the
/// same rows - nothing doubled - and "already imported" is a lookup of one id.
/// </summary>
/// <remarks>
/// The flip side of derived ids: a deleted receipt's id is a tombstone that ignores every later op, so a receipt deleted
/// could never be imported again. Nothing offers to delete one.
/// </remarks>
public static class ReceiptRules
{
    /// <summary>The op log's <c>Entity</c> for receipts. On the wire and in both databases - never rename.</summary>
    public const string Entity = "Receipt";

    public const string StoreField = "StoreId";
    public const string OrderNumberField = "OrderNumber";
    public const string PurchasedOnField = "PurchasedOn";
    public const string SubtotalCentsField = "SubtotalCents";
    public const string FeesCentsField = "FeesCents";
    public const string TotalCentsField = "TotalCents";
    public const string PaidCentsField = "PaidCents";
    public const string RefundCentsField = "RefundCents";

    /// <summary>The day it was imported: what "already imported" names.</summary>
    public const string ImportedOnField = "ImportedOn";

    public const int MaxOrderNumberLength = 50;

    public const string Noun = "A receipt";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        StoreField, OrderNumberField, PurchasedOnField, SubtotalCentsField, FeesCentsField, TotalCentsField, PaidCentsField,
        RefundCentsField, ImportedOnField,
    };

    private static readonly string[] RequiredOnCreate =
        [StoreField, OrderNumberField, PurchasedOnField, SubtotalCentsField, FeesCentsField, TotalCentsField, ImportedOnField];

    /// <summary>A store's receipt: a version-8 GUID from the store and its order number, the same on every device.</summary>
    public static Guid ReceiptId(Guid storeId, string orderNumber) =>
        ShoppingListRules.Derived($"receipt|{storeId:N}|{Names.Normalise(orderNumber).ToUpperInvariant()}");

    /// <summary>A receipt's line at <paramref name="position"/>.</summary>
    public static Guid LineId(Guid receiptId, int position) => ShoppingListRules.Derived($"receipt-line|{receiptId:N}|{position}");

    /// <summary>The price a line saw.</summary>
    public static Guid PriceId(Guid lineId) => ShoppingListRules.Derived($"receipt-price|{lineId:N}");

    /// <summary>What a line put into the pantry.</summary>
    public static Guid StockId(Guid lineId) => ShoppingListRules.Derived($"receipt-stock|{lineId:N}");

    /// <summary>Null when <paramref name="cents"/> is an amount a receipt prints - nothing is fine, a negative a discount.</summary>
    public static string? AmountProblem(long cents) => Math.Abs(cents) > Money.MaxCents ? "That is more than any shop." : null;

    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            StoreField       => OpValues.TryParseId(field.Value, out Guid? store) && store is not null ? null : "A receipt needs a store.",
            OrderNumberField => field.Value is { Length: > 0 and <= MaxOrderNumberLength } n && Names.Normalise(n) == n ? null : "The order number is not one.",
            PurchasedOnField or ImportedOnField => OpValues.TryParseDate(field.Value, out _) ? null : "The date is not a date.",
            SubtotalCentsField or FeesCentsField or TotalCentsField => OpValues.TryParseCents(field.Value, out long c) ? AmountProblem(c) : "The amount is not whole cents.",
            PaidCentsField or RefundCentsField => field.Value is null ? null
                : OpValues.TryParseCents(field.Value, out long c) ? AmountProblem(c) : "The amount is not whole cents.",
            _ => null,
        });
}

/// <summary>A receipt's line as the op log sees it. Its product and kind change when the planner matches it; the rest never.</summary>
public static class ReceiptLineRules
{
    /// <summary>The op log's <c>Entity</c> for receipt lines. On the wire and in both databases - never rename.</summary>
    public const string Entity = "ReceiptLine";

    public const string ReceiptField = "ReceiptId";
    public const string PositionField = "Position";
    public const string DescriptionField = "Description";
    public const string StoreCodeField = "StoreCode";
    public const string CategoryField = "Category";
    public const string QuantityField = "Quantity";
    public const string UnitPriceCentsField = "UnitPriceCents";
    public const string AmountCentsField = "AmountCents";
    public const string WeightGramsField = "WeightGrams";
    public const string PerKgCentsField = "PerKgCents";
    public const string DiscountCentsField = "DiscountCents";
    public const string ProductField = "ProductId";
    public const string KindField = "Kind";

    /// <summary>Receipts print long descriptions: a hundred characters is not enough for a meat tray's.</summary>
    public const int MaxDescriptionLength = 200;

    public const int MaxPositions = 1000;

    public const string Noun = "A receipt line";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        ReceiptField, PositionField, DescriptionField, StoreCodeField, CategoryField, QuantityField, UnitPriceCentsField,
        AmountCentsField, WeightGramsField, PerKgCentsField, DiscountCentsField, ProductField, KindField,
    };

    private static readonly string[] RequiredOnCreate = [ReceiptField, PositionField, DescriptionField, QuantityField, AmountCentsField, KindField];

    public static string? TextProblem(string? value, int max, string what) =>
        value is { Length: > 0 } t && t.Length <= max && Names.Normalise(t) == t ? null : $"The {what} is not one.";

    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            ReceiptField        => OpValues.TryParseId(field.Value, out Guid? r) && r is not null ? null : "A receipt line needs a receipt.",
            PositionField       => OpValues.TryParseInt(field.Value, out int p) && p is >= 0 and < MaxPositions ? null : "The position is not one.",
            DescriptionField    => TextProblem(field.Value, MaxDescriptionLength, "description"),
            StoreCodeField      => field.Value is null ? null : TextProblem(field.Value, 50, "store code"),
            CategoryField       => field.Value is null ? null : TextProblem(field.Value, Names.MaxLength, "category"),
            QuantityField       => OpValues.TryParseDecimal(field.Value, out decimal q) && q >= 0 && q <= Quantity.Max ? null : "The quantity is not one.",
            WeightGramsField    => field.Value is null ? null
                : OpValues.TryParseDecimal(field.Value, out decimal w) && w > 0 && w <= Quantity.Max ? null : "The weight is not one.",
            AmountCentsField or DiscountCentsField => OpValues.TryParseCents(field.Value, out long c) ? ReceiptRules.AmountProblem(c) : "The amount is not whole cents.",
            UnitPriceCentsField or PerKgCentsField => field.Value is null ? null
                : OpValues.TryParseCents(field.Value, out long c) ? ReceiptRules.AmountProblem(c) : "The price is not whole cents.",
            ProductField        => OpValues.TryParseId(field.Value, out _) ? null : "The product is not an id.",
            KindField           => ReceiptLineKinds.TryParse(field.Value, out _) ? null : "The kind is not food, household, fee or ignored.",
            _                   => null,
        });
}

/// <summary>
/// A receipt description a product has been matched to, as the op log sees it: what makes the next receipt match by
/// itself. <b>Its own row, never a field of the product</b> - a list in one field would merge last-writer-wins, and two
/// phones each teaching an alias offline would lose one. Its id is derived from the store and the text, so the same
/// description has one alias, and matching it again to another product moves it (the later match wins).
/// </summary>
public static class ProductAliasRules
{
    /// <summary>The op log's <c>Entity</c> for aliases. On the wire and in both databases - never rename.</summary>
    public const string Entity = "ProductAlias";

    public const string StoreField = "StoreId";
    public const string TextField = "Text";
    public const string ProductField = "ProductId";

    public const string Noun = "An alias";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal) { StoreField, TextField, ProductField };

    private static readonly string[] RequiredOnCreate = [StoreField, TextField, ProductField];

    /// <summary>The key a description matches by: spaces normalised, case ignored.</summary>
    public static string Key(string description) => Names.Normalise(description).ToLowerInvariant();

    public static Guid AliasId(Guid storeId, string description) => ShoppingListRules.Derived($"alias|{storeId:N}|{Key(description)}");

    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            StoreField   => OpValues.TryParseId(field.Value, out Guid? s) && s is not null ? null : "An alias needs a store.",
            TextField    => ReceiptLineRules.TextProblem(field.Value, ReceiptLineRules.MaxDescriptionLength, "alias"),
            ProductField => OpValues.TryParseId(field.Value, out Guid? p) && p is not null ? null : "An alias needs a product.",
            _            => null,
        });
}

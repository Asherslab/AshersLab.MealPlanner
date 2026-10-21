using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Receipts;

namespace AshersLab.MealPlanner.Shared.Data.Receipts;

/// <summary>
/// An imported receipt: its store, order number and totals as printed. <b>The file itself is never stored</b> - only what
/// was read from it. Its id is derived from the store and order number (<see cref="ReceiptRules.ReceiptId"/>), so a second
/// import of the same order finds the first. The same row in the server's database and in every browser's.
/// </summary>
public class Receipt : ISyncedRow
{
    public Guid Id { get; set; }

    public Guid StoreId { get; set; }

    public string OrderNumber { get; set; } = "";

    public DateOnly PurchasedOn { get; set; }

    public long SubtotalCents { get; set; }

    public long FeesCents { get; set; }

    public long TotalCents { get; set; }

    public long? PaidCents { get; set; }

    /// <summary>Weighed meat supplied under the maximum weight is refunded after the order is paid.</summary>
    public long? RefundCents { get; set; }

    public DateOnly ImportedOn { get; set; }

    public bool Deleted { get; set; }
}

/// <summary>One line of an imported receipt, matched to a product or not (<see cref="ReceiptLineRules"/>).</summary>
public class ReceiptLine : ISyncedRow
{
    /// <summary>Derived from the receipt and <see cref="Position"/> (<see cref="ReceiptRules.LineId"/>).</summary>
    public Guid Id { get; set; }

    public Guid ReceiptId { get; set; }

    public int Position { get; set; }

    /// <summary>As printed, markers stripped: the alias a match teaches.</summary>
    public string Description { get; set; } = "";

    public string? StoreCode { get; set; }

    /// <summary>The heading the store printed it under: a hint, never a key.</summary>
    public string? Category { get; set; }

    /// <summary>Packs supplied - 0 for one out of stock.</summary>
    public decimal Quantity { get; set; }

    public long? UnitPriceCents { get; set; }

    public long AmountCents { get; set; }

    public decimal? WeightGrams { get; set; }

    public long? PerKgCents { get; set; }

    public long DiscountCents { get; set; }

    /// <summary>Not a foreign key: null until it is matched.</summary>
    public Guid? ProductId { get; set; }

    public ReceiptLineKind Kind { get; set; }

    public bool Deleted { get; set; }
}

/// <summary>A receipt description matched to a product: the next receipt printing it matches by itself (<see cref="ProductAliasRules"/>).</summary>
public class ProductAlias : ISyncedRow
{
    /// <summary>Derived from the store and the text (<see cref="ProductAliasRules.AliasId"/>).</summary>
    public Guid Id { get; set; }

    public Guid StoreId { get; set; }

    public string Text { get; set; } = "";

    public Guid ProductId { get; set; }

    public bool Deleted { get; set; }
}

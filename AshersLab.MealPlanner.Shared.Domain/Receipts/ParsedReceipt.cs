using AshersLab.MealPlanner.Shared.Domain.Prices;

namespace AshersLab.MealPlanner.Shared.Domain.Receipts;

/// <summary>
/// One line of a receipt as the store printed it, before anyone matched it to a product.
/// </summary>
/// <param name="Position">Its place on the receipt, top to bottom, from 0: what makes its id (<see cref="ReceiptRules.LineId"/>).</param>
/// <param name="Description">As printed, markers stripped ("* ", "(Sub) ", "~ ") and spaces normalised: the alias it teaches.</param>
/// <param name="StoreCode">The store's own code for the product, when the receipt prints one (Costco does; Woolworths does not).</param>
/// <param name="Category">The heading it was printed under ("Dairy", "Fruit & Veg") - a hint, never a key: the names vary.</param>
/// <param name="Supplied">How many packs came home - 0 for one out of stock, or an original line a substitute replaced.</param>
/// <param name="UnitPriceCents">The shelf price of one pack; for variable-weight meat, null - it was priced by the kilo.</param>
/// <param name="AmountCents">What the line cost.</param>
/// <param name="WeightGrams">Variable-weight meat: the weight supplied ("1 @ 662g").</param>
/// <param name="PerKgCents">Variable-weight meat: the price per kilogram.</param>
/// <param name="DiscountCents">How much less the line cost than its packs at the shelf price: a substitute charged at the original's price.</param>
/// <param name="Gst">Printed with the GST marker: mostly household things, a hint.</param>
/// <param name="Substitute">A replacement the store chose for something out of stock.</param>
public sealed record ParsedLine(
    int     Position,
    string  Description,
    string? StoreCode,
    string? Category,
    decimal Supplied,
    long?   UnitPriceCents,
    long    AmountCents,
    decimal? WeightGrams,
    long?   PerKgCents,
    long    DiscountCents,
    bool    Gst,
    bool    Substitute)
{
    /// <summary>Nothing came home: no price seen, nothing into the pantry.</summary>
    public bool NotSupplied => Supplied == 0;

    /// <summary>The price one pack was seen at: the shelf price, or for variable-weight meat what this pack cost.</summary>
    public long? PackPriceCents => WeightGrams is not null ? (Supplied > 0 ? (long)Math.Round(AmountCents / Supplied, MidpointRounding.AwayFromZero) : null) : UnitPriceCents;
}

/// <summary>A fee on the receipt: "Paper bags $2.00", "Collection fee $0.00".</summary>
public sealed record ParsedFee(string Name, long AmountCents);

/// <summary>
/// A receipt as read from the store's file, before review: its header, lines, fees and totals as printed. Nothing here is
/// stored until the planner imports it; the file itself never is.
/// </summary>
/// <param name="OrderNumber">The store's order or receipt number: <b>the dedupe key</b> (<see cref="ReceiptRules.ReceiptId"/>).</param>
public sealed record ParsedReceipt(
    string                   OrderNumber,
    DateOnly                 PurchasedOn,
    IReadOnlyList<ParsedLine> Lines,
    IReadOnlyList<ParsedFee> Fees,
    long                     SubtotalCents,
    long                     TotalCents,
    long?                    PaidCents,
    long?                    RefundCents)
{
    public long FeesCents => Fees.Sum(f => f.AmountCents);

    public long LinesCents => Lines.Sum(l => l.AmountCents);

    /// <summary>
    /// Null when the receipt adds up; otherwise both figures. <b>A receipt that does not add up is never imported</b>: the
    /// lines would be wrong somewhere - a line read twice, a price in the wrong column - and every cost built on them with it.
    /// Two checks: the lines make the sub total, and the sub total and fees make the total.
    /// </summary>
    public string? TotalsProblem() =>
        LinesCents != SubtotalCents
            ? $"The lines add up to {Money.Format(LinesCents)}, but the receipt's sub total is {Money.Format(SubtotalCents)}."
        : SubtotalCents + FeesCents != TotalCents
            ? $"The sub total and fees make {Money.Format(SubtotalCents + FeesCents)}, but the receipt's total is {Money.Format(TotalCents)}."
        : null;
}

/// <summary>A file that is not a receipt this store's reader knows, and why.</summary>
public sealed class ReceiptFormatException(string message) : Exception(message);

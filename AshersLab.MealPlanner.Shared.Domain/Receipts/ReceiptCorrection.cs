using System.Globalization;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Prices;

namespace AshersLab.MealPlanner.Shared.Domain.Receipts;

/// <summary>
/// The planner correcting what OCR misread, in the review: a line's description, item number, quantity, price and amount, or
/// the receipt's total, items sold and date - typed as the receipt prints them. The corrected receipt is checked again
/// (<see cref="ParsedReceipt.TotalsProblem"/>), so a correction cannot make a receipt that does not add up importable.
/// </summary>
public static class ReceiptCorrection
{
    /// <summary>The line as corrected; null with the reason in <paramref name="problem"/> when a field cannot be read.</summary>
    public static ParsedLine? Line(ParsedLine line, string? description, string? code, string? quantity, string? price, string? amount, out string? problem)
    {
        string name = Names.Normalise(description);
        string? number = Names.Normalise(code) is { Length: > 0 } c ? c : null;
        bool counted = decimal.TryParse(quantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal q) && q >= 0 && q <= Quantity.Max;
        problem = name.Length == 0 ? "The line needs a description."
            : name.Length > ReceiptLineRules.MaxDescriptionLength ? "That description is too long."
            : number is { Length: > 50 } ? "That item number is too long."
            : !counted ? "Type the quantity as a number, like 2."
            : null;
        if (problem is not null)
            return null;
        long? unit = Money.ParseCents(price, out problem);
        if (unit is null)
            return null;
        long? cents = Money.ParseCents(amount, out problem);
        if (cents is null)
            return null;
        // The discount stays as read - it came from its own line. Worked out from the corrected figures instead, any amount
        // under the price would pass for a discount and the line's own check (ParsedLine.SumProblem) would never fail.
        return line with { Description = name, StoreCode = number, Supplied = q, UnitPriceCents = unit, AmountCents = cents.Value };
    }

    /// <summary>The receipt with its total, items sold and date as corrected; null with the reason when one cannot be read.</summary>
    public static ParsedReceipt? Receipt(ParsedReceipt receipt, string? total, string? itemsSold, DateOnly? date, out string? problem)
    {
        long? cents = Money.ParseCents(total, out problem);
        if (cents is null)
            return null;
        int? sold = null;
        if (!string.IsNullOrWhiteSpace(itemsSold))
        {
            if (!int.TryParse(itemsSold, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
            {
                problem = "Type the items sold as a whole number.";
                return null;
            }
            sold = n;
        }
        // A receipt with no fees has no sub total of its own: the total is both.
        long subtotal = receipt.Fees.Count == 0 && receipt.SubtotalCents == receipt.TotalCents ? cents.Value : receipt.SubtotalCents;
        return receipt with { TotalCents = cents.Value, SubtotalCents = subtotal, ItemsSold = sold, PurchasedOn = date ?? receipt.PurchasedOn };
    }
}

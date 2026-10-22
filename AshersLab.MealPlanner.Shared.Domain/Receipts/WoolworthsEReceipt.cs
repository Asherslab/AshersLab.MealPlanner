using System.Globalization;
using System.Text.RegularExpressions;

namespace AshersLab.MealPlanner.Shared.Domain.Receipts;

/// <summary>
/// Reads a Woolworths in-store receipt - the "eReceipt" the Woolworths app saves as a PDF after a shop at the till
/// (docs/modules/receipts/receipts.md). A text PDF of the till roll: one tall page, one monospaced row per printed line,
/// each word its own run. Read by position (<see cref="PdfRows"/>), like the online invoice.
/// </summary>
/// <remarks>
/// The layout, as the real one prints it:
/// <list type="bullet">
/// <item>A header (the store, its phone and address, <c>TAX INVOICE - ABN ...</c>), then the column headings
///   <c>Description ... $</c>.</item>
/// <item>An item on one row, its amount at the right: <c>Milk 2L  3.40</c>. A leading <c>#</c> marks GST, <c>^</c> a
///   promotional price.</item>
/// <item>An item of more than one, or weighed: the name alone, then <c>Qty 2 @ $9.00 each  18.00</c> or
///   <c>1.250 kg NET @ $4.00/kg  5.00</c>.</item>
/// <item>An offer: a row of its own with a negative amount, <c>COLA OFFER  -4.00</c>, coming off the item before it.</item>
/// <item><c>12 SUBTOTAL $64.20</c> - the count is the items sold, a weighed item one - then any rows that change it, then
///   <c>TOTAL $64.20</c>. The payment, GST and rewards blocks follow and are not read.</item>
/// <item>The footer: <c>WOOLWORTHS 1234</c> and <c>POS 004 TRANS 5108 10:41 12/03/2026</c> - the store, till and
///   transaction, which with the date and time are the receipt's number.</item>
/// </list>
/// No stockcode is printed, so its lines match by alias - descriptions no other Woolworths file prints: the till's are
/// abbreviated ("Choc Chip Bscts 300g").
/// </remarks>
public static partial class WoolworthsEReceipt
{
    [GeneratedRegex(@"^(?<sign>-)?\$?(?<dollars>\d{1,3}(?:,\d{3})+|\d+)\.(?<cents>\d{2})$")]
    private static partial Regex AmountShape();

    [GeneratedRegex(@"^(?:Qty\s+)?(?<count>\d+)\s*@\s*\$(?<price>[\d,]+\.\d{2})\s*each$", RegexOptions.IgnoreCase)]
    private static partial Regex QuantityShape();

    [GeneratedRegex(@"^(?<weight>\d+(?:\.\d+)?)\s*(?<unit>kg|g)\s+(?:NET\s+)?@\s*\$(?<price>[\d,]+\.\d{2})\s*/\s*kg$", RegexOptions.IgnoreCase)]
    private static partial Regex WeighedShape();

    [GeneratedRegex(@"^(?:(?<count>\d+)\s+)?SUBTOTAL$", RegexOptions.IgnoreCase)]
    private static partial Regex SubtotalShape();

    [GeneratedRegex(@"\bPOS\s+(?<pos>\d+)\s+TRANS\s+(?<trans>\d+)\s+(?<h>\d{1,2}):(?<min>\d{2})\s+(?<d>\d{1,2})/(?<m>\d{1,2})/(?<y>\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex StampShape();

    [GeneratedRegex(@"^WOOLWORTHS\s+(?<store>\d{3,5})$", RegexOptions.IgnoreCase)]
    private static partial Regex StoreShape();

    /// <summary>The markers a line can start with: <c>#</c> GST, <c>^</c> a promotional price.</summary>
    private const string Markers = "#^*";

    /// <summary>Whether the pages look like an in-store receipt: its column headings, a sub total and the till's stamp.</summary>
    public static bool Recognises(IReadOnlyList<PdfPage> pages)
    {
        List<PdfRow> rows = [.. pages.SelectMany(PdfRows.Of)];
        return rows.Any(Headings) && rows.Any(r => SubtotalShape().IsMatch(Label(r))) && rows.Any(r => StampShape().IsMatch(r.Text));
    }

    /// <exception cref="ReceiptFormatException">Not an in-store receipt, or a part of one this reader cannot place.</exception>
    public static ParsedReceipt Parse(IReadOnlyList<PdfPage> pages)
    {
        List<PdfRow> rows = [.. pages.SelectMany(PdfRows.Of)];

        Match stamp = rows.Select(r => StampShape().Match(r.Text)).FirstOrDefault(m => m.Success)
                      ?? throw new ReceiptFormatException("This is not a Woolworths receipt: it has no till number and date.");
        DateOnly date;
        try
        {
            date = new DateOnly(Int(stamp, "y"), Int(stamp, "m"), Int(stamp, "d"));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ReceiptFormatException($"The receipt's date \"{stamp.Groups["d"].Value}/{stamp.Groups["m"].Value}/{stamp.Groups["y"].Value}\" is not a date.");
        }
        string? store = rows.Select(r => StoreShape().Match(r.Text)).FirstOrDefault(m => m.Success)?.Groups["store"].Value;
        // The same till and transaction numbers come round again; with the store, the day and the minute they do not.
        string number = string.Join(' ', new[]
        {
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            $"{Int(stamp, "h"):00}:{stamp.Groups["min"].Value}",
            store, stamp.Groups["pos"].Value, stamp.Groups["trans"].Value,
        }.Where(p => p is not null));

        int start = rows.FindIndex(Headings);
        int subtotalAt = rows.FindIndex(r => SubtotalShape().IsMatch(Label(r)));
        if (start < 0 || subtotalAt < start)
            throw new ReceiptFormatException("The receipt's items could not be found.");
        PdfRow subtotalRow = rows[subtotalAt];
        long subtotal = Amount(subtotalRow) ?? throw new ReceiptFormatException("The receipt has no sub total.");
        int? itemsSold = SubtotalShape().Match(Label(subtotalRow)).Groups["count"] is { Success: true } count
            ? int.Parse(count.Value, CultureInfo.InvariantCulture)
            : null;

        List<ParsedLine> lines = [];
        string? waiting = null;   // a name printed alone, its quantity or weight on the next row
        bool waitingGst = false;
        foreach (PdfRow row in rows.Skip(start + 1).Take(subtotalAt - start - 1))
        {
            string label = Label(row);
            long? amount = Amount(row);

            if (waiting is not null && amount is { } detailAmount && QuantityShape().Match(label) is { Success: true } qty)
            {
                decimal packs = int.Parse(qty.Groups["count"].Value, CultureInfo.InvariantCulture);
                long unit = Cents(qty.Groups["price"].Value);
                lines.Add(new ParsedLine(lines.Count, waiting, null, null, packs, unit, detailAmount, null, null,
                    Math.Max(0, (long)(packs * unit) - detailAmount), waitingGst, false));
                waiting = null;
                continue;
            }
            if (waiting is not null && amount is { } weighedAmount && WeighedShape().Match(label) is { Success: true } weighed)
            {
                decimal grams = decimal.Parse(weighed.Groups["weight"].Value, CultureInfo.InvariantCulture)
                                * (weighed.Groups["unit"].Value.Equals("kg", StringComparison.OrdinalIgnoreCase) ? 1000 : 1);
                lines.Add(new ParsedLine(lines.Count, waiting, null, null, 1, null, weighedAmount, grams, Cents(weighed.Groups["price"].Value),
                    0, waitingGst, false));
                waiting = null;
                continue;
            }
            // A name alone with an amount under it that is neither a quantity nor a weight: guessing how many would price
            // the product wrong, so the receipt is refused.
            if (waiting is not null && amount is not null)
                throw new ReceiptFormatException($"\"{waiting}\" is followed by \"{label}\", which this reader does not know.");
            // A name alone that nothing followed is a legend ("^Promotional Price"): dropped. It carries no money, so the
            // lines still make the sub total, and an item missed would show in the items sold.
            waiting = null;

            if (amount is { } off && off < 0 && !Markers.Contains(label.FirstOrDefault()))
            {
                // An offer: it comes off the item before it, as on a Costco receipt. One before any item cannot be placed.
                if (lines.Count == 0)
                    throw new ReceiptFormatException($"The offer \"{label}\" comes before any item.");
                ParsedLine item = lines[^1];
                lines[^1] = item with { AmountCents = item.AmountCents + off, DiscountCents = item.DiscountCents - off };
                continue;
            }

            bool gst = label.StartsWith('#');
            string name = Name(label);
            if (name.Length == 0)
                continue;
            if (amount is null)
            {
                (waiting, waitingGst) = (name, gst);
                continue;
            }
            lines.Add(new ParsedLine(lines.Count, name, null, null, 1, amount, amount.Value, null, null, 0, gst, false));
        }

        // Anything between the sub total and the total that costs something - a rewards discount - is a fee, named as printed.
        int totalAt = rows.FindIndex(subtotalAt + 1, r => Label(r).Equals("TOTAL", StringComparison.OrdinalIgnoreCase) && Amount(r) is not null);
        if (totalAt < 0)
            throw new ReceiptFormatException("The receipt has no total.");
        List<ParsedFee> fees = [.. rows.Skip(subtotalAt + 1).Take(totalAt - subtotalAt - 1)
            .Where(r => Amount(r) is not null && Label(r).Length > 0)
            .Select(r => new ParsedFee(Name(Label(r)), Amount(r)!.Value))];

        return new ParsedReceipt(number, date, lines, fees, subtotal, Amount(rows[totalAt])!.Value, null, null, itemsSold);
    }

    private static int Int(Match m, string group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    /// <summary>The column headings: "Description" at the left and "$" over the amounts.</summary>
    private static bool Headings(PdfRow row) =>
        row.Items.Count >= 2 && row.Items[0].Text.Trim() == "Description" && row.Items[^1].Text.Trim() == "$";

    /// <summary>The row's amount: its last run, when that is money. Amounts sit at the row's right; a price inside the text ("@ $9.00 each") never ends it.</summary>
    private static long? Amount(PdfRow row) =>
        row.Items.Count >= 2 && AmountShape().Match(row.Items[^1].Text.Trim()) is { Success: true } m ? Cents(m) : null;

    /// <summary>The row's text without its amount.</summary>
    private static string Label(PdfRow row) =>
        PdfRows.Join(Amount(row) is null ? row.Items : row.Items.Take(row.Items.Count - 1));

    /// <summary>A description: markers stripped, spaces normalised, cut to what a line can hold.</summary>
    private static string Name(string label)
    {
        string name = Names.Normalise(label.TrimStart(Markers.ToCharArray()));
        return name.Length > ReceiptLineRules.MaxDescriptionLength ? name[..ReceiptLineRules.MaxDescriptionLength].TrimEnd() : name;
    }

    private static long Cents(string money) => AmountShape().Match(money) is { Success: true } m ? Cents(m) : throw new ReceiptFormatException($"\"{money}\" is not an amount.");

    private static long Cents(Match m)
    {
        long cents = long.Parse(m.Groups["dollars"].Value.Replace(",", ""), CultureInfo.InvariantCulture) * 100
                     + long.Parse(m.Groups["cents"].Value, CultureInfo.InvariantCulture);
        return m.Groups["sign"].Success ? -cents : cents;
    }
}

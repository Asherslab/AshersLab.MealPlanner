using System.Globalization;
using System.Text.RegularExpressions;

namespace AshersLab.MealPlanner.Shared.Domain.Receipts;

/// <summary>
/// Reads a Woolworths online-order tax invoice from its PDF's text runs (docs/modules/receipts/receipts.md). A text PDF,
/// so no OCR: but its text is stored out of reading order, so everything is read by position (<see cref="PdfRows"/>).
/// </summary>
/// <remarks>
/// The layout, as seven real invoices print it:
/// <list type="bullet">
/// <item>A header on every page: <c>Invoice/Order Number: 312345678</c> (the dedupe key) and <c>Date: 24 Jun 2026</c>.</item>
/// <item>A table under the column headings <c>Line | Description | Ordered | Supplied | Price | Amount</c>, its lines grouped
///   under category headings that are rows of their own. Columns are found from the headings' positions, never fixed.</item>
/// <item><b>A long description wraps above and below its numbers</b>: the row with the numbers can have no description of
///   its own, its text a few points above and below. Text rows that close to a row of numbers are its description; text
///   rows further away are category headings.</item>
/// <item>Variable-weight meat: the numbered row has the price per kilo and no amount; the next row, "~ name", has
///   "1 @ 662g" and the real amount.</item>
/// <item>A "Substitutions" table: the original line with nothing supplied and $0.00, then "(Sub) replacement" charged at
///   the original's price - its shelf price is the Price column, what it cost is the Amount.</item>
/// <item>Totals after the tables: Sub Total, the fees (Paper bags, Collection fee, Service fees), Invoice Total, Paid
///   Amount, sometimes Refund Amount.</item>
/// </list>
/// </remarks>
public static partial class WoolworthsInvoice
{
    /// <summary>A text row this close to a row of numbers is part of its description; the rows themselves are 12.7 apart.</summary>
    public const double WrapDistance = 6.5;

    [GeneratedRegex(@"Invoice/Order Number:\s*(?<number>\d+)")]
    private static partial Regex OrderNumberShape();

    [GeneratedRegex(@"\bDate:\s*(?<date>\d{1,2} [A-Za-z]{3} \d{4})")]
    private static partial Regex DateShape();

    [GeneratedRegex(@"^(?<count>\d+(?:\.\d+)?)\s*@\s*(?<weight>\d+(?:\.\d+)?)\s*(?<unit>kg|g)$", RegexOptions.IgnoreCase)]
    private static partial Regex WeightShape();

    [GeneratedRegex(@"^(?<money>-?\$[\d,]+\.\d{2})\s*/\s*kg$", RegexOptions.IgnoreCase)]
    private static partial Regex PerKgShape();

    [GeneratedRegex(@"^(?<sign>-)?\$(?<dollars>\d{1,3}(?:,\d{3})+|\d+)\.(?<cents>\d{2})$")]
    private static partial Regex MoneyShape();

    /// <summary>Whether the pages look like a Woolworths invoice at all: its order-number header.</summary>
    public static bool Recognises(IReadOnlyList<PdfPage> pages) =>
        pages.SelectMany(PdfRows.Of).Any(r => OrderNumberShape().IsMatch(r.Text)) && pages.SelectMany(PdfRows.Of).Any(r => r.Text.Contains("Woolworths", StringComparison.Ordinal));

    /// <exception cref="ReceiptFormatException">Not a Woolworths invoice, or a part of one this reader cannot place.</exception>
    public static ParsedReceipt Parse(IReadOnlyList<PdfPage> pages)
    {
        List<IReadOnlyList<PdfRow>> rows = [.. pages.Select(PdfRows.Of)];
        List<PdfRow> all = [.. rows.SelectMany(r => r)];

        string orderNumber = all.Select(r => OrderNumberShape().Match(r.Text)).FirstOrDefault(m => m.Success)?.Groups["number"].Value
                             ?? throw new ReceiptFormatException("This is not a Woolworths invoice: it has no order number.");
        string dateText = all.Select(r => DateShape().Match(r.Text)).FirstOrDefault(m => m.Success)?.Groups["date"].Value
                          ?? throw new ReceiptFormatException("The invoice has no date.");
        if (!DateOnly.TryParseExact(dateText, "d MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            throw new ReceiptFormatException($"The invoice's date \"{dateText}\" is not a date.");

        List<ParsedLine> lines = [];
        Columns? columns = null;
        foreach (IReadOnlyList<PdfRow> page in rows)
        {
            List<PdfRow> section = [];
            bool inTable = false;
            foreach (PdfRow row in page)
            {
                string text = row.Text;
                if (Columns.From(row) is { } found)
                {
                    Lines(section, columns, lines);
                    section = [];
                    (columns, inTable) = (found, true);
                    continue;
                }
                if (text.StartsWith("Sub Total", StringComparison.Ordinal) || text.StartsWith("Substitutions", StringComparison.Ordinal)
                    || text.StartsWith("Registered O", StringComparison.Ordinal))
                    inTable = false;
                if (inTable)
                    section.Add(row);
            }
            Lines(section, columns, lines);
        }

        long? Total(string label)
        {
            PdfRow? row = all.LastOrDefault(r => r.Items.Any(i => i.Text.TrimEnd(':').Trim() == label));
            return row?.Items.Select(i => Cents(i.Text)).LastOrDefault(c => c is not null);
        }

        long subtotal = Total("Sub Total") ?? throw new ReceiptFormatException("The invoice has no sub total.");
        long total = Total("Invoice Total") ?? throw new ReceiptFormatException("The invoice has no total.");

        // The fees: every row between the sub total and the total with an amount, named by the text just left of it.
        List<ParsedFee> fees = [];
        int from = all.FindLastIndex(r => r.Items.Any(i => i.Text.TrimEnd(':').Trim() == "Sub Total"));
        int to = all.FindLastIndex(r => r.Items.Any(i => i.Text.TrimEnd(':').Trim() == "Invoice Total"));
        for (int i = from + 1; i < to; i++)
        {
            List<PdfItem> items = [.. all[i].Items];
            int money = items.FindLastIndex(x => Cents(x.Text) is not null);
            if (money > 0)
                fees.Add(new ParsedFee(items[money - 1].Text.TrimEnd(':').Trim(), Cents(items[money].Text)!.Value));
        }

        return new ParsedReceipt(orderNumber, date, lines, fees, subtotal, total, Total("Paid Amount"), Total("Refund Amount"));
    }

    /// <summary>The table's columns, from its heading row: each starts a little left of its heading, numbers being right-aligned.</summary>
    private sealed record Columns(double Description, double Ordered, double Supplied, double Price, double Amount)
    {
        private const double Slack = 12;

        public static Columns? From(PdfRow row)
        {
            double? At(string heading) => row.Items.FirstOrDefault(i => i.Text.Trim() == heading)?.X;
            return At("Description") is { } d && At("Ordered") is { } o && At("Supplied") is { } s && At("Price") is { } p && At("Amount") is { } a
                ? new Columns(d - Slack, o - Slack, s - Slack, p - Slack, a - Slack)
                : null;
        }

        public Column Of(PdfItem item) =>
            item.X >= Amount ? Column.Amount
            : item.X >= Price ? Column.Price
            : item.X >= Supplied ? Column.Supplied
            : item.X >= Ordered ? Column.Ordered
            : item.X >= Description ? Column.Description
            : Column.Line;
    }

    private enum Column { Line, Description, Ordered, Supplied, Price, Amount }

    /// <summary>One table section's rows into lines: rows with numbers are lines, text rows close to one are its description, the rest headings.</summary>
    private static void Lines(List<PdfRow> section, Columns? columns, List<ParsedLine> lines)
    {
        if (section.Count == 0 || columns is null)
            return;

        bool Numbers(PdfRow r) => r.Items.Any(i => columns.Of(i) != Column.Description);
        List<PdfRow> numbered = [.. section.Where(Numbers)];
        Dictionary<PdfRow, List<PdfRow>> wraps = numbered.ToDictionary(r => r, r => new List<PdfRow> { r });
        Dictionary<PdfRow, string> headingBefore = [];
        string? category = null;
        foreach (PdfRow row in section)
        {
            if (Numbers(row))
            {
                if (category is not null)
                    headingBefore[row] = category;
                continue;
            }
            PdfRow? nearest = numbered.MinBy(n => Math.Abs(n.Y - row.Y));
            if (nearest is not null && Math.Abs(nearest.Y - row.Y) <= WrapDistance)
                wraps[nearest].Add(row);
            else
                category = row.Text;
        }

        string? current = null;
        ParsedLine? open = null;   // a variable-weight line still waiting for its "~" row
        foreach (PdfRow row in numbered)
        {
            current = headingBefore.GetValueOrDefault(row, current!);
            string Cell(Column c) => PdfRows.Join(row.Items.Where(i => columns.Of(i) == c));
            string description = Names.Normalise(string.Join(' ', wraps[row].OrderBy(r => r.Y)
                .Select(r => PdfRows.Join(r.Items.Where(i => columns.Of(i) == Column.Description)))));
            string supplied = Cell(Column.Supplied), price = Cell(Column.Price), amount = Cell(Column.Amount);

            if (description.StartsWith('~') || (Cell(Column.Line).Length == 0 && supplied.Contains('@')))
            {
                // The second row of a variable-weight line: what was supplied, at what weight, and the real amount.
                Match weight = WeightShape().Match(supplied);
                if (open is null || !weight.Success || Cents(amount) is not { } paid)
                    throw new ReceiptFormatException($"A weighed line (\"{description}\") is not where it should be.");
                decimal grams = decimal.Parse(weight.Groups["weight"].Value, CultureInfo.InvariantCulture)
                                * (weight.Groups["unit"].Value.Equals("kg", StringComparison.OrdinalIgnoreCase) ? 1000 : 1);
                lines[^1] = open with
                {
                    Supplied    = decimal.Parse(weight.Groups["count"].Value, CultureInfo.InvariantCulture),
                    AmountCents = paid,
                    WeightGrams = grams,
                    PerKgCents  = PerKg(price) ?? open.PerKgCents,
                };
                open = null;
                continue;
            }
            if (open is not null)
                throw new ReceiptFormatException($"The weighed line \"{open.Description}\" has no weight.");

            bool gst = description.StartsWith("* ", StringComparison.Ordinal);
            bool sub = description.StartsWith("(Sub) ", StringComparison.Ordinal);
            string name = Names.Normalise(gst ? description[2..] : sub ? description[6..] : description);
            if (name.Length > ReceiptLineRules.MaxDescriptionLength)
                name = name[..ReceiptLineRules.MaxDescriptionLength].TrimEnd();
            if (name.Length == 0)
                throw new ReceiptFormatException("A line on the invoice has no description.");

            if (PerKg(price) is { } perKg)
            {
                open = new ParsedLine(lines.Count, name, null, current, Quantity(Cell(Column.Ordered), name), null, 0, null, perKg, 0, gst, sub);
                lines.Add(open);
                continue;
            }

            decimal count = Quantity(supplied, name);
            long unit = Cents(price) ?? throw new ReceiptFormatException($"\"{name}\" has no price.");
            long cost = Cents(amount) ?? throw new ReceiptFormatException($"\"{name}\" has no amount.");
            long discount = Math.Max(0, (long)(unit * count) - cost);
            lines.Add(new ParsedLine(lines.Count, name, null, current, count, unit, cost, null, null, count == 0 ? 0 : discount, gst, sub));
        }
        if (open is not null)
            throw new ReceiptFormatException($"The weighed line \"{open.Description}\" has no weight.");
    }

    private static decimal Quantity(string text, string line) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal q) && q >= 0
            ? q
            : throw new ReceiptFormatException($"\"{line}\" has no quantity.");

    /// <summary>"$1.00", "$1,234.56", "-$2.00" as cents; null for anything else.</summary>
    public static long? Cents(string text)
    {
        Match m = MoneyShape().Match(text.Trim());
        if (!m.Success)
            return null;
        long cents = long.Parse(m.Groups["dollars"].Value.Replace(",", ""), CultureInfo.InvariantCulture) * 100
                     + long.Parse(m.Groups["cents"].Value, CultureInfo.InvariantCulture);
        return m.Groups["sign"].Success ? -cents : cents;
    }

    private static long? PerKg(string text) => PerKgShape().Match(text.Trim()) is { Success: true } m ? Cents(m.Groups["money"].Value) : null;
}

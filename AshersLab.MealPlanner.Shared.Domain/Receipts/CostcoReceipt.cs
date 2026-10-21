using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AshersLab.MealPlanner.Shared.Domain.Receipts;

/// <summary>
/// One line of text as OCR read it from the receipt's picture (<c>wwwroot/js/receipt-pdf.js</c> <c>ocr</c>), and where it
/// is: pixels from the picture's top. The review draws that strip of the picture beside the line it became.
/// </summary>
public sealed record OcrLine(string Text, double Top, double Bottom);

/// <summary>The lines as <c>receipt-pdf.js</c> sends them. Source-generated, as <see cref="PdfJson"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<OcrLine>))]
public sealed partial class OcrJson : JsonSerializerContext
{
    public static List<OcrLine> Read(string json) =>
        JsonSerializer.Deserialize(json, Default.ListOcrLine) ?? throw new ReceiptFormatException("Nothing could be read from the picture.");
}

/// <summary>A strip of the receipt's picture: the rows one parsed line was read from.</summary>
public sealed record PictureSpan(double Top, double Bottom);

/// <summary>
/// A Costco receipt as read: the receipt, where each line came from on the picture, and the lines whose amount OCR could not
/// read and was worked out from the quantity and price instead - shown to be checked.
/// </summary>
public sealed record CostcoRead(ParsedReceipt Receipt, IReadOnlyDictionary<int, IReadOnlyList<PictureSpan>> Spans, IReadOnlySet<int> Unsure);

/// <summary>
/// Reads a Costco receipt from the OCR of its picture (docs/modules/receipts/receipts.md). The Costco app's receipt is an
/// image-only PDF: one bitmap, shown across two pages, so the picture is read whole and no line is cut at a page break.
/// </summary>
/// <remarks>
/// Each item is a name, an optional line of pack size and logistics codes, then
/// <c>item-number  qty x  unit-price  amount GST-flag</c> - <c>700101  1x  21.49  21.49 0</c>. Measured on the real receipts,
/// OCR reads item numbers, names, unit prices and totals exactly, and misreads only <b>the amount column</b>, in ways a rule
/// undoes because every amount has two decimals and the flag is one digit:
/// <list type="bullet">
/// <item><b>The GST flag runs into the amount</b>: <c>49.99 1</c> reads <c>49.991</c>.</item>
/// <item><b>The decimal point is lost</b>: <c>6.99 0</c> reads <c>6990</c>.</item>
/// <item>A discount's <c>3.00 - 0</c> reads <c>300-0</c>.</item>
/// </list>
/// A discount line (<c>TPD -BUY 2 SAVE</c>, its amount ending in <c>-</c>) is not a product: it comes off the line before
/// it. The same item number scanned twice is one line. Then the receipt is checked twice - the lines make the total, and
/// their quantities make the items sold - and one that does not add up is not imported until the planner corrects it.
/// </remarks>
public static partial class CostcoReceipt
{
    [GeneratedRegex(@"^(?<code>[0-9OoIl|]{3,8})\s+(?<qty>[0-9Il|]{1,3})\s*[xX×]\s+(?<unit>[0-9OoIl|]+[.,]?[0-9OoIl|]{2})\s+(?<rest>.+)$")]
    private static partial Regex ItemShape();

    [GeneratedRegex(@"^(?<dollars>\d+)[.,]?(?<cents>\d{2})(?<minus>-)?(?<gst>[01])$")]
    private static partial Regex AmountWithFlag();

    [GeneratedRegex(@"^(?<dollars>\d+)[.,](?<cents>\d{2})(?<minus>-)?$")]
    private static partial Regex AmountAlone();

    [GeneratedRegex(@"^(?<dollars>\d+)[.,]?(?<cents>\d{2})$")]
    private static partial Regex Price();

    [GeneratedRegex(@"(?<dollars>\d+)[.,](?<cents>\d{2})\s*$")]
    private static partial Regex TrailingMoney();

    [GeneratedRegex(@"TOTAL\s*\(?\s*INCL", RegexOptions.IgnoreCase)]
    private static partial Regex TotalShape();

    [GeneratedRegex(@"ITEMS\s+SOLD\s*[=:]?\s*(?<n>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ItemsSoldShape();

    [GeneratedRegex(@"(?<d>\d{1,2})/(?<m>\d{1,2})/(?<y>\d{4})\s+(?<h>\d{1,2}):(?<min>\d{2}):(?<s>\d{2})\s*(?<ampm>[AP]M)?\s+(?<txn>\d[\d ]*\d)")]
    private static partial Regex StampShape();

    [GeneratedRegex(@"^(\d+(\.\d+)?(KG|G|ML|L|CT|PK)|\d+X\d+(\.\d+)?(KG|G|ML|L))$", RegexOptions.IgnoreCase)]
    private static partial Regex PackToken();

    [GeneratedRegex(@"member|tax\s+invoice|\babn\b", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderShape();

    [GeneratedRegex(@"^(VISA|MASTERCARD|MASTER|AMEX|EFTPOS|DEBIT|CREDIT|CASH)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PaymentShape();

    /// <summary>Whether the text is a Costco receipt at all: the store's name and its total line.</summary>
    public static bool Recognises(IReadOnlyList<OcrLine> lines) =>
        lines.Any(l => l.Text.Contains("COSTCO", StringComparison.OrdinalIgnoreCase)) && lines.Any(l => TotalShape().IsMatch(l.Text));

    /// <summary>
    /// The receipt's dedupe key, from its date, time and register/transaction numbers - "2026-03-03 17:07:00 100 001 0002 003".
    /// The file's name also encodes it, but a file can be renamed.
    /// </summary>
    public static string ReceiptNumber(DateTime at, string transaction) =>
        $"{at:yyyy-MM-dd HH:mm:ss} {Names.Normalise(transaction)}";

    /// <exception cref="ReceiptFormatException">Not a Costco receipt, or one with no date or total.</exception>
    public static CostcoRead Parse(IReadOnlyList<OcrLine> ocr)
    {
        List<OcrLine> lines = [.. ocr.Select(l => l with { Text = Names.Normalise(l.Text) }).Where(l => l.Text.Any(char.IsLetterOrDigit))];

        List<Raw> raws = [];
        List<OcrLine> pending = [];
        int end = lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            OcrLine line = lines[i];
            if (TotalShape().IsMatch(line.Text))
            {
                end = i;
                break;
            }
            if (HeaderShape().IsMatch(line.Text))
            {
                pending.Clear();
                continue;
            }
            Match item = ItemShape().Match(line.Text);
            if (!item.Success)
            {
                pending.Add(line);
                continue;
            }
            List<OcrLine> text = pending.Count > 2 ? pending[^2..] : [.. pending];
            pending.Clear();
            raws.Add(RawOf(item, text, line));
        }
        if (end == lines.Count)
            throw new ReceiptFormatException("This is not a Costco receipt: it has no total.");

        List<OcrLine> footer = lines[end..];
        long total = Money(TrailingMoney().Match(footer[0].Text)) ?? throw new ReceiptFormatException("The receipt's total could not be read.");
        Match stamp = footer.Select(l => StampShape().Match(l.Text)).FirstOrDefault(m => m.Success)
                      ?? throw new ReceiptFormatException("The receipt's date could not be read.");
        DateTime at = Stamp(stamp);
        int? sold = footer.Select(l => ItemsSoldShape().Match(l.Text)).FirstOrDefault(m => m.Success) is { } s
            ? int.Parse(s.Groups["n"].Value, CultureInfo.InvariantCulture) : null;
        long? paid = footer.Skip(1).TakeWhile(l => !ItemsSoldShape().IsMatch(l.Text))
            .Where(l => PaymentShape().IsMatch(l.Text)).Select(l => Money(TrailingMoney().Match(l.Text))).FirstOrDefault(m => m is not null);

        (List<ParsedLine> parsed, Dictionary<int, IReadOnlyList<PictureSpan>> spans, HashSet<int> unsure) = Merge(raws);
        ParsedReceipt receipt = new(ReceiptNumber(at, stamp.Groups["txn"].Value), DateOnly.FromDateTime(at), parsed, [], total, total, paid, null, sold);
        return new CostcoRead(receipt, spans, unsure);
    }

    /// <summary>One item row as printed, before discounts are taken off and repeats merged.</summary>
    private sealed record Raw(string Code, string Name, decimal Quantity, long Unit, long Amount, bool Gst, bool Discount, bool Unsure, PictureSpan Span);

    private static Raw RawOf(Match item, List<OcrLine> text, OcrLine row)
    {
        string code = Digits(item.Groups["code"].Value);
        decimal quantity = decimal.Parse(Digits(item.Groups["qty"].Value), CultureInfo.InvariantCulture);
        long unit = Money(Price().Match(Digits(item.Groups["unit"].Value))) ?? 0;

        string rest = Digits(item.Groups["rest"].Value.Replace(" ", "").Replace('—', '-').Replace('–', '-'));
        Match amount = AmountWithFlag().Match(rest);
        if (!amount.Success)
            amount = AmountAlone().Match(rest);
        long? cents = amount.Success ? Money(amount) : null;
        bool minus = amount.Success && amount.Groups["minus"].Success;

        string name = text.Count > 0 ? text[0].Text : code;
        bool discount = minus || name.StartsWith("TPD ", StringComparison.OrdinalIgnoreCase);
        // The pack size can be on the second line, among the logistics codes ("8X250G MP8 P256 SL365"): keep it, drop the codes.
        HashSet<string> named = new(name.Split(' '), StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> packs = text.Skip(1).SelectMany(l => l.Text.Split([' ', '/'], StringSplitOptions.RemoveEmptyEntries))
            .Where(t => PackToken().IsMatch(t) && !named.Contains(t));
        string description = Names.Normalise(string.Join(' ', [name, .. packs]));
        if (description.Length > ReceiptLineRules.MaxDescriptionLength)
            description = description[..ReceiptLineRules.MaxDescriptionLength].TrimEnd();

        bool gst = amount.Success && amount.Groups["gst"].Success && amount.Groups["gst"].Value == "1";
        PictureSpan span = new(text.Count > 0 ? text[0].Top : row.Top, row.Bottom);
        return new Raw(code, description, quantity, unit, cents ?? (long)(quantity * unit), gst, discount, cents is null, span);
    }

    /// <summary>
    /// Discounts taken off the item before them, then each item number's rows made one line - in the order each first
    /// appears, positions from 0.
    /// </summary>
    private static (List<ParsedLine>, Dictionary<int, IReadOnlyList<PictureSpan>>, HashSet<int>) Merge(List<Raw> raws)
    {
        List<string> order = [];
        Dictionary<string, List<Raw>> rows = [];
        Dictionary<string, long> discounts = [];
        Dictionary<string, List<PictureSpan>> spans = [];
        Raw? last = null;
        foreach (Raw raw in raws)
        {
            if (raw.Discount)
            {
                // Before any item it has nothing to come off: kept as a line of its own, so the totals still add up.
                if (last is null)
                {
                    Add(raw with { Amount = -raw.Amount });
                    continue;
                }
                discounts[last.Code] = discounts.GetValueOrDefault(last.Code) + raw.Amount;
                spans[last.Code].Add(raw.Span);
                continue;
            }
            Add(raw);
            last = raw;
        }

        void Add(Raw raw)
        {
            if (!rows.TryGetValue(raw.Code, out List<Raw>? list))
            {
                rows[raw.Code] = list = [];
                spans[raw.Code] = [];
                order.Add(raw.Code);
            }
            list.Add(raw);
            spans[raw.Code].Add(raw.Span);
        }

        List<ParsedLine> lines = [];
        Dictionary<int, IReadOnlyList<PictureSpan>> where = [];
        HashSet<int> unsure = [];
        foreach (string code in order)
        {
            List<Raw> same = rows[code];
            long discount = discounts.GetValueOrDefault(code);
            int position = lines.Count;
            lines.Add(new ParsedLine(position, same[0].Name, code, null, same.Sum(r => r.Quantity), same[0].Unit,
                same.Sum(r => r.Amount) - discount, null, null, discount, same.Any(r => r.Gst), false));
            where[position] = spans[code];
            if (same.Any(r => r.Unsure))
                unsure.Add(position);
        }
        return (lines, where, unsure);
    }

    /// <summary>OCR's usual confusions in a column of digits: O for 0, l, I or | for 1.</summary>
    private static string Digits(string text) =>
        text.Replace('O', '0').Replace('o', '0').Replace('l', '1').Replace('I', '1').Replace('|', '1');

    private static long? Money(Match m) =>
        m.Success && long.TryParse(m.Groups["dollars"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long dollars)
                  && dollars <= Prices.Money.MaxCents / 100
            ? dollars * 100 + long.Parse(m.Groups["cents"].Value, CultureInfo.InvariantCulture)
            : null;

    private static DateTime Stamp(Match m)
    {
        int hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture) % 12;
        if (!m.Groups["ampm"].Success)
            hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        else if (m.Groups["ampm"].Value == "PM")
            hour += 12;
        try
        {
            return new DateTime(int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture), hour, int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ReceiptFormatException($"The receipt's date \"{m.Value}\" is not a date.");
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AshersLab.MealPlanner.Shared.Domain.Receipts;

/// <summary>
/// The pages as <c>receipt-pdf.js</c> sends them. Source-generated, like the op fields' JSON, so the WASM app's trimming
/// cannot remove what reflection would need.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<PdfPage>))]
public sealed partial class PdfJson : JsonSerializerContext
{
    public static List<PdfPage> Read(string json) =>
        JsonSerializer.Deserialize(json, Default.ListPdfPage) ?? throw new ReceiptFormatException("The file has no pages.");
}

/// <summary>
/// One run of text on a PDF page as pdf.js reports it (<c>wwwroot/js/receipt-pdf.js</c>): where it starts, in points from
/// the page's top-left corner (<paramref name="Y"/> is the baseline, growing down the page), and how wide it is.
/// </summary>
public sealed record PdfItem(string Text, double X, double Y, double Width);

/// <summary>One page's text runs, in whatever order the PDF holds them - which is not reading order.</summary>
public sealed record PdfPage(IReadOnlyList<PdfItem> Items);

/// <summary>A run of text items on one baseline, left to right.</summary>
public sealed record PdfRow(double Y, IReadOnlyList<PdfItem> Items)
{
    public string Text => PdfRows.Join(Items);
}

/// <summary>
/// Turning a page's loose text runs into rows. <b>Never read a PDF's text in the order it is stored</b>: on one Woolworths
/// invoice the totals' labels come out before the line table and their values after it. Position is the only order.
/// </summary>
public static class PdfRows
{
    /// <summary>Items whose baselines are this close are one row: a description and its numbers sit within a point.</summary>
    public const double SameRow = 2.0;

    /// <summary>
    /// The page's rows, top to bottom. Two traps handled here: <b>bold text is printed twice</b> at the same spot (the
    /// invoice fakes bold by overprinting), so an item repeating another's text and position is dropped; and <b>ligatures
    /// split a word</b> ("chicken breast fi llet") into runs that touch, so runs closer than a space are joined without one.
    /// </summary>
    public static IReadOnlyList<PdfRow> Of(PdfPage page)
    {
        List<PdfItem> items = [];
        foreach (PdfItem item in page.Items.Where(i => !string.IsNullOrWhiteSpace(i.Text)).OrderBy(i => i.Y).ThenBy(i => i.X))
            if (!items.Any(seen => seen.Text == item.Text && Math.Abs(seen.X - item.X) < 1 && Math.Abs(seen.Y - item.Y) < 1))
                items.Add(item);

        List<PdfRow> rows = [];
        List<PdfItem> current = [];
        double y = double.NaN;
        foreach (PdfItem item in items)
        {
            if (current.Count > 0 && item.Y - y > SameRow)
            {
                rows.Add(new PdfRow(y, [.. current.OrderBy(i => i.X)]));
                current = [];
            }
            if (current.Count == 0)
                y = item.Y;
            current.Add(item);
        }
        if (current.Count > 0)
            rows.Add(new PdfRow(y, [.. current.OrderBy(i => i.X)]));
        return rows;
    }

    /// <summary>Runs left to right as one string: a space between runs a gap apart, none between runs that touch.</summary>
    public static string Join(IEnumerable<PdfItem> items)
    {
        System.Text.StringBuilder text = new();
        PdfItem? last = null;
        foreach (PdfItem item in items.OrderBy(i => i.X))
        {
            if (last is not null && item.X - (last.X + last.Width) > 1)
                text.Append(' ');
            text.Append(item.Text);
            last = item;
        }
        return Names.Normalise(text.ToString());
    }
}

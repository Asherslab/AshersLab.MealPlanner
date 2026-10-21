using AshersLab.MealPlanner.Shared.Domain.Receipts;

namespace AshersLab.MealPlanner.Tests.Receipts;

/// <summary>
/// A Woolworths invoice's text runs, written by hand to the layout of the real ones (docs/modules/receipts/receipts.md) -
/// never taken from a real invoice. The positions are the real layout's: the columns, the 12.7 pt row pitch, a wrapped
/// description a few points above and below its numbers. So are its traps: <b>bold printed twice</b> at the same spot,
/// <b>ligatures splitting a word</b> into touching runs, and <b>runs stored out of reading order</b> (<see cref="Pages"/>
/// shuffles them).
/// </summary>
public sealed class SyntheticInvoice
{
    private const double Pitch = 12.7;

    private readonly List<List<PdfItem>> _pages = [[]];
    private double _y;

    public SyntheticInvoice(string orderNumber = "300000001", string date = "24 Jun 2026")
    {
        OrderNumber = orderNumber;
        Date = date;
        PageHeader();
        _y = 157;
        Bold(39, "Supplied");
        TableHeader();
    }

    public string OrderNumber { get; }

    public string Date { get; }

    private List<PdfItem> Page => _pages[^1];

    private static double WidthOf(string text) => text.Length * 4.3;

    private void Text(double x, double y, string text) => Page.Add(new PdfItem(text, x, y, WidthOf(text)));

    /// <summary>Bold: the invoice overprints it, so the same run comes twice.</summary>
    private void Bold(double x, string text)
    {
        Text(x, _y, text);
        Text(x, _y, text);
    }

    /// <summary>A description, split where a ligature falls ("fi", "fl") into runs that touch, as the PDF stores them.</summary>
    private void Description(double x, double y, string text)
    {
        int at = 0;
        foreach (string part in Ligatures(text))
        {
            Text(x + WidthOf(text[..at]), y, part);
            at += part.Length;
        }
    }

    private static IEnumerable<string> Ligatures(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (text[i] == 'f' && text[i + 1] is 'i' or 'l')
            {
                if (i > start)
                    yield return text[start..i];
                yield return text[i..(i + 2)];
                start = i + 2;
                i++;
            }
        }
        if (start < text.Length)
            yield return text[start..];
    }

    private void PageHeader()
    {
        _y = 27;
        Bold(255, "Tax Invoice");
        Text(517, 27, $"Page {_pages.Count}");
        Text(247, 39.7, "ABN 00 000 000 000");
        _y = 53.2;
        Bold(375, $"Invoice/Order Number: {OrderNumber}");
        _y = 62.2;
        Bold(375, "Customer: Sam Example");
        _y = 71.2;
        Bold(375, $"Date: {Date}");
        _y = 80.2;
        Bold(375, "Pickup: 1300 - 1400");
    }

    private void TableHeader()
    {
        _y += 14.3;
        foreach ((double x, string heading) in new[] { (48.0, "Line"), (81.0, "Description"), (350.0, "Ordered"), (404.0, "Supplied"), (467.0, "Price"), (516.0, "Amount") })
            Bold(x, heading);
    }

    public SyntheticInvoice Heading(string category)
    {
        _y += 12;
        Bold(81, category);
        return this;
    }

    /// <summary>A line on one row: "4  Woolworths black beans 420g  1  1  $1.00  $1.00".</summary>
    public SyntheticInvoice Line(int number, string description, int ordered, int supplied, string price, string amount)
    {
        _y += Pitch;
        Text(number < 10 ? 55.2 : 53, _y, number.ToString());
        Description(81, _y + 0.7, description);
        Numbers(ordered, supplied, price, amount);
        return this;
    }

    /// <summary>A long description that wraps: half a few points above the numbers, the rest a few points below.</summary>
    public SyntheticInvoice Wrapped(int number, string above, string below, int ordered, int supplied, string price, string amount)
    {
        _y += Pitch;
        Description(81, _y - 3.8, above);
        Text(number < 10 ? 55.2 : 53, _y, number.ToString());
        Numbers(ordered, supplied, price, amount);
        Description(81, _y + 4.5, below);
        return this;
    }

    private void Numbers(int ordered, int supplied, string price, string amount)
    {
        Text(365.3, _y, ordered.ToString());
        Text(420.3, _y, supplied.ToString());
        Text(468 - (price.Length - 5) * 4.3, _y, price);
        Text(532.7 - (amount.Length - 5) * 4.3, _y, amount);
    }

    /// <summary>
    /// Variable-weight meat: the numbered row (its name wrapped round it, the price per kilo, no amount), then the "~" row
    /// with what was supplied and the real amount.
    /// </summary>
    public SyntheticInvoice Weighed(int number, string name, string range, string perKg, string supplied, string amount)
    {
        _y += Pitch;
        Description(81, _y - 3.8, name + " " + range.Split(' ')[0]);
        Text(53, _y, number.ToString());
        Text(365.3, _y, "1");
        Text(460, _y, perKg);
        Description(81, _y + 4.5, string.Join(' ', range.Split(' ').Skip(1)));
        _y += 21;
        Description(88, _y - 3.8, "~ " + name);
        Text(406, _y, supplied);
        Text(460, _y, perKg);
        Text(532.7 - (amount.Length - 5) * 4.3, _y, amount);
        Description(81, _y + 4.5, range);
        return this;
    }

    /// <summary>The substitutions table: its title, its explanation, and its own column headings.</summary>
    public SyntheticInvoice Substitutions()
    {
        _y += 14;
        Bold(40, "Substitutions");
        _y += 7.5;
        Text(40, _y, "Your personal shopper has hand picked a suitable replacement for the following item(s) as per your request.");
        _y += 7.5;
        Text(40, _y, "than the original item ordered, at no extra cost to you.");
        TableHeader();
        return this;
    }

    /// <summary>The rest of the lines on a new page, under its own header and column headings.</summary>
    public SyntheticInvoice NewPage(bool withColumns = true)
    {
        _pages.Add([]);
        PageHeader();
        _y = 157;
        if (withColumns)
            TableHeader();
        return this;
    }

    public SyntheticInvoice Totals(string subtotal, string bags, string collection, string total, string? refund = null, bool onNewPage = true)
    {
        if (onNewPage)
        {
            _pages.Add([]);
            PageHeader();
            _y = 145;
        }
        void Row(double labelX, string label, string value, string? before = null)
        {
            _y += 11.3;
            if (before is not null)
                Text(217, _y, before);
            Bold(labelX, label);
            Text(532.7 - (value.Length - 5) * 4.3, _y, value);
        }
        Row(443, "Sub Total:", subtotal);
        Row(434, "Paper bags:", bags, "Thank you for working with us towards a greener future.");
        Row(427, "Collection fee", collection);
        Row(433, "Service fees", "$0.00");
        Row(428, "Invoice Total:", total);
        Row(361, "Invoice total includes GST of:", "$0.50");
        Row(428, "Paid Amount:", total);
        if (refund is not null)
            Row(416, "Refund Amount:", refund);
        foreach (List<PdfItem> page in _pages)
        {
            page.Add(new PdfItem("Registered Office: Woolworths Group Limited", 112, 727.5, 200));
            page.Add(new PdfItem("* Indicates GST applicable items", 34, 744, 120));
        }
        return this;
    }

    /// <summary>The pages, each page's runs in a shuffled order - the PDF's own order is not reading order.</summary>
    public List<PdfPage> Pages(int seed = 7)
    {
        Random random = new(seed);
        return [.. _pages.Select(p => new PdfPage([.. p.OrderBy(_ => random.Next())]))];
    }
}

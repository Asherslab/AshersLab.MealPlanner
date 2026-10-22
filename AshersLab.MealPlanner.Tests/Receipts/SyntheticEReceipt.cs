using System.Globalization;
using AshersLab.MealPlanner.Shared.Domain.Receipts;

namespace AshersLab.MealPlanner.Tests.Receipts;

/// <summary>
/// A Woolworths in-store eReceipt's text runs, written by hand to the layout of the real one (docs/modules/receipts/receipts.md)
/// - never taken from a real receipt. The till roll is monospaced: each character 3.6 pt, each printed line a row 7.9 pt
/// below the last, <b>each word its own run</b>, a marker (<c>#</c>, <c>^</c>) one character left of the names, amounts
/// right-aligned. <see cref="Pages"/> shuffles the runs, as a PDF need not store them in reading order.
/// </summary>
public sealed class SyntheticEReceipt
{
    private const double Char = 3.6, Pitch = 7.9, Left = 8, Right = 209.6;

    private readonly List<PdfItem> _items = [];
    private double _y = 54;

    public SyntheticEReceipt()
    {
        Row(Left + Char * 11, "1234 Example Town PH: 07 0000 0000");
        Row(Left + Char * 13, "1 Example St");
        Row(Left + Char * 13, "TAX INVOICE - ABN 00 000 000 000");
        _y += Pitch;
        Row(Left, "Description");
        Words(Right - Char, _y, "$");
    }

    /// <summary>Each word of <paramref name="text"/> as its own run from <paramref name="x"/>, one character apart.</summary>
    private void Words(double x, double y, string text)
    {
        foreach (string word in text.Split(' '))
        {
            if (word.Length > 0)
                _items.Add(new PdfItem(word, x, y, word.Length * Char));
            x += (word.Length + 1) * Char;
        }
    }

    private void Row(double x, string text, string? amount = null)
    {
        _y += Pitch;
        Words(x, _y, text);
        if (amount is not null)
            Words(Right - amount.Length * Char, _y, amount);
    }

    /// <summary>A name, a marker in front of it or a space where one would be.</summary>
    private void Name(string name, char? marker, string? amount = null) =>
        Row(marker is null ? Left + Char : Left, marker is { } m ? m + name : name, amount);

    /// <summary>"Milk 2L  3.40": an item and its amount on one row.</summary>
    public SyntheticEReceipt Item(string name, string amount, char? marker = null)
    {
        Name(name, marker, amount);
        return this;
    }

    /// <summary>The name alone, then "Qty 2 @ $9.00 each  18.00".</summary>
    public SyntheticEReceipt Quantity(string name, int count, string each, string amount, char? marker = null)
    {
        Name(name, marker);
        Row(Left + Char, $"Qty {count} @ {each} each", amount);
        return this;
    }

    /// <summary>The name alone, then "1.250 kg NET @ $4.00/kg  5.00".</summary>
    public SyntheticEReceipt Weighed(string name, string kilos, string perKg, string amount, char? marker = null)
    {
        Name(name, marker);
        Row(Left + Char, $"{kilos} kg NET @ {perKg}/kg", amount);
        return this;
    }

    /// <summary>"COLA OFFER  -4.00": off the item before it.</summary>
    public SyntheticEReceipt Offer(string text, string amount)
    {
        Row(Left + Char, text, amount);
        return this;
    }

    /// <summary>A row of text with no amount: "^Promotional Price" under the items.</summary>
    public SyntheticEReceipt Text(string text)
    {
        Row(Left, text);
        return this;
    }

    /// <summary>A gap of one printed line.</summary>
    public SyntheticEReceipt Blank()
    {
        _y += Pitch;
        return this;
    }

    /// <summary>"12 SUBTOTAL $64.20" then "TOTAL $64.20", the payment block, GST and the footer.</summary>
    public SyntheticEReceipt Totals(int? count, string subtotal, string total, (string Label, string Amount)[]? between = null,
                                    string date = "12/03/2026", string time = "10:41", string pos = "004", string trans = "5108",
                                    string? store = "1234")
    {
        Row(Left + Char * 4, count is { } c ? $"{c} SUBTOTAL" : "SUBTOTAL", subtotal);
        foreach ((string label, string amount) in between ?? [])
            Row(Left + Char * 7, label, amount);
        Row(Left + Char * 7, "TOTAL", total);
        _y += Pitch;
        Row(Left + Char * 15, "-------------------------");
        if (store is not null)
            Row(Left + Char * 15, $"WOOLWORTHS {store}");
        DateTime when = DateTime.ParseExact($"{date} {time}", "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        Row(Left + Char * 16, $"{when:dd/MM/yy} {time} 00{trans}");
        Row(Left + Char * 16, "PURCHASE", total);
        Row(Left + Char * 16, "TOTAL", total);
        Row(Left + Char * 16, "APPROVED 00");
        Row(Left + Char * 7, "X-0000", total);
        Row(Left + Char * 7, "Change", "$0.00");
        _y += Pitch;
        Row(Left, "#Taxable Items");
        Row(Left, "TOTAL includes GST", "$0.50");
        Row(Left + Char * 19, "You saved $1.00");
        _y += Pitch * 4;
        Row(Left + Char * 7, $"POS {pos} TRANS {trans} {time} {date}");
        Row(Left + Char * 17, "WOOLWORTHS GROUP LIMITED");
        return this;
    }

    /// <summary>The one tall page, its runs in a shuffled order.</summary>
    public List<PdfPage> Pages(int seed = 7)
    {
        Random random = new(seed);
        return [new PdfPage([.. _items.OrderBy(_ => random.Next())])];
    }
}

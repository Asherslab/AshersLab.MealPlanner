using AshersLab.MealPlanner.Shared.Domain.Receipts;

namespace AshersLab.MealPlanner.Tests.Receipts;

/// <summary>
/// Reading a Costco receipt from the OCR of its picture: synthetic receipts, every item and number invented, laid out as the
/// real ones and carrying the misreads measured on them - the GST flag run into the amount ("49.991"), the decimal point
/// lost ("6990"), a discount's "3.00 - 0" read "300-0" - plus the logo's noise above the header.
/// </summary>
public class CostcoReceiptTests
{
    private const int Pitch = 36;

    /// <summary>OCR lines as tesseract.js gives them: text, and a strip of the picture each.</summary>
    private static List<OcrLine> Ocr(params string[] text) =>
        [.. text.Select((t, i) => new OcrLine(t, i * Pitch, i * Pitch + 30))];

    private static readonly string[] Header =
    [
        "=——=WHOLESALE", "|", "Costco Wholesale Australia Pty Ltd", "1 Example Street", "Exampleton", "QLD 4000",
        "ABN No: 00 000 000 000", "TAX INVOICE (S$)", "Member 00000000000",
    ];

    internal static string[] Footer(string total, int sold, string stamp = "03/03/2026 05:07:00 PM 100 001 0002 003") =>
    [
        $"**** TOTAL (INCL GST)      {total}", "**** TOTAL (EXCL GST)      0.00", "GST Amount      0.00", $"VISA CREDIT     {total}",
        "GST     Code      GST", "0     0.0000 %      0.00", "1     110.0000 %      0.00", $"TOTAL NUMBER OF ITEMS SOLD = {sold}", stamp,
        "COSTCO WHOLESALE AUSTRALIA", "ABN : 00 000 000 000", $"Items Sold: {sold}",
    ];

    /// <summary>Mince, apples, two loaves less a buy-two discount, nappies (GST) - as OCR reads them, traps and all.</summary>
    internal static List<OcrLine> Receipt(string total = "91.45", int sold = 5, string stamp = "03/03/2026 05:07:00 PM 100 001 0002 003") => Ocr(
    [
        .. Header,
        "GRASSFED BEEF MINCE", "6X500G MP6 P200 SL300", "700101      1x      21.49      21.490",
        "PINK LADY APPLES 2KG", "SL12", "700202       1x      8.99       8.99 0",
        "SOURDOUGH LOAF 800G", "SL4", "700303      1x      6.99      6990",
        "SOURDOUGH LOAF 800G", "SL4", "700303      1x      6.99      6.99 0",
        "TPD -BUY 2 SAVE", "700909      1x      3.00      300-0",
        "NAPPIES SIZE 3 120CT", "P40", "700404      1x      49.99      49.991",
        .. Footer(total, sold, stamp),
    ]);

    [Fact]
    public void Reads_the_items_the_total_the_date_and_the_items_sold()
    {
        CostcoRead read = CostcoReceipt.Parse(Receipt());
        ParsedReceipt receipt = read.Receipt;

        Assert.Equal(
        [
            // The pack size on the second line joins the name; the logistics codes do not.
            (0, "GRASSFED BEEF MINCE 6X500G", "700101", 1m, 2149L, 2149L, 0L, false),
            (1, "PINK LADY APPLES 2KG", "700202", 1m, 899L, 899L, 0L, false),
            // Scanned twice: one line of two. The discount after it comes off it.
            (2, "SOURDOUGH LOAF 800G", "700303", 2m, 699L, 1098L, 300L, false),
            (3, "NAPPIES SIZE 3 120CT", "700404", 1m, 4999L, 4999L, 0L, true),
        ], receipt.Lines.Select(l => (l.Position, l.Description, l.StoreCode!, l.Supplied, l.UnitPriceCents!.Value, l.AmountCents, l.DiscountCents, l.Gst)));
        Assert.Equal((9145L, 9145L, 9145L, 5), (receipt.TotalCents, receipt.SubtotalCents, receipt.PaidCents!.Value, receipt.ItemsSold!.Value));
        Assert.Empty(receipt.Fees);
        Assert.Equal(new DateOnly(2026, 3, 3), receipt.PurchasedOn);
        Assert.Equal("2026-03-03 17:07:00 100 001 0002 003", receipt.OrderNumber);
        Assert.Null(receipt.TotalsProblem());
        Assert.Empty(read.Unsure);
    }

    [Theory]
    [InlineData("49.991", 4999L, true)]
    [InlineData("49.99 1", 4999L, true)]
    [InlineData("6990", 699L, false)]
    [InlineData("12.340", 1234L, false)]
    [InlineData("10450", 1045L, false)]
    [InlineData("21.49", 2149L, false)]
    [InlineData("1O.49 0", 1049L, false)]
    public void An_amount_is_read_however_OCR_ran_it_into_the_GST_flag(string printed, long cents, bool gst)
    {
        long unit = cents;
        CostcoRead read = CostcoReceipt.Parse(Ocr([.. Header, "THING", $"123456   1x   {unit / 100}.{unit % 100:00}   {printed}", .. Footer($"{cents / 100}.{cents % 100:00}", 1)]));
        ParsedLine line = read.Receipt.Lines.Single();
        Assert.Equal((cents, gst), (line.AmountCents, line.Gst));
        // Read, not worked out from the quantity and price - which at one pack would give the same figure.
        Assert.Empty(read.Unsure);
    }

    [Fact]
    public void An_item_row_OCR_ran_together_is_still_an_item()
    {
        List<OcrLine> ocr = Receipt();
        int apples = ocr.FindIndex(l => l.Text.StartsWith("700202"));
        ocr[apples] = ocr[apples] with { Text = "700202 1x8.99 8.99 0" };
        ParsedReceipt receipt = CostcoReceipt.Parse(ocr).Receipt;
        Assert.Equal(("PINK LADY APPLES 2KG", "700202", 899L), (receipt.Lines[1].Description, receipt.Lines[1].StoreCode, receipt.Lines[1].AmountCents));
        Assert.Null(receipt.TotalsProblem());

        // A 0 in the quantity read as O.
        ParsedLine ten = CostcoReceipt.Parse(Ocr([.. Header, "THING", "123456   1Ox   0.50   5.00 0", .. Footer("5.00", 10)])).Receipt.Lines.Single();
        Assert.Equal((10m, 500L), (ten.Supplied, ten.AmountCents));
    }

    [Fact]
    public void A_discount_is_known_by_its_minus_or_by_its_name()
    {
        // The minus alone: OCR misread the name.
        List<OcrLine> ocr = Receipt();
        int tpd = ocr.FindIndex(l => l.Text.StartsWith("TPD"));
        ocr[tpd] = ocr[tpd] with { Text = "T?D -8UY 2 SAVE" };
        Assert.Equal(300, CostcoReceipt.Parse(ocr).Receipt.Lines[2].DiscountCents);

        // The name alone: OCR lost the minus.
        ocr = Receipt();
        int amount = ocr.FindIndex(l => l.Text.StartsWith("700909"));
        ocr[amount] = ocr[amount] with { Text = "700909      1x      3.00      3.000" };
        ParsedReceipt receipt = CostcoReceipt.Parse(ocr).Receipt;
        Assert.Equal(300, receipt.Lines[2].DiscountCents);
        Assert.Null(receipt.TotalsProblem());
    }

    [Fact]
    public void A_discount_whose_amount_was_worked_out_marks_its_item_to_be_checked()
    {
        List<OcrLine> ocr = Receipt();
        int amount = ocr.FindIndex(l => l.Text.StartsWith("700909"));
        ocr[amount] = ocr[amount] with { Text = "700909      1x      3.00      3.g?" };
        CostcoRead read = CostcoReceipt.Parse(ocr);
        Assert.Equal(300, read.Receipt.Lines[2].DiscountCents);
        Assert.Equal([2], read.Unsure);
    }

    [Fact]
    public void A_discount_before_any_item_is_a_line_of_its_own_that_adds_up()
    {
        ParsedReceipt receipt = CostcoReceipt.Parse(Ocr(
        [
            .. Header, "TPD -BUY 2 SAVE", "700909      1x      3.00      300-0", "THING", "123456   1x   4.00   4.00 0", .. Footer("1.00", 1),
        ])).Receipt;
        ParsedLine discount = receipt.Lines[0];
        // No packs, all discount: nothing came home, nothing counted as sold, and the receipt imports.
        Assert.Equal((0m, -300L, 300L), (discount.Supplied, discount.AmountCents, discount.DiscountCents));
        Assert.Null(receipt.TotalsProblem());
    }

    [Fact]
    public void A_total_OCR_lost_the_point_of_is_read_and_one_it_could_not_read_is_left_to_correct()
    {
        Assert.Equal(9145, CostcoReceipt.Parse(Receipt(total: "9145")).Receipt.TotalCents);

        // Unreadable: not refused - the review opens on a receipt that does not add up, its total there to correct.
        ParsedReceipt unread = CostcoReceipt.Parse(Receipt(total: "g?.4S")).Receipt;
        Assert.Equal((0L, 4), (unread.TotalCents, unread.Lines.Count));
        Assert.Equal("The lines add up to $91.45, but the receipt's total is $0.00.", unread.TotalsProblem());
        Assert.Null(ReceiptCorrection.Receipt(unread, "91.45", "5", null, out _)!.TotalsProblem());
    }

    [Fact]
    public void Each_line_knows_where_it_is_on_the_picture_its_discount_too()
    {
        CostcoRead read = CostcoReceipt.Parse(Receipt());
        // The mince: its name (OCR line 9) to its numbers (line 11).
        Assert.Equal([new PictureSpan(9 * Pitch, 11 * Pitch + 30)], read.Spans[0]);
        // The loaves: both scans and the discount line.
        Assert.Equal([new PictureSpan(15 * Pitch, 17 * Pitch + 30), new PictureSpan(18 * Pitch, 20 * Pitch + 30), new PictureSpan(21 * Pitch, 22 * Pitch + 30)],
            read.Spans[2]);
    }

    [Fact]
    public void A_misread_amount_is_caught_by_the_lines_own_sums_and_a_wrong_total_by_the_total()
    {
        // OCR read the apples' amount as 8.39: the line disagrees with its quantity and price.
        List<OcrLine> ocr = Receipt();
        int apples = ocr.FindIndex(l => l.Text.StartsWith("700202"));
        ocr[apples] = ocr[apples] with { Text = "700202       1x       8.99       8.39 0" };
        ParsedReceipt misread = CostcoReceipt.Parse(ocr).Receipt;
        Assert.Equal("\"PINK LADY APPLES 2KG\": 1 × $8.99 is $8.99, not $8.39.", misread.TotalsProblem());

        // The planner corrects it in the review: it adds up again.
        Assert.Null(misread.WithLine(misread.Lines[1] with { AmountCents = 899 }).TotalsProblem());

        Assert.Equal("The lines add up to $91.45, but the receipt's total is $91.54.", CostcoReceipt.Parse(Receipt(total: "91.54")).Receipt.TotalsProblem());
        Assert.Equal("The lines are 5 items, but the receipt says 6 were sold.", CostcoReceipt.Parse(Receipt(sold: 6)).Receipt.TotalsProblem());
    }

    [Fact]
    public void An_amount_OCR_could_not_read_is_worked_out_and_marked_unsure()
    {
        List<OcrLine> ocr = Receipt();
        int apples = ocr.FindIndex(l => l.Text.StartsWith("700202"));
        ocr[apples] = ocr[apples] with { Text = "700202       1x       8.99       8.g?" };
        CostcoRead read = CostcoReceipt.Parse(ocr);
        Assert.Equal(899, read.Receipt.Lines[1].AmountCents);
        Assert.Equal([1], read.Unsure);
    }

    [Fact]
    public void Letters_OCR_puts_in_a_column_of_digits_are_the_digits()
    {
        ParsedLine line = CostcoReceipt.Parse(Ocr([.. Header, "THING", "7OO1Ol   1x   4.5O   4.50 0", .. Footer("4.50", 1)])).Receipt.Lines.Single();
        Assert.Equal(("700101", 450L, 450L), (line.StoreCode, line.UnitPriceCents!.Value, line.AmountCents));
    }

    [Fact]
    public void A_morning_receipt_and_its_number()
    {
        List<OcrLine> ocr = Receipt();
        ParsedReceipt receipt = CostcoReceipt.Parse([.. ocr.Take(ocr.Count - 4), new OcrLine("12/11/2026 09:05:30 AM 100 001 0099 003", 0, 0)]).Receipt;
        Assert.Equal(("2026-11-12 09:05:30 100 001 0099 003", new DateOnly(2026, 11, 12)), (receipt.OrderNumber, receipt.PurchasedOn));
    }

    [Fact]
    public void Not_a_Costco_receipt_or_one_without_a_date_is_refused()
    {
        Assert.False(CostcoReceipt.Recognises(Ocr("Some other shop", "TOTAL 4.00")));
        Assert.True(CostcoReceipt.Recognises(Receipt()));
        Assert.Equal("This is not a Costco receipt: it has no total.",
            Assert.Throws<ReceiptFormatException>(() => CostcoReceipt.Parse(Ocr([.. Header, "THING", "123456  1x  4.00  4.00 0"]))).Message);
        Assert.Equal("The receipt's date could not be read.",
            Assert.Throws<ReceiptFormatException>(() => CostcoReceipt.Parse(Ocr([.. Header, "THING", "123456  1x  4.00  4.00 0", "**** TOTAL (INCL GST) 4.00"]))).Message);
    }
}

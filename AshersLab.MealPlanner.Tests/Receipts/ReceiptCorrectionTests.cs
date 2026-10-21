using AshersLab.MealPlanner.Shared.Domain.Receipts;

namespace AshersLab.MealPlanner.Tests.Receipts;

/// <summary>Correcting what OCR misread in the review, and the corrected receipt checked again.</summary>
public class ReceiptCorrectionTests
{
    private static ParsedReceipt Misread()
    {
        List<OcrLine> ocr = CostcoReceiptTests.Receipt();
        int apples = ocr.FindIndex(l => l.Text.StartsWith("700202"));
        ocr[apples] = ocr[apples] with { Text = "700202       1x       8.99       8.39 0" };
        return CostcoReceipt.Parse(ocr).Receipt;
    }

    [Fact]
    public void A_corrected_amount_makes_the_receipt_add_up_and_a_wrong_correction_does_not()
    {
        ParsedReceipt receipt = Misread();
        ParsedLine apples = receipt.Lines[1];
        Assert.NotNull(receipt.TotalsProblem());

        ParsedLine fixedLine = ReceiptCorrection.Line(apples, "PINK LADY APPLES 2KG", "700202", "1", "8.99", "8.99", out string? problem)!;
        Assert.Null(problem);
        Assert.Null(receipt.WithLine(fixedLine).TotalsProblem());

        // An amount under the price is not taken for a discount: the line still disagrees with itself.
        ParsedLine under = ReceiptCorrection.Line(apples, "PINK LADY APPLES 2KG", "700202", "1", "8.99", "5.00", out _)!;
        Assert.Equal("\"PINK LADY APPLES 2KG\": 1 × $8.99 is $8.99, not $5.00.", receipt.WithLine(under).TotalsProblem());
    }

    [Fact]
    public void A_discounted_line_keeps_its_discount()
    {
        ParsedReceipt receipt = CostcoReceipt.Parse(CostcoReceiptTests.Receipt()).Receipt;
        ParsedLine loaves = ReceiptCorrection.Line(receipt.Lines[2], "SOURDOUGH LOAF 800G", "700303", "2", "6.99", "10.98", out _)!;
        Assert.Equal((300L, 1098L), (loaves.DiscountCents, loaves.AmountCents));
        Assert.Null(receipt.WithLine(loaves).TotalsProblem());
    }

    [Theory]
    [InlineData("", "1", "8.99", "8.99", "The line needs a description.")]
    [InlineData("APPLES", "one", "8.99", "8.99", "Type the quantity as a number, like 2.")]
    [InlineData("APPLES", "1", "8,99", "8.99", "Type a price like 24.99.")]
    [InlineData("APPLES", "1", "8.99", "", "Enter the price.")]
    public void A_field_that_cannot_be_read_is_refused_saying_why(string description, string quantity, string price, string amount, string expected)
    {
        Assert.Null(ReceiptCorrection.Line(Misread().Lines[1], description, null, quantity, price, amount, out string? problem));
        Assert.Equal(expected, problem);
    }

    [Fact]
    public void The_total_items_sold_and_date_can_be_corrected_and_are_checked_again()
    {
        ParsedReceipt read = CostcoReceipt.Parse(CostcoReceiptTests.Receipt(total: "91.54", sold: 6)).Receipt;
        Assert.NotNull(read.TotalsProblem());

        ParsedReceipt corrected = ReceiptCorrection.Receipt(read, "91.45", "5", new DateOnly(2026, 3, 4), out string? problem)!;
        Assert.Null(problem);
        Assert.Equal((9145L, 9145L, 5, new DateOnly(2026, 3, 4)), (corrected.TotalCents, corrected.SubtotalCents, corrected.ItemsSold!.Value, corrected.PurchasedOn));
        Assert.Null(corrected.TotalsProblem());

        Assert.Null(ReceiptCorrection.Receipt(read, "91.45", "five", null, out problem));
        Assert.Equal("Type the items sold as a whole number.", problem);
    }
}

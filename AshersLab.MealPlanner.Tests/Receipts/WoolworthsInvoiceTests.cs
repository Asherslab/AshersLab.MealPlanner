using AshersLab.MealPlanner.Shared.Domain.Receipts;

namespace AshersLab.MealPlanner.Tests.Receipts;

/// <summary>
/// Reading a Woolworths invoice from its text runs, on synthetic invoices laid out like the real ones: lines and their
/// categories, quantities and prices, descriptions wrapped round their numbers, weighed meat, substitutions, fees and
/// totals - and refusing what does not add up rather than importing it.
/// </summary>
public class WoolworthsInvoiceTests
{
    private static SyntheticInvoice Ordinary() => new SyntheticInvoice("300000001", "9 Jul 2026")
        .Heading("Bakery")
        .Line(2, "Woolworths wholemeal soft sandwich bread 700g", 1, 1, "$2.80", "$2.80")
        .Heading("Canned & Packet Food")
        .Line(4, "Woolworths black beans pdm 420g", 1, 1, "$1.00", "$1.00")
        .Line(10, "Twinings english breakfast tea bags pdm 50 pack", 2, 2, "$5.00", "$10.00")
        .Heading("Household Cleaning")
        .Line(15, "* Earth choice floor cleaner wooden surface 750ml", 1, 1, "$5.00", "$5.00")
        .Totals("$18.80", "$2.00", "$0.00", "$20.80");

    [Fact]
    public void Reads_the_header_the_lines_under_their_headings_and_the_totals()
    {
        ParsedReceipt receipt = WoolworthsInvoice.Parse(Ordinary().Pages());

        Assert.Equal(("300000001", new DateOnly(2026, 7, 9)), (receipt.OrderNumber, receipt.PurchasedOn));
        Assert.Equal(
        [
            (0, "Woolworths wholemeal soft sandwich bread 700g", "Bakery", 1m, 280L, 280L, false),
            (1, "Woolworths black beans pdm 420g", "Canned & Packet Food", 1m, 100L, 100L, false),
            (2, "Twinings english breakfast tea bags pdm 50 pack", "Canned & Packet Food", 2m, 500L, 1000L, false),
            // The GST marker is stripped from the description and kept as a hint.
            (3, "Earth choice floor cleaner wooden surface 750ml", "Household Cleaning", 1m, 500L, 500L, true),
        ], receipt.Lines.Select(l => (l.Position, l.Description, l.Category, l.Supplied, l.UnitPriceCents!.Value, l.AmountCents, l.Gst)));
        Assert.All(receipt.Lines, l => Assert.Equal((0L, false, (decimal?)null), (l.DiscountCents, l.Substitute, l.WeightGrams)));

        Assert.Equal((1880L, 2080L, 2080L, (long?)null), (receipt.SubtotalCents, receipt.TotalCents, receipt.PaidCents, receipt.RefundCents));
        Assert.Equal([("Paper bags", 200L), ("Collection fee", 0L), ("Service fees", 0L)], receipt.Fees.Select(f => (f.Name, f.AmountCents)));
        Assert.Null(receipt.TotalsProblem());
    }

    [Fact]
    public void Reading_order_is_position_never_the_order_the_PDF_stores_its_text_in()
    {
        ParsedReceipt stored = WoolworthsInvoice.Parse(Ordinary().Pages(seed: 1));
        foreach (int seed in new[] { 2, 3, 42, 1000 })
        {
            ParsedReceipt other = WoolworthsInvoice.Parse(Ordinary().Pages(seed));
            Assert.Equal(stored.Lines, other.Lines);
            Assert.Equal(stored.Fees, other.Fees);
        }
    }

    [Fact]
    public void Bold_printed_twice_is_read_once_and_a_ligature_split_word_is_one_word()
    {
        ParsedReceipt receipt = WoolworthsInvoice.Parse(new SyntheticInvoice()
            .Heading("Dairy")
            .Line(1, "Bega cheddar cheese pdm 250g", 1, 1, "$4.00", "$4.00")
            .Line(2, "Chicken breast fillet skinless 1kg", 1, 1, "$9.00", "$9.00")
            .Line(3, "Muscle protein shake strawberry flavour 300g", 1, 1, "$7.00", "$7.00")
            .Totals("$20.00", "$0.00", "$0.00", "$20.00").Pages());

        // One heading, not two; three lines, not six.
        Assert.Equal(["Dairy"], receipt.Lines.Select(l => l.Category).Distinct());
        Assert.Equal(["Bega cheddar cheese pdm 250g", "Chicken breast fillet skinless 1kg", "Muscle protein shake strawberry flavour 300g"],
            receipt.Lines.Select(l => l.Description));
    }

    [Fact]
    public void A_description_wrapped_above_and_below_its_numbers_is_the_lines_and_a_heading_close_by_is_not()
    {
        ParsedReceipt receipt = WoolworthsInvoice.Parse(new SyntheticInvoice()
            .Heading("Cooking, Seasoning & Gravy")
            .Wrapped(6, "Mingle crave this chilli con carne", "recipe base 30g", 1, 1, "$2.60", "$2.60")
            .Line(7, "Mingle honey garlic bbq seasoning 30g", 1, 1, "$3.00", "$3.00")
            .Heading("Frozen Food")
            .Wrapped(12, "Essentials frozen cut green", "beans 1kg", 1, 1, "$2.80", "$2.80")
            .Totals("$8.40", "$0.00", "$0.00", "$8.40").Pages());

        Assert.Equal(
        [
            ("Mingle crave this chilli con carne recipe base 30g", "Cooking, Seasoning & Gravy"),
            ("Mingle honey garlic bbq seasoning 30g", "Cooking, Seasoning & Gravy"),
            ("Essentials frozen cut green beans 1kg", "Frozen Food"),
        ], receipt.Lines.Select(l => (l.Description, l.Category)));
    }

    [Fact]
    public void Weighed_meat_is_its_numbered_row_and_its_tilde_row_the_weight_the_per_kg_price_and_the_real_amount()
    {
        ParsedReceipt receipt = WoolworthsInvoice.Parse(new SyntheticInvoice()
            .Heading("Meat")
            .Weighed(17, "Woolworths chicken breast fillet skinless small", "500g - 800g", "$13.00/Kg", "1 @ 700g", "$9.10")
            .Line(18, "Leg ham pdm 4 pack", 1, 1, "$6.70", "$6.70")
            .Totals("$15.80", "$0.00", "$0.00", "$15.80", refund: "$1.30").Pages());

        ParsedLine chicken = receipt.Lines[0];
        Assert.Equal("Woolworths chicken breast fillet skinless small 500g - 800g", chicken.Description);
        Assert.Equal((1m, 700m, 1300L, 910L, (long?)null), (chicken.Supplied, chicken.WeightGrams!.Value, chicken.PerKgCents!.Value, chicken.AmountCents, chicken.UnitPriceCents));
        // What this pack was seen at: what it cost, not the per-kilo price.
        Assert.Equal(910, chicken.PackPriceCents);
        Assert.Equal(("Leg ham pdm 4 pack", 670L), (receipt.Lines[1].Description, receipt.Lines[1].AmountCents));
        Assert.Equal(2, receipt.Lines.Count);
        Assert.Equal(130, receipt.RefundCents);
        Assert.Null(receipt.TotalsProblem());
    }

    [Fact]
    public void A_weight_in_kilos_is_grams()
    {
        ParsedReceipt receipt = WoolworthsInvoice.Parse(new SyntheticInvoice()
            .Heading("Meat")
            .Weighed(3, "Beef rump steak", "1kg - 1.5kg", "$30.00/Kg", "1 @ 1.234kg", "$37.02")
            .Totals("$37.02", "$0.00", "$0.00", "$37.02").Pages());
        Assert.Equal(1234m, receipt.Lines.Single().WeightGrams);
    }

    [Fact]
    public void A_substitute_is_its_shelf_price_charged_at_the_originals_and_the_original_brought_nothing_home()
    {
        ParsedReceipt receipt = WoolworthsInvoice.Parse(new SyntheticInvoice()
            .Heading("Drinks")
            .Line(1, "Oat milk long life 1l", 1, 1, "$2.50", "$2.50")
            .Substitutions()
            .Heading("Drinks")
            .Line(8, "Vitasoy oat milk uht 1l", 3, 0, "$2.50", "$0.00")
            .Line(8, "(Sub) Sanitarium so good oat milk uht 1L", 0, 3, "$3.00", "$7.50")
            .Totals("$10.00", "$2.00", "$0.00", "$12.00").Pages());

        Assert.Equal(3, receipt.Lines.Count);
        ParsedLine original = receipt.Lines[1], sub = receipt.Lines[2];
        Assert.Equal((true, 0L, false), (original.NotSupplied, original.AmountCents, original.Substitute));
        Assert.Equal(("Sanitarium so good oat milk uht 1L", true, 3m, 300L, 750L, 150L),
            (sub.Description, sub.Substitute, sub.Supplied, sub.UnitPriceCents!.Value, sub.AmountCents, sub.DiscountCents));
        Assert.Equal(300, sub.PackPriceCents);
        Assert.Null(receipt.TotalsProblem());
    }

    [Fact]
    public void Lines_on_a_second_page_under_their_headings_are_read_and_its_page_header_is_not_a_line()
    {
        ParsedReceipt receipt = WoolworthsInvoice.Parse(new SyntheticInvoice()
            .Heading("Bakery")
            .Line(1, "Sourdough loaf 650g", 1, 1, "$5.00", "$5.00")
            .NewPage()
            .Heading("Dairy")
            .Line(2, "Greek yoghurt 1kg", 1, 1, "$6.00", "$6.00")
            .Totals("$11.00", "$0.00", "$0.00", "$11.00", onNewPage: false).Pages());

        Assert.Equal(["Sourdough loaf 650g", "Greek yoghurt 1kg"], receipt.Lines.Select(l => l.Description));
        Assert.Null(receipt.TotalsProblem());
    }

    [Fact]
    public void Lines_this_reader_cannot_place_make_the_totals_fail_never_a_quietly_short_receipt()
    {
        // A page of lines with no column headings: its lines cannot be placed, so they are not read - and the receipt says so.
        ParsedReceipt receipt = WoolworthsInvoice.Parse(new SyntheticInvoice()
            .Line(1, "Sourdough loaf 650g", 1, 1, "$5.00", "$5.00")
            .NewPage(withColumns: false)
            .Line(2, "Greek yoghurt 1kg", 1, 1, "$6.00", "$6.00")
            .Totals("$11.00", "$0.00", "$0.00", "$11.00", onNewPage: false).Pages());

        Assert.Equal("The lines add up to $5.00, but the receipt's sub total is $11.00.", receipt.TotalsProblem());
    }

    [Fact]
    public void A_total_that_is_not_the_sub_total_and_fees_is_refused_with_both_figures()
    {
        ParsedReceipt receipt = WoolworthsInvoice.Parse(new SyntheticInvoice()
            .Line(1, "Sourdough loaf 650g", 1, 1, "$5.00", "$5.00")
            .Totals("$5.00", "$2.00", "$0.00", "$8.00").Pages());
        Assert.Equal("The sub total and fees make $7.00, but the receipt's total is $8.00.", receipt.TotalsProblem());
    }

    [Fact]
    public void Something_that_is_not_a_Woolworths_invoice_is_refused()
    {
        List<PdfPage> letter = [new PdfPage([new PdfItem("Dear customer", 40, 100, 60), new PdfItem("Thank you", 40, 120, 40)])];
        Assert.False(WoolworthsInvoice.Recognises(letter));
        Assert.Equal("This is not a Woolworths invoice: it has no order number.",
            Assert.Throws<ReceiptFormatException>(() => WoolworthsInvoice.Parse(letter)).Message);
        Assert.True(WoolworthsInvoice.Recognises(Ordinary().Pages()));
    }

    [Theory]
    [InlineData("$1.00", 100L)]
    [InlineData("$0.05", 5L)]
    [InlineData("$1,234.56", 123456L)]
    [InlineData("-$2.00", -200L)]
    [InlineData("$13.00/Kg", null)]
    [InlineData("$1.5", null)]
    [InlineData("1.00", null)]
    [InlineData("$1,23.00", null)]
    public void Money_as_printed_is_whole_cents(string printed, long? cents) =>
        Assert.Equal(cents, WoolworthsInvoice.Cents(printed));
}

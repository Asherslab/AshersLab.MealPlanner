using AshersLab.MealPlanner.Shared.Domain.Receipts;

namespace AshersLab.MealPlanner.Tests.Receipts;

/// <summary>
/// Reading a Woolworths in-store eReceipt from its text runs, on synthetic receipts laid out like the real one: items on one
/// row, items of more than one and weighed items on two, an offer off the item before it, the items sold, the till's stamp as
/// the receipt's number - and refusing what does not add up rather than importing it.
/// </summary>
public class WoolworthsEReceiptTests
{
    private static SyntheticEReceipt Ordinary() => new SyntheticEReceipt()
        .Weighed("Bananas Loose", "1.250", "$4.00", "5.00")
        .Quantity("Cola Soft Drink 330ml 4Pk", 2, "$9.00", "18.00", '#')
        .Offer("COLA MULTIBUY OFFER", "-4.00")
        .Item("WW Full Cream Milk 2L", "3.40")
        .Quantity("Spaghetti 500g", 2, "$1.20", "2.40")
        .Item("Peanut Butter Smooth 470g", "4.75", '^')
        .Item("Woolworths Paper Bag", "0.25", '#')
        .Blank()
        .Text("^Promotional Price")
        .Totals(8, "$29.80", "$29.80");

    [Fact]
    public void Reads_the_items_offers_weighed_and_counted_lines_the_totals_and_the_stamp()
    {
        ParsedReceipt receipt = WoolworthsEReceipt.Parse(Ordinary().Pages());

        Assert.Equal(("2026-03-12 10:41 1234 004 5108", new DateOnly(2026, 3, 12)), (receipt.OrderNumber, receipt.PurchasedOn));
        Assert.Equal(
        [
            (0, "Bananas Loose", 1m, (long?)null, 500L, 0L, false),
            // Two at $9.00, the offer's $4.00 off them: the shelf price stays the price.
            (1, "Cola Soft Drink 330ml 4Pk", 2m, 900L, 1400L, 400L, true),
            (2, "WW Full Cream Milk 2L", 1m, 340L, 340L, 0L, false),
            (3, "Spaghetti 500g", 2m, 120L, 240L, 0L, false),
            // A promotional price's marker is stripped; the legend under the items is not a line.
            (4, "Peanut Butter Smooth 470g", 1m, 475L, 475L, 0L, false),
            (5, "Woolworths Paper Bag", 1m, 25L, 25L, 0L, true),
        ], receipt.Lines.Select(l => (l.Position, l.Description, l.Supplied, l.UnitPriceCents, l.AmountCents, l.DiscountCents, l.Gst)));
        Assert.Equal((1250m, 400L), (receipt.Lines[0].WeightGrams!.Value, receipt.Lines[0].PerKgCents!.Value));
        Assert.All(receipt.Lines, l => Assert.Null(l.Category));

        Assert.Equal((2980L, 2980L, 8), (receipt.SubtotalCents, receipt.TotalCents, receipt.ItemsSold!.Value));
        Assert.Empty(receipt.Fees);
        Assert.Null(receipt.TotalsProblem());
    }

    [Fact]
    public void Reading_order_is_position_never_the_order_the_PDF_stores_its_text_in()
    {
        ParsedReceipt stored = WoolworthsEReceipt.Parse(Ordinary().Pages(seed: 1));
        foreach (int seed in new[] { 2, 3, 42, 1000 })
            Assert.Equal(stored.Lines, WoolworthsEReceipt.Parse(Ordinary().Pages(seed)).Lines);
    }

    [Fact]
    public void A_weighed_item_counts_as_one_item_sold_and_its_price_is_what_it_cost()
    {
        ParsedReceipt receipt = WoolworthsEReceipt.Parse(new SyntheticEReceipt()
            .Weighed("Apples Red", "0.600", "$6.00", "3.60")
            .Weighed("Mince Beef", "0.85", "$12.00", "10.20", '^')
            .Totals(2, "$13.80", "$13.80").Pages());

        Assert.Equal([(600m, 600L, 360L), (850m, 1200L, 1020L)], receipt.Lines.Select(l => (l.WeightGrams!.Value, l.PerKgCents!.Value, l.PackPriceCents!.Value)));
        Assert.Null(receipt.TotalsProblem());
    }

    [Fact]
    public void A_rewards_discount_between_the_sub_total_and_the_total_is_a_fee()
    {
        ParsedReceipt receipt = WoolworthsEReceipt.Parse(new SyntheticEReceipt()
            .Item("WW Full Cream Milk 2L", "3.40")
            .Totals(1, "$3.40", "$1.40", between: [("Rewards Dollars", "-2.00")]).Pages());

        Assert.Equal([("Rewards Dollars", -200L)], receipt.Fees.Select(f => (f.Name, f.AmountCents)));
        Assert.Null(receipt.TotalsProblem());
    }

    [Fact]
    public void A_second_row_it_does_not_know_is_refused_and_a_wrong_count_fails_the_totals()
    {
        // How many came home is not guessed: it would price the product wrong.
        ReceiptFormatException unknown = Assert.Throws<ReceiptFormatException>(() => WoolworthsEReceipt.Parse(new SyntheticEReceipt()
            .Item("WW Full Cream Milk 2L", "3.40")
            .Text("Odd Thing")
            .Offer("2 for $5.00 pack", "5.00")
            .Totals(3, "$8.40", "$8.40").Pages()));
        Assert.Contains("\"Odd Thing\" is followed by \"2 for $5.00 pack\"", unknown.Message);

        // A name alone with nothing under it is a legend, dropped - an item it really was shows in the count.
        ParsedReceipt miscounted = WoolworthsEReceipt.Parse(new SyntheticEReceipt()
            .Item("WW Full Cream Milk 2L", "3.40")
            .Totals(2, "$3.40", "$3.40").Pages());
        Assert.Equal("The lines are 1 items, but the receipt says 2 were sold.", miscounted.TotalsProblem());
    }

    [Fact]
    public void A_receipt_without_a_count_or_a_store_number_still_reads()
    {
        ParsedReceipt receipt = WoolworthsEReceipt.Parse(new SyntheticEReceipt()
            .Item("WW Full Cream Milk 2L", "3.40")
            .Totals(null, "$3.40", "$3.40", store: null, date: "05/11/2026", time: "09:05", pos: "002", trans: "77").Pages());

        Assert.Equal(("2026-11-05 09:05 002 77", (int?)null), (receipt.OrderNumber, receipt.ItemsSold));
        Assert.Null(receipt.TotalsProblem());
    }

    [Fact]
    public void An_offer_before_any_item_is_refused()
    {
        Assert.Throws<ReceiptFormatException>(() => WoolworthsEReceipt.Parse(new SyntheticEReceipt()
            .Offer("COLA OFFER", "-1.00")
            .Totals(0, "-$1.00", "-$1.00").Pages()));
    }

    [Fact]
    public void It_knows_an_eReceipt_from_an_online_invoice_and_neither_reads_the_other()
    {
        List<PdfPage> eReceipt = Ordinary().Pages();
        List<PdfPage> invoice = new SyntheticInvoice().Line(1, "Woolworths black beans pdm 420g", 1, 1, "$1.00", "$1.00")
            .Totals("$1.00", "$0.00", "$0.00", "$1.00").Pages();

        Assert.True(WoolworthsEReceipt.Recognises(eReceipt));
        Assert.False(WoolworthsInvoice.Recognises(eReceipt));
        Assert.False(WoolworthsEReceipt.Recognises(invoice));
        Assert.True(WoolworthsInvoice.Recognises(invoice));
        Assert.Throws<ReceiptFormatException>(() => WoolworthsEReceipt.Parse(invoice));
    }
}

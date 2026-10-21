using AshersLab.MealPlanner.Shared.Data.Pantry;
using AshersLab.MealPlanner.Shared.Data.Receipts;
using AshersLab.MealPlanner.Shared.Domain.Pantry;
using AshersLab.MealPlanner.Shared.Domain.Receipts;
using AshersLab.MealPlanner.Shared.Domain.Shopping;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Receipts;

/// <summary>A receipt's, a line's and an alias's ops: derived ids, the fields each needs, and what each refuses.</summary>
public class ReceiptRulesTests
{
    private static readonly Guid Woolworths = new("01900000-0000-7000-8000-0000000000c2");
    private static readonly Guid Costco     = new("01900000-0000-7000-8000-0000000000c1");

    [Fact]
    public void A_receipts_id_is_its_store_and_order_number_so_the_same_order_is_the_same_row_everywhere()
    {
        Assert.Equal(ReceiptRules.ReceiptId(Woolworths, "300000001"), ReceiptRules.ReceiptId(Woolworths, " 300000001 "));
        Assert.NotEqual(ReceiptRules.ReceiptId(Woolworths, "300000001"), ReceiptRules.ReceiptId(Woolworths, "300000002"));
        Assert.NotEqual(ReceiptRules.ReceiptId(Woolworths, "300000001"), ReceiptRules.ReceiptId(Costco, "300000001"));

        Guid receipt = ReceiptRules.ReceiptId(Woolworths, "300000001");
        Guid line = ReceiptRules.LineId(receipt, 3);
        Assert.Equal(line, ReceiptRules.LineId(receipt, 3));
        Guid[] all = [receipt, line, ReceiptRules.LineId(receipt, 4), ReceiptRules.PriceId(line), ReceiptRules.StockId(line)];
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    [Fact]
    public void An_alias_is_one_per_description_at_a_store_whatever_its_case_or_spacing()
    {
        Assert.Equal(ProductAliasRules.AliasId(Woolworths, "Black beans 420g"), ProductAliasRules.AliasId(Woolworths, "black  BEANS 420g"));
        Assert.NotEqual(ProductAliasRules.AliasId(Woolworths, "Black beans 420g"), ProductAliasRules.AliasId(Costco, "Black beans 420g"));
    }

    private static IReadOnlyList<OpField> ReceiptCreate() => ReceiptFields.Of(new Receipt
    {
        StoreId = Woolworths, OrderNumber = "300000001", PurchasedOn = new(2026, 10, 24), SubtotalCents = 1880, FeesCents = 200,
        TotalCents = 2080, PaidCents = 2080, ImportedOn = new(2026, 10, 25),
    });

    private static IReadOnlyList<OpField> LineCreate(ReceiptLine? line = null) => ReceiptFields.Of(line ?? new ReceiptLine
    {
        ReceiptId = ReceiptRules.ReceiptId(Woolworths, "300000001"), Position = 0, Description = "Woolworths black beans pdm 420g",
        Quantity = 1, UnitPriceCents = 100, AmountCents = 100, Kind = ReceiptLineKind.Food,
    });

    [Fact]
    public void Every_field_an_import_writes_is_accepted()
    {
        Assert.Null(ReceiptRules.OpProblem(OpKind.Create, ReceiptCreate()));
        Assert.Null(ReceiptLineRules.OpProblem(OpKind.Create, LineCreate()));
        Assert.Null(ProductAliasRules.OpProblem(OpKind.Create, ReceiptFields.Of(new ProductAlias { StoreId = Woolworths, Text = "Black beans", ProductId = Guid.CreateVersion7() })));
        // A discount line is negative; a fee line has no product.
        Assert.Null(ReceiptLineRules.OpProblem(OpKind.Create, LineCreate(new ReceiptLine
        {
            ReceiptId = Guid.CreateVersion7(), Position = 9, Description = "TPD -PICK 'N' MIX", Quantity = 1, AmountCents = -749, Kind = ReceiptLineKind.Food,
        })));
        // Matching later is an update of the product and kind only.
        Assert.Null(ReceiptLineRules.OpProblem(OpKind.Update,
            [new OpField(ReceiptLineRules.ProductField, OpValues.FormatId(Guid.CreateVersion7())), new OpField(ReceiptLineRules.KindField, "Household")]));
    }

    [Theory]
    [InlineData(ReceiptRules.OrderNumberField, null, "A new one needs 'OrderNumber'.")]
    [InlineData(ReceiptRules.OrderNumberField, " 300000001", "The order number is not one.")]
    [InlineData(ReceiptRules.TotalCentsField, "20.80", "The amount is not whole cents.")]
    [InlineData(ReceiptRules.TotalCentsField, "10000001", "That is more than any shop.")]
    [InlineData(ReceiptRules.PurchasedOnField, "24/10/2026", "The date is not a date.")]
    public void A_receipt_refuses(string field, string? value, string problem)
    {
        List<OpField> fields = [.. ReceiptCreate().Where(f => f.Name != field)];
        if (value is not null)
            fields.Add(new OpField(field, value));
        Assert.Equal(problem, ReceiptRules.OpProblem(OpKind.Create, fields));
    }

    [Theory]
    [InlineData(ReceiptLineRules.DescriptionField, "", "The description is not one.")]
    [InlineData(ReceiptLineRules.DescriptionField, "two  spaces", "The description is not one.")]
    [InlineData(ReceiptLineRules.QuantityField, "-1", "The quantity is not one.")]
    [InlineData(ReceiptLineRules.KindField, "food", "The kind is not food, household, fee or ignored.")]
    [InlineData(ReceiptLineRules.KindField, "0", "The kind is not food, household, fee or ignored.")]
    [InlineData(ReceiptLineRules.PositionField, "1000", "The position is not one.")]
    [InlineData(ReceiptLineRules.WeightGramsField, "0", "The weight is not one.")]
    public void A_line_refuses(string field, string value, string problem)
    {
        List<OpField> fields = [.. LineCreate().Where(f => f.Name != field), new OpField(field, value)];
        Assert.Equal(problem, ReceiptLineRules.OpProblem(OpKind.Create, fields));
        Assert.Equal("The description is not one.", ReceiptLineRules.OpProblem(OpKind.Update,
            [new OpField(ReceiptLineRules.DescriptionField, new string('x', ReceiptLineRules.MaxDescriptionLength + 1))]));
    }

    [Fact]
    public void Kinds_travel_by_name()
    {
        foreach (ReceiptLineKind kind in Enum.GetValues<ReceiptLineKind>())
            Assert.True(ReceiptLineKinds.TryParse(ReceiptLineKinds.Format(kind), out ReceiptLineKind back) && back == kind);
    }
}

/// <summary>Which movements count, now a receipt can replace a shopping-list tick.</summary>
public class StockCountingTests
{
    private static readonly Guid Mince = Guid.CreateVersion7();
    private static readonly Guid Tick = new("01900000-0000-8000-8000-000000000001");

    private static StockMovement Move(StockReason reason, decimal quantity, Guid? item = null, Guid? covers = null) => new()
    {
        Id = Guid.CreateVersion7(), IngredientId = Mince, Quantity = quantity, OccurredOn = new(2026, 10, 24), Reason = reason,
        ShoppingItemId = item, CoversItemId = covers,
    };

    [Fact]
    public void A_receipts_purchase_covering_a_tick_silences_every_movement_the_tick_wrote()
    {
        StockMovement ticked = Move(StockReason.Bought, 2000, item: Tick);
        StockMovement receipt = Move(StockReason.Bought, 1850, covers: Tick);
        StockMovement counted = Move(StockReason.Counted, 500);
        Dictionary<Guid, ShoppingTick> ticks = new() { [Tick] = ShoppingTick.Bought };

        Assert.Equal([ticked, counted], StockMovement.Counting([ticked, counted], ticks));
        Assert.Equal([receipt, counted], StockMovement.Counting([ticked, receipt, counted], ticks).OrderBy(m => m.Id == counted.Id));
        // A second phone's pack for the same tick, arriving after the receipt, is silenced too: the cover names the tick.
        Assert.Equal([receipt], StockMovement.Counting([receipt, Move(StockReason.Bought, 2000, item: Tick), ticked], ticks));
    }
}

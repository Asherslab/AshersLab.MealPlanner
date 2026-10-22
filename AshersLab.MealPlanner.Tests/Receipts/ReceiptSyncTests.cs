using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Pantry;
using AshersLab.MealPlanner.Shared.Data.Prices;
using AshersLab.MealPlanner.Shared.Data.Products;
using AshersLab.MealPlanner.Shared.Data.Receipts;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Pantry;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Products;
using AshersLab.MealPlanner.Shared.Domain.Receipts;
using AshersLab.MealPlanner.Shared.Domain.Shopping;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using AshersLab.MealPlanner.Tests.Grpc;
using AshersLab.MealPlanner.Tests.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Tests.Receipts;

/// <summary>
/// A Woolworths receipt imported in a real browser database and synced through the real host: what an import writes, the
/// alias that makes the next receipt match by itself, a second import refused, two phones importing one receipt, and a
/// receipt and a shopping-list tick for the same purchase counting once.
/// </summary>
public sealed class ReceiptSyncTests : IAsyncLifetime
{
    private readonly MealPlannerHost _host = new();
    private ISyncService _server = null!;

    private static readonly Guid Woolworths = CatalogueSeed.Woolworths;
    private static readonly DateOnly Bought = new(2026, 10, 24);
    private static readonly DateOnly Today = new(2026, 10, 25);

    public ValueTask InitializeAsync()
    {
        _server = _host.CreateGrpcWebClient<ISyncService>();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Guid NextId(TestBrowser browser)
    {
        browser.Now = browser.Now.AddMilliseconds(10);
        return Guid.CreateVersion7(browser.Now);
    }

    private sealed record Kitchen(Guid Beans, Guid Chicken, Guid Soy, Guid BeansProduct, Guid SoyProduct);

    private static async Task<Kitchen> KitchenAsync(TestBrowser a)
    {
        OpField usual = new(IngredientRules.UsualStoreField, OpValues.FormatId(Woolworths));
        Guid beans = await a.CreateIngredientAsync("Black beans", BaseUnit.Gram, usual);
        Guid chicken = await a.CreateIngredientAsync("Chicken breast", BaseUnit.Gram, usual);
        Guid soy = await a.CreateIngredientAsync("Soy sauce", BaseUnit.Millilitre, usual, new OpField(IngredientRules.IsStapleField, "true"));
        Guid beansProduct = NextId(a), soyProduct = NextId(a);
        await a.WriteAsync(SyncedEntities.Product, beansProduct, OpKind.Create, new ProductDraft("Black beans 420g", Woolworths, null, 420, beans).FieldsChangedFrom(null));
        await a.WriteAsync(SyncedEntities.Product, soyProduct, OpKind.Create, new ProductDraft("Soy sauce 250ml", Woolworths, null, 250, soy).FieldsChangedFrom(null));
        return new Kitchen(beans, chicken, soy, beansProduct, soyProduct);
    }

    /// <summary>Two tins of beans, a weighed chicken, floor cleaner, soy sauce, milk out of stock and its substitute, bags.</summary>
    private static ParsedReceipt Invoice(string order = "300000001") => WoolworthsInvoice.Parse(new SyntheticInvoice(order, "24 Oct 2026")
        .Heading("Canned & Packet Food")
        .Line(4, "Woolworths black beans pdm 420g", 2, 2, "$1.00", "$2.00")
        .Heading("Meat")
        .Weighed(17, "Woolworths chicken breast fillet skinless small", "500g - 800g", "$13.00/Kg", "1 @ 700g", "$9.10")
        .Heading("Household Cleaning")
        .Line(15, "* Earth choice floor cleaner 750ml", 1, 1, "$5.00", "$5.00")
        .Heading("Pantry")
        .Line(3, "Kikkoman soy sauce 250ml", 1, 1, "$4.00", "$4.00")
        .Substitutions()
        .Heading("Drinks")
        .Line(8, "Vitasoy oat milk uht 1l", 1, 0, "$2.50", "$0.00")
        .Line(8, "(Sub) Sanitarium oat milk uht 1L", 0, 1, "$3.00", "$2.50")
        .Totals("$22.60", "$2.00", "$0.00", "$24.60").Pages());

    private static async Task<ReceiptImport> ReviewAsync(TestBrowser browser, ParsedReceipt receipt)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await ReceiptImport.LoadAsync(db, Woolworths, receipt, Ct);
    }

    private static async Task ImportAsync(TestBrowser browser, ReceiptImport import, IReadOnlyList<LineDecision> decisions)
    {
        browser.Now = browser.Now.AddSeconds(1);
        List<ReceiptEdit> edits = import.Edits(decisions, Today, () => NextId(browser));
        await using LocalDbContext db = browser.CreateDbContext();
        await LocalOps.WriteAllAsync(db, browser.DeviceId, browser.Now, [.. edits.Select(e => new LocalEdit(e.Type, e.Id, e.Kind, e.Fields))], Ct);
    }

    /// <summary>The planner's review of <see cref="Invoice"/>: the beans and soy sauce to their products, a new chicken product, the cleaner as household.</summary>
    private static List<LineDecision> Reviewed(ReceiptImport import, Kitchen k)
    {
        List<ReviewLine> lines = import.Review();
        return
        [
            lines[0].Decision with { ProductId = k.BeansProduct },
            lines[1].Decision with { New = new NewProduct("Chicken breast fillet small", k.Chicken, ReceiptMatching.PackGuess(lines[1].Line.Description, BaseUnit.Gram)!.Value) },
            lines[2].Decision with { New = new NewProduct("Earth choice floor cleaner 750ml", null, 750) },
            lines[3].Decision with { ProductId = k.SoyProduct },
            lines[4].Decision,
            lines[5].Decision with { Ignore = true },
        ];
    }

    private static async Task<PantryBook> PantryAsync(TestBrowser browser, IReadOnlySet<Guid>? ignoring = null)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await PantryBook.LoadAsync(db, Bought, Bought.AddDays(7), Ct, ignoring);
    }

    [Fact]
    public async Task An_old_shop_imported_for_its_prices_puts_nothing_into_the_pantry()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);
        ReceiptImport import = await ReviewAsync(a, Invoice());

        List<ReceiptEdit> pricesOnly = import.Edits(Reviewed(import, k), Today, () => NextId(a), intoPantry: false);
        List<ReceiptEdit> stocked = import.Edits(Reviewed(import, k), Today, () => NextId(a));

        Assert.DoesNotContain(pricesOnly, e => e.Type == SyncedEntities.StockMovement);
        Assert.Contains(stocked, e => e.Type == SyncedEntities.StockMovement);
        // Everything else the same: the receipt, its lines, the products, the prices and the aliases.
        Assert.Equal(stocked.Count(e => e.Type != SyncedEntities.StockMovement), pricesOnly.Count);
        Assert.Equal(stocked.Count(e => e.Type == SyncedEntities.PriceObservation), pricesOnly.Count(e => e.Type == SyncedEntities.PriceObservation));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]    // a week ago: still in the cupboard
    [InlineData(8, false)]   // older: long eaten
    [InlineData(40, false)]
    public void A_shop_goes_into_the_pantry_by_default_only_if_it_was_in_the_last_week(int daysAgo, bool expected) =>
        Assert.Equal(expected, ReceiptStock.IntoPantryByDefault(Today.AddDays(-daysAgo), Today));

    [Fact]
    public void The_synthetic_invoice_reads_as_its_six_lines_and_adds_up()
    {
        ParsedReceipt receipt = Invoice();
        Assert.Equal(6, receipt.Lines.Count);
        Assert.Null(receipt.TotalsProblem());
    }

    [Fact]
    public async Task An_import_writes_the_receipt_its_lines_prices_stock_and_aliases_and_the_next_receipt_matches_by_itself()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);

        ReceiptImport first = await ReviewAsync(a, Invoice());
        List<ReviewLine> review = first.Review();
        // Nothing matches by itself the first time: the beans are only a suggestion the planner confirms.
        Assert.All(review, l => Assert.Null(l.Match.ProductId));
        Assert.Equal(k.BeansProduct, review[0].Match.Suggestions[0].ProductId);
        await ImportAsync(a, first, Reviewed(first, k));
        await a.SyncAsync();
        await b.SyncAsync();

        await using (LocalDbContext db = b.CreateDbContext())
        {
            Receipt receipt = await db.Receipts.SingleAsync(Ct);
            Assert.Equal((ReceiptRules.ReceiptId(Woolworths, "300000001"), "300000001", Bought, 2260L, 200L, 2460L, Today),
                (receipt.Id, receipt.OrderNumber, receipt.PurchasedOn, receipt.SubtotalCents, receipt.FeesCents, receipt.TotalCents, receipt.ImportedOn));

            List<ReceiptLine> lines = await db.ReceiptLines.OrderBy(l => l.Position).ToListAsync(Ct);
            Assert.Equal(
            [
                ("Woolworths black beans pdm 420g", ReceiptLineKind.Food, 200L),
                ("Woolworths chicken breast fillet skinless small 500g - 800g", ReceiptLineKind.Food, 910L),
                ("Earth choice floor cleaner 750ml", ReceiptLineKind.Household, 500L),
                ("Kikkoman soy sauce 250ml", ReceiptLineKind.Food, 400L),
                // Out of stock and not matched: imported, as food, matching nothing.
                ("Vitasoy oat milk uht 1l", ReceiptLineKind.Food, 0L),
                ("Sanitarium oat milk uht 1L", ReceiptLineKind.Ignored, 250L),
                // The bags, a fee. The $0.00 fees are not lines.
                ("Paper bags", ReceiptLineKind.Fee, 200L),
            ], lines.Select(l => (l.Description, l.Kind, l.AmountCents)));
            Assert.Equal((700m, 1300L), (lines[1].WeightGrams!.Value, lines[1].PerKgCents!.Value));
            Assert.Null(lines[4].ProductId);
            Assert.Null(lines[5].ProductId);

            // The new chicken product: under its ingredient, its pack the heaviest tray.
            Product chicken = await db.Products.SingleAsync(p => p.Name == "Chicken breast fillet small", Ct);
            Assert.Equal((k.Chicken, 800m), (chicken.IngredientId, chicken.PackQuantity));
            Assert.Null((await db.Products.SingleAsync(p => p.Name == "Earth choice floor cleaner 750ml", Ct)).IngredientId);

            // A price for each matched line that came home, from the receipt, on its date: the beans' shelf price, the
            // chicken tray's own cost with its per-kg price, the cleaner, the soy sauce.
            List<PriceObservation> prices = await db.PriceObservations.Where(p => p.Source == PriceSource.Receipt).ToListAsync(Ct);
            Assert.Equal([(k.BeansProduct, 100L, (long?)null), (k.SoyProduct, 400L, null), (lines[2].ProductId!.Value, 500L, null), (chicken.Id, 910L, 1300L)],
                prices.OrderBy(p => p.PriceCents).Select(p => (p.ProductId, p.PriceCents, p.PerKgCents)));
            Assert.All(prices, p => Assert.Equal(Bought, p.ObservedOn));

            // Into the pantry: two tins of beans (840 g) and the chicken by its weight. The soy sauce is a staple - no movement -
            // and the cleaner is not food.
            List<StockMovement> moves = await db.StockMovements.ToListAsync(Ct);
            Assert.Equal([(k.Chicken, 700m), (k.Beans, 840m)], moves.OrderBy(m => m.Quantity).Select(m => (m.IngredientId, m.Quantity)));
            Assert.All(moves, m => Assert.Equal((StockReason.Bought, Bought, (Guid?)null), (m.Reason, m.OccurredOn, m.CoversItemId)));

            // Every matched line taught its alias.
            Assert.Equal(4, await db.ProductAliases.CountAsync(Ct));
        }

        // The next order on the other phone: the same products match by themselves.
        ReceiptImport next = await ReviewAsync(b, Invoice("300000002"));
        List<ReviewLine> again = next.Review();
        Assert.Equal([MatchedBy.Alias, MatchedBy.Alias, MatchedBy.Alias, MatchedBy.Alias, MatchedBy.None, MatchedBy.None],
            again.Select(l => l.Match.By));
        Assert.Equal(k.BeansProduct, again[0].Decision.ProductId);
        Assert.Null(next.Earlier);
    }

    [Fact]
    public async Task The_same_order_again_is_refused_naming_the_earlier_import()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);
        ReceiptImport first = await ReviewAsync(a, Invoice());
        await ImportAsync(a, first, Reviewed(first, k));
        await a.SyncAsync();
        await b.SyncAsync();

        ReceiptImport again = await ReviewAsync(b, Invoice());
        Assert.Equal((Today, Bought), (again.Earlier!.ImportedOn, again.Earlier.PurchasedOn));
        Assert.Throws<InvalidOperationException>(() => again.Edits(again.Review().Select(l => l.Decision).ToList(), Today, Guid.CreateVersion7));
    }

    [Fact]
    public async Task A_receipt_that_does_not_add_up_is_never_imported()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        ParsedReceipt wrong = Invoice() with { SubtotalCents = 2400 };
        ReceiptImport import = await ReviewAsync(a, wrong);
        Assert.Equal("The lines add up to $22.60, but the receipt's sub total is $24.00.",
            Assert.Throws<InvalidOperationException>(() => import.Edits(import.Review().Select(l => l.Decision).ToList(), Today, Guid.CreateVersion7)).Message);
    }

    [Fact]
    public async Task Two_phones_importing_one_receipt_offline_make_one_receipt_and_one_lot_of_stock()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);
        await a.SyncAsync();
        await b.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            ReceiptImport import = await ReviewAsync(browser, Invoice());
            List<LineDecision> decisions = [.. import.Review().Select(l => l.Decision)];
            decisions[0] = decisions[0] with { ProductId = k.BeansProduct };
            await ImportAsync(browser, import, decisions);
        }
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            await using LocalDbContext db = browser.CreateDbContext();
            Assert.Equal(1, await db.Receipts.CountAsync(Ct));
            Assert.Equal(7, await db.ReceiptLines.CountAsync(Ct));
            Assert.Equal(1, await db.PriceObservations.CountAsync(p => p.Source == PriceSource.Receipt, Ct));
            Assert.Equal(840, (await PantryAsync(browser)).At(k.Beans, Bought.AddDays(1)).Quantity);
        }
    }

    /// <summary>A shopping-list tick of two tins of beans, as <c>ShoppingTicking</c> writes it: the tick and its movement.</summary>
    private static async Task<Guid> TickBoughtAsync(TestBrowser a, Guid ingredient, decimal quantity, DateOnly on)
    {
        DateOnly monday = on.AddDays(-(((int)on.DayOfWeek + 6) % 7));
        Guid list = ShoppingListRules.ListId(monday, monday.AddDays(6));
        Guid tick = ShoppingItemRules.TickId(list, ingredient);
        await using LocalDbContext db = a.CreateDbContext();
        a.Now = a.Now.AddSeconds(1);
        await LocalOps.WriteAllAsync(db, a.DeviceId, a.Now,
        [
            new LocalEdit(SyncedEntities.ShoppingList, list, OpKind.Create, ShoppingListRules.CreateFields(monday, monday.AddDays(6))),
            new LocalEdit(SyncedEntities.ShoppingListItem, tick, OpKind.Create, ShoppingItemRules.TickFields(list, ingredient, ShoppingTick.Bought)),
            new LocalEdit(SyncedEntities.StockMovement, NextId(a), OpKind.Create, new StockEntry(ingredient, quantity, on, StockReason.Bought, tick).Fields()),
        ], Ct);
        return tick;
    }

    [Fact]
    public async Task A_receipt_and_a_shopping_list_tick_for_the_same_purchase_count_once_the_receipts_quantity()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);
        // In the shop on Thursday the beans were ticked bought: one tin, the list said. Two came home.
        Guid tick = await TickBoughtAsync(a, k.Beans, 420, Bought.AddDays(-2));
        await a.SyncAsync();
        await b.SyncAsync();
        Assert.Equal(420, (await PantryAsync(b)).At(k.Beans, Bought.AddDays(1)).Quantity);

        ReceiptImport import = await ReviewAsync(b, Invoice());
        List<LineDecision> decisions = Reviewed(import, k);
        // The review found the tick: the receipt's beans replace it.
        Assert.Equal(tick, import.CoverFor(import.Parsed.Lines[0], k.BeansProduct, new HashSet<Guid>()));
        decisions[0] = decisions[0] with { Covers = tick };
        await ImportAsync(b, import, decisions);
        await b.SyncAsync();
        await a.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            // 840 g - the receipt's two tins - not 1260 g.
            Assert.Equal(840, (await PantryAsync(browser)).At(k.Beans, Bought.AddDays(1)).Quantity);
            // The week's shopping list reads the pantry without its own ticks - and without the purchase that replaced one -
            // so the beans stay a line it bought, not "in the pantry".
            Assert.Equal(StockState.NotTracked, (await PantryAsync(browser, new HashSet<Guid> { tick })).At(k.Beans, Bought.AddDays(1)).State);
            await using LocalDbContext db = browser.CreateDbContext();
            Assert.Equal(tick, (await db.StockMovements.SingleAsync(m => m.CoversItemId != null, Ct)).CoversItemId);
            Assert.Equal(ShoppingTick.Bought, (await db.ShoppingListItems.SingleAsync(Ct)).Tick);
        }

        // A tick already replaced is not offered again.
        ReceiptImport later = await ReviewAsync(a, Invoice("300000002"));
        Assert.Null(later.CoverFor(later.Parsed.Lines[0], k.BeansProduct, new HashSet<Guid>()));
    }

    [Fact]
    public async Task Turned_off_the_cover_two_purchases_both_count()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);
        // The beans were matched on an earlier receipt, so the review matches them by itself - and offers the tick.
        await a.WriteAsync(SyncedEntities.ProductAlias, ProductAliasRules.AliasId(Woolworths, "Woolworths black beans pdm 420g"), OpKind.Create,
            ReceiptFields.Of(new ProductAlias { StoreId = Woolworths, Text = "Woolworths black beans pdm 420g", ProductId = k.BeansProduct }));
        Guid tick = await TickBoughtAsync(a, k.Beans, 420, Bought.AddDays(-2));

        ReceiptImport import = await ReviewAsync(a, Invoice());
        List<LineDecision> decisions = Reviewed(import, k);
        Assert.Equal((MatchedBy.Alias, tick), (import.Review()[0].Match.By, decisions[0].Covers));

        // The planner unticks "Replaces the shopping list's tick": what the page decides from its checkbox.
        decisions[0] = ReviewChoice.Decide($"p:{k.BeansProduct}", null, null, BaseUnit.Gram, tick, cover: false)!;
        await ImportAsync(a, import, decisions);

        await using LocalDbContext db = a.CreateDbContext();
        Assert.Null((await db.StockMovements.SingleAsync(m => m.Id == ReceiptRules.StockId(ReceiptRules.LineId(import.ReceiptId, 0)), Ct)).CoversItemId);
        Assert.Equal(1260, (await PantryAsync(a)).At(k.Beans, Bought.AddDays(1)).Quantity);
    }

    [Fact]
    public async Task The_same_order_under_another_store_is_refused_too()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);
        ReceiptImport first = await ReviewAsync(a, Invoice());
        await ImportAsync(a, first, Reviewed(first, k));

        // A receipt's id is derived from its store as well as its order: under another store the id differs, but the
        // purchase is the same one.
        await using LocalDbContext db = a.CreateDbContext();
        ReceiptImport again = await ReceiptImport.LoadAsync(db, CatalogueSeed.Costco, Invoice(), Ct);
        Assert.NotEqual(first.ReceiptId, again.ReceiptId);
        Assert.Equal(Today, again.Earlier?.ImportedOn);
        Assert.Throws<InvalidOperationException>(() => again.Edits(again.Review().Select(l => l.Decision).ToList(), Today, Guid.CreateVersion7));
    }
}

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
/// A Costco receipt imported through the same review and import as a Woolworths invoice, synced through the real host: its
/// item numbers become the products' codes, so the next receipt matches by code; a line matched by name to a product with no
/// code teaches an alias instead; and its purchase replaces the shopping-list tick it covers.
/// </summary>
public sealed class CostcoSyncTests : IAsyncLifetime
{
    private readonly MealPlannerHost _host = new();
    private ISyncService _server = null!;

    private static readonly Guid Costco = CatalogueSeed.Costco;
    private static readonly DateOnly Bought = new(2026, 3, 3);
    private static readonly DateOnly Today = new(2026, 3, 4);

    public ValueTask InitializeAsync()
    {
        _server = _host.CreateGrpcWebClient<ISyncService>();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ReceiptImport> ReviewAsync(TestBrowser browser, ParsedReceipt receipt)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await ReceiptImport.LoadAsync(db, Costco, receipt, Ct);
    }

    [Fact]
    public async Task Item_numbers_become_codes_the_next_receipt_matches_by_them_and_a_purchase_replaces_its_tick()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        OpField usual = new(IngredientRules.UsualStoreField, OpValues.FormatId(Costco));
        Guid mince = await a.CreateIngredientAsync("Beef mince", BaseUnit.Gram, usual);
        Guid apples = await a.CreateIngredientAsync("Apples", BaseUnit.Gram, usual);
        // Apples were priced by hand before: a product with no item number.
        a.Now = a.Now.AddMilliseconds(10);
        Guid applesProduct = Guid.CreateVersion7(a.Now);
        await a.WriteAsync(SyncedEntities.Product, applesProduct, OpKind.Create, new ProductDraft("Pink lady apples 2kg", Costco, null, 2000, apples).FieldsChangedFrom(null));

        // The mince was ticked bought on the shopping list the day before: one pack, the list thought.
        DateOnly monday = Bought.AddDays(-(((int)Bought.DayOfWeek + 6) % 7));
        Guid list = ShoppingListRules.ListId(monday, monday.AddDays(6));
        Guid tick = ShoppingItemRules.TickId(list, mince);
        await using (LocalDbContext db = a.CreateDbContext())
        {
            a.Now = a.Now.AddSeconds(1);
            await LocalOps.WriteAllAsync(db, a.DeviceId, a.Now,
            [
                new LocalEdit(SyncedEntities.ShoppingList, list, OpKind.Create, ShoppingListRules.CreateFields(monday, monday.AddDays(6))),
                new LocalEdit(SyncedEntities.ShoppingListItem, tick, OpKind.Create, ShoppingItemRules.TickFields(list, mince, ShoppingTick.Bought)),
                new LocalEdit(SyncedEntities.StockMovement, Guid.CreateVersion7(a.Now), OpKind.Create, new StockEntry(mince, 500, Bought.AddDays(-1), StockReason.Bought, tick).Fields()),
            ], Ct);
        }
        await a.SyncAsync();
        await b.SyncAsync();

        ParsedReceipt receipt = CostcoReceipt.Parse(CostcoReceiptTests.Receipt()).Receipt;
        ReceiptImport import = await ReviewAsync(b, receipt);
        List<ReviewLine> review = import.Review();
        Assert.All(review, l => Assert.Equal(MatchedBy.None, l.Match.By));
        Assert.Equal(applesProduct, review[1].Match.Suggestions[0].ProductId);
        Guid? covers = import.CoverFor(receipt.Lines[0], null, new HashSet<Guid>(), mince);
        Assert.Equal(tick, covers);

        List<LineDecision> decisions =
        [
            // A new product of the mince, its pack from the second line: "6X500G".
            ReviewChoice.Decide($"n:{mince}", "Grassfed beef mince 6x500g", ReceiptMatching.PackGuess(receipt.Lines[0].Description, BaseUnit.Gram)!.Value.ToString(),
                BaseUnit.Gram, covers, cover: true)!,
            ReviewChoice.Decide($"p:{applesProduct}", null, null, BaseUnit.Gram, null, true)!,
            ReviewChoice.Decide("", null, null, BaseUnit.Each, null, true)!,
            ReviewChoice.Decide("h", "Nappies size 3", "120", BaseUnit.Each, null, true)!,
        ];
        b.Now = b.Now.AddSeconds(1);
        List<ReceiptEdit> edits = import.Edits(decisions, Today, () => { b.Now = b.Now.AddMilliseconds(10); return Guid.CreateVersion7(b.Now); });
        await using (LocalDbContext db = b.CreateDbContext())
            await LocalOps.WriteAllAsync(db, b.DeviceId, b.Now, [.. edits.Select(e => new LocalEdit(e.Type, e.Id, e.Kind, e.Fields))], Ct);
        await b.SyncAsync();
        await a.SyncAsync();

        await using (LocalDbContext db = a.CreateDbContext())
        {
            Receipt row = await db.Receipts.SingleAsync(Ct);
            Assert.Equal((Costco, "2026-03-03 17:07:00 100 001 0002 003", Bought, 9145L, 0L, 9145L), (row.StoreId, row.OrderNumber, row.PurchasedOn, row.SubtotalCents, row.FeesCents, row.TotalCents));

            List<ReceiptLine> lines = await db.ReceiptLines.OrderBy(l => l.Position).ToListAsync(Ct);
            Assert.Equal(
            [
                ("700101", ReceiptLineKind.Food, 1m, 2149L, 0L),
                ("700202", ReceiptLineKind.Food, 1m, 899L, 0L),
                ("700303", ReceiptLineKind.Food, 2m, 1098L, 300L),
                ("700404", ReceiptLineKind.Household, 1m, 4999L, 0L),
            ], lines.Select(l => (l.StoreCode!, l.Kind, l.Quantity, l.AmountCents, l.DiscountCents)));

            // The new products carry the item numbers; the hand-made apples product is untouched.
            Product made = await db.Products.SingleAsync(p => p.Name == "Grassfed beef mince 6x500g", Ct);
            Assert.Equal(("700101", 3000m, (Guid?)mince), (made.StoreCode, made.PackQuantity, made.IngredientId));
            Assert.Equal("700404", (await db.Products.SingleAsync(p => p.Name == "Nappies size 3", Ct)).StoreCode);
            Assert.Null((await db.Products.SingleAsync(p => p.Id == applesProduct, Ct)).StoreCode);

            // Only the apples taught an alias: their product has no code to match by next time.
            Assert.Equal([applesProduct], await db.ProductAliases.Select(x => x.ProductId).ToListAsync(Ct));

            List<PriceObservation> prices = await db.PriceObservations.Where(p => p.Source == PriceSource.Receipt).ToListAsync(Ct);
            Assert.Equal([899L, 2149L, 4999L], prices.Select(p => p.PriceCents).Order());

            // 3 kg of mince, replacing the tick's 500 g; 2 kg of apples.
            List<StockMovement> moves = await db.StockMovements.Where(m => m.ShoppingItemId == null).OrderBy(m => m.Quantity).ToListAsync(Ct);
            Assert.Equal([(apples, 2000m, (Guid?)null), (mince, 3000m, tick)], moves.Select(m => (m.IngredientId, m.Quantity, m.CoversItemId)));
            PantryBook pantry = await PantryBook.LoadAsync(db, Bought, Bought.AddDays(7), Ct);
            Assert.Equal(3000, pantry.At(mince, Bought.AddDays(1)).Quantity);
        }

        // The same receipt again on the other phone: refused.
        Assert.NotNull((await ReviewAsync(a, receipt)).Earlier);

        // The next receipt: the mince and nappies by their item numbers, the apples by their alias.
        ParsedReceipt next = CostcoReceipt.Parse(CostcoReceiptTests.Receipt(stamp: "10/03/2026 10:00:00 AM 100 001 0050 003")).Receipt;
        ReceiptImport again = await ReviewAsync(a, next);
        Assert.Null(again.Earlier);
        Assert.Equal([MatchedBy.StoreCode, MatchedBy.Alias, MatchedBy.None, MatchedBy.StoreCode], again.Review().Select(l => l.Match.By));
    }
}

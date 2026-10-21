using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Products;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Products;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using AshersLab.MealPlanner.Tests.Grpc;
using AshersLab.MealPlanner.Tests.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AshersLab.MealPlanner.Tests.Catalogue;

/// <summary>Products through the real host: every field, by op and by snapshot, and edits from two browsers.</summary>
public sealed class ProductSyncTests : IAsyncLifetime
{
    private readonly MealPlannerHost _host = new();
    private ISyncService _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = _host.CreateGrpcWebClient<ISyncService>();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Dictionary<Guid, Product>> ProductsAsync(TestBrowser browser)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await db.Products.AsNoTracking().Where(p => !p.Deleted).ToDictionaryAsync(p => p.Id, Ct);
    }

    [Fact]
    public async Task Every_product_field_reaches_another_browser_by_op_and_by_snapshot()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        Guid mince = await a.CreateIngredientAsync("beef mince", BaseUnit.Gram);
        Guid product = Guid.CreateVersion7();
        await a.WriteAsync(SyncedEntities.Product, product, OpKind.Create,
            new ProductDraft("Beef mince 8x250g", CatalogueSeed.Costco, "12345", 2000m, mince).FieldsChangedFrom(null));
        Guid bags = Guid.CreateVersion7();
        await a.WriteAsync(SyncedEntities.Product, bags, OpKind.Create,
            new ProductDraft("Bin bags", CatalogueSeed.Woolworths, null, 0.5m, null).FieldsChangedFrom(null));
        await a.SyncAsync();

        await using TestBrowser bySnapshot = await TestBrowser.OpenAsync(_server);
        await bySnapshot.SyncAsync();
        await using TestBrowser byOps = await TestBrowser.OpenAsync(_server);
        await byOps.SyncAsync();
        a.Now = a.Now.AddMinutes(1);
        await a.WriteAsync(SyncedEntities.Product, product, OpKind.Update, [new OpField(ProductRules.PackQuantityField, "1500")]);
        await a.SyncAsync();
        await byOps.SyncAsync();
        await bySnapshot.SyncAsync();

        foreach (TestBrowser browser in new[] { bySnapshot, byOps })
        {
            Dictionary<Guid, Product> rows = await ProductsAsync(browser);
            Product row = rows[product];
            Assert.Equal(("Beef mince 8x250g", CatalogueSeed.Costco, "12345", 1500m, (Guid?)mince),
                (row.Name, row.StoreId, row.StoreCode, row.PackQuantity, row.IngredientId));
            Assert.Equal((0.5m, (Guid?)null, (string?)null), (rows[bags].PackQuantity, rows[bags].IngredientId, rows[bags].StoreCode));
        }

        using IServiceScope scope = _host.Services.CreateScope();
        Product onServer = await scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>().Products.SingleAsync(p => p.Id == product, Ct);
        Assert.Equal(1500m, onServer.PackQuantity);
    }

    [Fact]
    public async Task Prices_reach_another_browser_by_op_and_by_snapshot_and_a_deleted_one_goes_everywhere()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser byOps = await TestBrowser.OpenAsync(_server);
        await byOps.SyncAsync();
        Guid product = Guid.CreateVersion7();
        await a.WriteAsync(SyncedEntities.Product, product, OpKind.Create,
            new ProductDraft("Beef mince", CatalogueSeed.Costco, null, 2000m, null).FieldsChangedFrom(null));
        Guid typed = Guid.CreateVersion7(), mistake = Guid.CreateVersion7(), meat = Guid.CreateVersion7();
        await a.WriteAsync(SyncedEntities.PriceObservation, typed, OpKind.Create,
            new PriceEntry(product, 2499, new DateOnly(2026, 10, 14), PriceSource.Manual, null).Fields());
        await a.WriteAsync(SyncedEntities.PriceObservation, mistake, OpKind.Create,
            new PriceEntry(product, 249900, new DateOnly(2026, 10, 21), PriceSource.Manual, null).Fields());
        await a.WriteAsync(SyncedEntities.PriceObservation, meat, OpKind.Create,
            new PriceEntry(product, 1873, new DateOnly(2026, 10, 20), PriceSource.Receipt, 1499).Fields());
        await a.SyncAsync();
        await byOps.SyncAsync();

        await a.WriteAsync(SyncedEntities.PriceObservation, mistake, OpKind.Delete, []);
        await a.SyncAsync();
        await byOps.SyncAsync();
        await using TestBrowser bySnapshot = await TestBrowser.OpenAsync(_server);
        await bySnapshot.SyncAsync();

        foreach (TestBrowser browser in new[] { byOps, bySnapshot })
        {
            await using LocalDbContext db = browser.CreateDbContext();
            List<PriceReading> readings = [.. (await db.PriceObservations.AsNoTracking().Where(o => !o.Deleted && o.ProductId == product).ToListAsync(Ct))
                .Select(o => o.Reading())];
            Assert.Equal(
                [new PriceReading(meat, 1873, new DateOnly(2026, 10, 20), PriceSource.Receipt, 1499),
                 new PriceReading(typed, 2499, new DateOnly(2026, 10, 14), PriceSource.Manual, null)],
                PriceReadings.NewestFirst(readings));
        }

        using IServiceScope scope = _host.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>().PriceObservations
            .Where(o => o.Id == mistake).Select(o => o.Deleted).SingleAsync(Ct));
    }

    [Fact]
    public async Task Two_browsers_editing_different_fields_of_one_product_offline_both_keep_their_edit()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Guid product = Guid.CreateVersion7();
        ProductDraft draft = new("Eggs 30pk", CatalogueSeed.Costco, null, 30m, null);
        await a.WriteAsync(SyncedEntities.Product, product, OpKind.Create, draft.FieldsChangedFrom(null));
        await a.SyncAsync();
        await b.SyncAsync();

        a.Now = a.Now.AddMinutes(1);
        await a.WriteAsync(SyncedEntities.Product, product, OpKind.Update, (draft with { StoreCode = "999" }).FieldsChangedFrom(draft));
        b.Now = b.Now.AddMinutes(2);
        await b.WriteAsync(SyncedEntities.Product, product, OpKind.Update, (draft with { PackQuantity = 36 }).FieldsChangedFrom(draft));
        await b.SyncAsync();
        await a.SyncAsync();
        await b.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            Product row = (await ProductsAsync(browser))[product];
            Assert.Equal(("999", 36m), (row.StoreCode, row.PackQuantity));
        }
    }
}

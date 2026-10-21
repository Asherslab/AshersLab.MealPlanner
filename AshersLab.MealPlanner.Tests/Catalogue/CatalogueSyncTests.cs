using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Categories;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain;
using AshersLab.MealPlanner.Shared.Data.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Categories;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Stores;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using AshersLab.MealPlanner.Tests.Grpc;
using AshersLab.MealPlanner.Tests.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AshersLab.MealPlanner.Tests.Catalogue;

/// <summary>Stores, categories and ingredients through the real host: the seeds, and every edit the pages make.</summary>
public sealed class CatalogueSyncTests : IAsyncLifetime
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

    private async Task<T> ServerAsync<T>(Func<MealPlannerDbContext, Task<T>> read)
    {
        using IServiceScope scope = _host.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>());
    }

    [Fact]
    public async Task A_new_browser_gets_the_seeded_stores()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();

        Dictionary<Guid, string> stores = await a.StoresAsync();
        Assert.Equal("Woolworths", stores[CatalogueSeed.Woolworths]);
        Assert.Equal("Costco", stores[CatalogueSeed.Costco]);
    }

    [Fact]
    public async Task A_new_browser_gets_the_seeded_categories_in_the_spreadsheet_order()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();

        Assert.Equal(CatalogueSeed.Categories, (await a.CategoriesAsync()).Select(c => c.Name));
    }

    [Fact]
    public async Task Moves_on_two_browsers_offline_both_survive()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();
        await b.SyncAsync();

        // A moves Household (last) up one; B, offline, moves Dairy (second) up to the top. Each is one op on a
        // different row, so neither undoes the other.
        await MoveAsync(a, "Household", -1);
        b.Now = b.Now.AddSeconds(1);
        await MoveAsync(b, "Dairy", -1);
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        string[] expected = ["Dairy", "Meat", "Fruit", "Vegetables", "Carbs/sides", "Pantry", "Fridge", "Household", "Freezer"];
        Assert.Equal(expected, (await a.CategoriesAsync()).Select(c => c.Name));
        Assert.Equal(expected, (await b.CategoriesAsync()).Select(c => c.Name));
        Assert.Equal(expected, CategoryEntity.InOrder(await ServerAsync(db => db.Categories.Where(c => !c.Deleted).ToListAsync(Ct))).Select(c => c.Name));
    }

    /// <summary>What the categories page's arrows do.</summary>
    private static async Task MoveAsync(TestBrowser browser, string name, int direction)
    {
        List<Category> categories = await browser.CategoriesAsync();
        int index = categories.FindIndex(c => c.Name == name);
        foreach ((int at, decimal position) in ListPosition.Move([.. categories.Select(c => c.Position)], index, direction))
            await browser.WriteAsync(SyncedEntities.Category, categories[at].Id, OpKind.Update,
                [new OpField(CategoryRules.PositionField, ListPosition.Format(position))]);
    }

    [Fact]
    public async Task Every_ingredient_field_reaches_another_browser_by_op_and_by_snapshot()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();

        Guid dairy = CatalogueSeed.Category(1);
        Guid cucumber = await a.CreateIngredientAsync("cucumber", BaseUnit.Each,
            new OpField(IngredientRules.CategoryField, OpValues.FormatId(dairy)),
            new OpField(IngredientRules.UsualStoreField, OpValues.FormatId(CatalogueSeed.Costco)),
            new OpField(IngredientRules.IsStapleField, "true"));
        await a.SyncAsync();

        await using TestBrowser bySnapshot = await TestBrowser.OpenAsync(_server);
        await bySnapshot.SyncAsync();     // first load: a snapshot

        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        await b.SyncAsync();
        Guid oil = await a.CreateIngredientAsync("olive oil", BaseUnit.Millilitre, new OpField(IngredientRules.IsStapleField, "true"));
        await a.SyncAsync();
        await b.SyncAsync();         // a cursor already: ops

        foreach (TestBrowser browser in new[] { bySnapshot, b })
        {
            Ingredient row = (await browser.IngredientRowsAsync())[cucumber];
            Assert.Equal(("cucumber", BaseUnit.Each, dairy, CatalogueSeed.Costco, true),
                (row.Name, row.BaseUnit, row.CategoryId, row.UsualStoreId, row.IsStaple));
        }

        Ingredient oilOnB = (await b.IngredientRowsAsync())[oil];
        Assert.Equal((BaseUnit.Millilitre, (Guid?)null, (Guid?)null, true), (oilOnB.BaseUnit, oilOnB.CategoryId, oilOnB.UsualStoreId, oilOnB.IsStaple));
    }

    [Fact]
    public async Task Two_browsers_editing_different_fields_of_one_ingredient_offline_both_keep_their_edit()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Guid mayo = await a.CreateIngredientAsync("mayo", BaseUnit.Gram);
        await a.SyncAsync();
        await b.SyncAsync();

        a.Now = a.Now.AddMinutes(1);
        await a.WriteAsync(SyncedEntities.Ingredient, mayo, OpKind.Update, [new OpField(IngredientRules.BaseUnitField, "Millilitre")]);
        b.Now = b.Now.AddMinutes(2);
        await b.WriteAsync(SyncedEntities.Ingredient, mayo, OpKind.Update,
            [new OpField(IngredientRules.CategoryField, OpValues.FormatId(CatalogueSeed.Category(6)))]);
        await b.SyncAsync();
        await a.SyncAsync();
        await b.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            Ingredient row = (await browser.IngredientRowsAsync())[mayo];
            Assert.Equal((BaseUnit.Millilitre, (Guid?)CatalogueSeed.Category(6)), (row.BaseUnit, row.CategoryId));
        }
    }

    [Fact]
    public async Task An_ingredient_whose_category_was_deleted_elsewhere_survives_pointing_at_the_tombstone()
    {
        // The race the page's refusal cannot stop: A deletes a category nothing uses (as A sees it) while B, offline,
        // puts an ingredient in it.
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();
        await b.SyncAsync();
        Guid freezer = CatalogueSeed.Category(7);

        await a.WriteAsync(SyncedEntities.Category, freezer, OpKind.Delete, []);
        Guid peas = await b.CreateIngredientAsync("frozen peas", BaseUnit.Gram, new OpField(IngredientRules.CategoryField, OpValues.FormatId(freezer)));
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            Assert.Equal(freezer, (await browser.IngredientRowsAsync())[peas].CategoryId);
            Assert.DoesNotContain(await browser.CategoriesAsync(), c => c.Id == freezer);
        }
    }

    [Fact]
    public async Task Seeds_are_written_once_and_a_deleted_seed_stays_deleted()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();
        await a.WriteAsync(SyncedEntities.Store, CatalogueSeed.Costco, OpKind.Delete, []);
        await a.WriteAsync(SyncedEntities.Store, CatalogueSeed.Woolworths, OpKind.Update, [new OpField(StoreRules.NameField, "Woolies")]);
        await a.SyncAsync();

        // A restart runs the seeding again.
        int written = await ServerAsync(async db =>
        {
            await using IDbContextTransaction transaction =await db.Database.BeginTransactionAsync(Ct);
            long? latest = await CatalogueSeed.WriteMissingAsync(db, Ct);
            await transaction.CommitAsync(Ct);
            return latest is null ? 0 : 1;
        });

        Assert.Equal(0, written);
        Assert.Equal(["Woolies"], (await a.StoresAsync()).Values);
        Assert.True(await ServerAsync(db => db.Stores.Where(s => s.Id == CatalogueSeed.Costco).Select(s => s.Deleted).SingleAsync(Ct)));
    }

    [Fact]
    public async Task A_seed_loses_to_any_edit_whatever_its_clock()
    {
        // A browser renamed Woolworths, and then the server lost it all (a backup from before the seed). The seed is
        // written again at clock zero; the browser's rename, pushed back, wins.
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        a.Now = DateTimeOffset.FromUnixTimeMilliseconds(1);
        await a.WriteAsync(SyncedEntities.Store, CatalogueSeed.Woolworths, OpKind.Update, [new OpField(StoreRules.NameField, "Woolies")]);
        await a.SyncAsync();

        Assert.Equal("Woolies", (await a.StoresAsync())[CatalogueSeed.Woolworths]);
        Assert.Equal("Woolies", await ServerAsync(db => db.Stores.Where(s => s.Id == CatalogueSeed.Woolworths).Select(s => s.Name).SingleAsync(Ct)));
    }

    [Fact]
    public async Task A_store_added_renamed_and_deleted_on_one_browser_does_the_same_on_the_other()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);

        Guid butcher = Guid.CreateVersion7();
        await a.WriteAsync(SyncedEntities.Store, butcher, OpKind.Create, [new OpField(StoreRules.NameField, "Butcher")]);
        await a.SyncAsync();
        await b.SyncAsync();
        Assert.Equal("Butcher", (await b.StoresAsync())[butcher]);

        a.Now = a.Now.AddMinutes(1);
        await a.WriteAsync(SyncedEntities.Store, butcher, OpKind.Update, [new OpField(StoreRules.NameField, "The butcher")]);
        await a.SyncAsync();
        await b.SyncAsync();
        Assert.Equal("The butcher", (await b.StoresAsync())[butcher]);

        await b.WriteAsync(SyncedEntities.Store, butcher, OpKind.Delete, []);
        await b.SyncAsync();
        await a.SyncAsync();
        Assert.DoesNotContain(butcher, (await a.StoresAsync()).Keys);
        Assert.True(await ServerAsync(db => db.Stores.Where(s => s.Id == butcher).Select(s => s.Deleted).SingleAsync(Ct)));
    }
}

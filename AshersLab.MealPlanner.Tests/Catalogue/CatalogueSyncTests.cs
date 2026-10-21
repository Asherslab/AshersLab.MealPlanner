using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Stores;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using AshersLab.MealPlanner.Tests.Grpc;
using AshersLab.MealPlanner.Tests.Sync;
using Microsoft.EntityFrameworkCore;
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
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
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

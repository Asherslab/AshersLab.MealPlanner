using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Categories;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using AshersLab.MealPlanner.Tests.Grpc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AshersLab.MealPlanner.Tests.Sync;

/// <summary>
/// The server reinstalled without a backup: a new, empty database under browsers that still hold the old one's rows.
/// A browser holding anything the new one lacks pushes and pulls nothing until the planner chooses - send its copy,
/// or clear it - and several browsers sending their copies merge by the ordinary rules.
/// </summary>
public sealed class ServerReplacedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_browser_holding_only_what_the_new_database_has_takes_it_without_asking()
    {
        await using MealPlannerHost old = new();
        await using TestBrowser a = await TestBrowser.OpenAsync(old.CreateGrpcWebClient<ISyncService>());
        await a.SyncAsync();

        // Only the seeds: the same rows at the same zero clock in every database.
        await using MealPlannerHost fresh = new();
        HookedServer wire = new(fresh.CreateGrpcWebClient<ISyncService>());
        a.Server = wire;
        SyncRoundResult round = await a.SyncAsync();

        Assert.Null(round.Replaced);
        Assert.Contains(wire.PullAnswers, p => p.Snapshot?.Reason == SnapshotReason.NewDatabase);
        Assert.Equal(await EpochAsync(fresh), (await a.StateAsync()).ServerEpoch);
        Assert.Equal(MealPlannerHost.SeedOps, (await a.StateAsync()).PulledThroughSeq);
    }

    [Fact]
    public async Task A_browser_holding_data_the_new_database_lacks_waits_and_sends_nothing()
    {
        await using MealPlannerHost old = new();
        await using TestBrowser a = await TestBrowser.OpenAsync(old.CreateGrpcWebClient<ISyncService>());
        Guid flour = await a.CreateIngredientAsync("flour");
        await a.SyncAsync();
        a.Now = a.Now.AddMinutes(1);
        Guid eggs = await a.CreateIngredientAsync("eggs");     // queued, never pushed

        await using MealPlannerHost fresh = new();
        HookedServer wire = new(fresh.CreateGrpcWebClient<ISyncService>());
        a.Server = wire;
        SyncRoundResult round = await a.SyncAsync();

        Assert.Equal(new ServerReplaced(await EpochAsync(fresh), 2), round.Replaced);
        // The push went before the pull found out, and the server logged none of it.
        Assert.Empty(Assert.Single(wire.PushAnswers).Results);
        Assert.Equal(MealPlannerHost.SeedOps, await OpCountAsync(fresh));
        // Nothing here changed: rows, queued edit, cursor still on the old database.
        Assert.Equal(new Dictionary<Guid, string> { [flour] = "flour", [eggs] = "eggs" }, await a.IngredientsAsync());
        Assert.Equal(1, await a.PendingAsync());
        Assert.Equal(await EpochAsync(old), (await a.StateAsync()).ServerEpoch);

        // Every round after that, in this window or another, asks again without touching the network.
        int calls = wire.PushAnswers.Count + wire.PullAnswers.Count;
        Assert.Equal(round.Replaced, (await a.SyncAsync()).Replaced);
        Assert.Equal(calls, wire.PushAnswers.Count + wire.PullAnswers.Count);
    }

    [Fact]
    public async Task A_deleted_row_the_new_database_never_had_is_not_worth_asking_about()
    {
        // Added and deleted before the wipe: all this browser holds is the tombstone, of nothing the planner would miss.
        await using MealPlannerHost old = new();
        await using TestBrowser a = await TestBrowser.OpenAsync(old.CreateGrpcWebClient<ISyncService>());
        Guid typo = await a.CreateIngredientAsync("fluor");
        await a.SyncAsync();
        a.Now = a.Now.AddMinutes(1);
        await a.DeleteAsync(typo);
        await a.SyncAsync();

        await using MealPlannerHost fresh = new();
        a.Server = fresh.CreateGrpcWebClient<ISyncService>();
        SyncRoundResult round = await a.SyncAsync();

        Assert.Null(round.Replaced);
        Assert.Equal(await EpochAsync(fresh), (await a.StateAsync()).ServerEpoch);
        Assert.Equal(MealPlannerHost.SeedOps, await OpCountAsync(fresh));
    }

    [Fact]
    public async Task A_deleted_seed_is_something_the_new_database_lacks()
    {
        await using MealPlannerHost old = new();
        await using TestBrowser a = await TestBrowser.OpenAsync(old.CreateGrpcWebClient<ISyncService>());
        await a.SyncAsync();
        await a.WriteAsync(SyncedEntities.Store, CatalogueSeed.Costco, OpKind.Delete, []);
        await a.SyncAsync();

        await using MealPlannerHost fresh = new();
        a.Server = fresh.CreateGrpcWebClient<ISyncService>();

        Assert.Equal(1, (await a.SyncAsync()).Replaced?.MissingRows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Copies_sent_from_two_browsers_merge_by_the_ordinary_rules(bool bSendsFirst)
    {
        await using MealPlannerHost old = new();
        await using TestBrowser a = await TestBrowser.OpenAsync(old.CreateGrpcWebClient<ISyncService>());
        await using TestBrowser b = await TestBrowser.OpenAsync(old.CreateGrpcWebClient<ISyncService>());

        Guid flour = await a.CreateIngredientAsync("flour");
        Guid sugar = await a.CreateIngredientAsync("sugar");
        await a.SyncAsync();
        await b.SyncAsync();

        // A renames flour and a seed category, both synced; B pulls them.
        a.Now = a.Now.AddMinutes(1);
        await a.RenameAsync(flour, "plain flour");
        await a.WriteAsync(SyncedEntities.Category, CatalogueSeed.Category(1), OpKind.Update,
            [new OpField(CategoryRules.NameField, "Dairy & eggs")]);
        await a.SyncAsync();
        await b.SyncAsync();

        // B deletes sugar (synced - A never pulls it) and adds eggs; then renames flour later than A did, still
        // queued when the server goes.
        b.Now = a.Now.AddMinutes(1);
        await b.DeleteAsync(sugar);
        Guid eggs = await b.CreateIngredientAsync("eggs");
        await b.SyncAsync();
        b.Now = b.Now.AddMinutes(1);
        await b.RenameAsync(flour, "wholemeal flour");

        await using MealPlannerHost fresh = new();
        a.Server = fresh.CreateGrpcWebClient<ISyncService>();
        b.Server = fresh.CreateGrpcWebClient<ISyncService>();
        Guid epoch = await EpochAsync(fresh);

        Assert.NotNull((await a.SyncAsync()).Replaced);
        Assert.NotNull((await b.SyncAsync()).Replaced);

        foreach (TestBrowser sender in bSendsFirst ? new[] { b, a } : [a, b])
        {
            Assert.True(await DecideAsync(sender, epoch, ServerReplacedChoice.SendMyCopy));
            Assert.Null((await sender.SyncAsync()).Replaced);
        }
        await a.SyncAsync();
        await b.SyncAsync();

        // The later rename wins, the delete wins, the seed's rename beats the new database's seed (clock zero), and
        // nothing is lost from either browser.
        Dictionary<Guid, string> expected = new() { [flour] = "wholemeal flour", [eggs] = "eggs" };
        Assert.Equal(expected, await ServerIngredientsAsync(fresh));
        Assert.Equal("Dairy & eggs", await ServerCategoryNameAsync(fresh, CatalogueSeed.Category(1)));

        long head = await OpCountAsync(fresh);
        foreach (TestBrowser browser in new[] { a, b })
        {
            Assert.Equal(expected, await browser.IngredientsAsync());
            Assert.Contains(sugar, await browser.TombstonesAsync());
            Assert.Equal("Dairy & eggs", (await browser.CategoriesAsync()).Single(c => c.Id == CatalogueSeed.Category(1)).Name);
            Assert.Equal(0, await browser.PendingAsync());
            Assert.Equal(epoch, (await browser.StateAsync()).ServerEpoch);
            Assert.Equal(head, (await browser.StateAsync()).PulledThroughSeq);
        }
    }

    [Fact]
    public async Task Clearing_deletes_everything_here_and_takes_the_server_as_it_is()
    {
        await using MealPlannerHost old = new();
        await using TestBrowser a = await TestBrowser.OpenAsync(old.CreateGrpcWebClient<ISyncService>());
        await a.CreateIngredientAsync("flour");
        await a.SyncAsync();
        a.Now = a.Now.AddMinutes(1);
        await a.CreateIngredientAsync("eggs");

        // Someone else is already using the new database.
        await using MealPlannerHost fresh = new();
        await using TestBrowser c = await TestBrowser.OpenAsync(fresh.CreateGrpcWebClient<ISyncService>());
        Guid milk = await c.CreateIngredientAsync("milk");
        await c.SyncAsync();

        a.Server = fresh.CreateGrpcWebClient<ISyncService>();
        Guid epoch = (await a.SyncAsync()).Replaced!.By;

        Assert.True(await DecideAsync(a, epoch, ServerReplacedChoice.ClearMyCopy));
        Assert.Equal(0, await a.PendingAsync());
        Assert.Empty(await a.IngredientsAsync());

        await a.SyncAsync();

        Dictionary<Guid, string> expected = new() { [milk] = "milk" };
        Assert.Equal(expected, await a.IngredientsAsync());
        Assert.Equal(expected, await ServerIngredientsAsync(fresh));
        Assert.Equal(0, await a.PendingAsync());
    }

    [Fact]
    public async Task Only_the_first_answer_applies()
    {
        await using MealPlannerHost old = new();
        await using TestBrowser a = await TestBrowser.OpenAsync(old.CreateGrpcWebClient<ISyncService>());
        Guid flour = await a.CreateIngredientAsync("flour");
        await a.SyncAsync();

        await using MealPlannerHost fresh = new();
        a.Server = fresh.CreateGrpcWebClient<ISyncService>();
        Guid epoch = (await a.SyncAsync()).Replaced!.By;

        // Two windows of the same browser, answering one after the other: the second changes nothing.
        Assert.True(await DecideAsync(a, epoch, ServerReplacedChoice.SendMyCopy));
        Assert.False(await DecideAsync(a, epoch, ServerReplacedChoice.ClearMyCopy));
        // Nor does an answer about some other database.
        Assert.False(await DecideAsync(a, Guid.NewGuid(), ServerReplacedChoice.ClearMyCopy));

        await a.SyncAsync();
        Assert.Equal("flour", (await ServerIngredientsAsync(fresh))[flour]);
        Assert.Equal("flour", (await a.IngredientsAsync())[flour]);
    }

    private static async Task<bool> DecideAsync(TestBrowser browser, Guid epoch, ServerReplacedChoice choice)
    {
        await using LocalDbContext db = await browser.Contexts.CreateDbContextAsync(Ct);
        return await LocalOps.DecideAsync(db, epoch, choice, Ct);
    }

    private static async Task<Guid> EpochAsync(MealPlannerHost host)
    {
        using IServiceScope scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>().SyncEpochs.Select(e => e.Epoch).SingleAsync(Ct);
    }

    private static async Task<Dictionary<Guid, string>> ServerIngredientsAsync(MealPlannerHost host)
    {
        using IServiceScope scope = host.Services.CreateScope();
        MealPlannerDbContext db = scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>();
        return await db.Ingredients.Where(i => !i.Deleted).ToDictionaryAsync(i => i.Id, i => i.Name, Ct);
    }

    private static async Task<string> ServerCategoryNameAsync(MealPlannerHost host, Guid id)
    {
        using IServiceScope scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>().Categories
            .Where(c => c.Id == id).Select(c => c.Name).SingleAsync(Ct);
    }

    private static async Task<long> OpCountAsync(MealPlannerHost host)
    {
        using IServiceScope scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>().Ops.LongCountAsync(Ct);
    }
}

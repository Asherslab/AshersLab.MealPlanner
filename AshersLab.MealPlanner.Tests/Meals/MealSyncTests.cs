using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Meals;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using AshersLab.MealPlanner.Tests.Grpc;
using AshersLab.MealPlanner.Tests.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Tests.Meals;

/// <summary>Meal groups, meals and their ingredients through the real host.</summary>
public sealed class MealSyncTests : IAsyncLifetime
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

    private static async Task<List<MealGroup>> GroupsAsync(TestBrowser browser)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return [.. MealGroupEntity.InOrder(await db.MealGroups.AsNoTracking().Where(g => !g.Deleted).ToListAsync(Ct))];
    }

    [Fact]
    public async Task A_new_browser_gets_the_seeded_meal_groups_in_the_spreadsheet_order()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();

        Assert.Equal(["Beef", "Chicken & egg", "Fish", "Other", "Individual", "Side", "Snack"], (await GroupsAsync(a)).Select(g => g.Name));
        Assert.Equal(CatalogueSeed.MealGroup(0), (await GroupsAsync(a))[0].Id);
    }

    [Fact]
    public async Task A_group_added_renamed_and_moved_on_one_browser_reaches_another()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();

        Guid pasta = Guid.CreateVersion7();
        await a.WriteAsync(SyncedEntities.MealGroup, pasta, OpKind.Create,
        [
            new OpField(MealGroupRules.NameField, "Pasta"),
            new OpField(MealGroupRules.PositionField, ListPosition.Format(ListPosition.AtEnd((await GroupsAsync(a)).Select(g => g.Position)))),
        ]);
        a.Now = a.Now.AddSeconds(1);
        await a.WriteAsync(SyncedEntities.MealGroup, pasta, OpKind.Update, [new OpField(MealGroupRules.NameField, "Pasta night")]);
        List<MealGroup> groups = await GroupsAsync(a);
        foreach ((int at, decimal position) in ListPosition.Move([.. groups.Select(g => g.Position)], groups.Count - 1, -1))
            await a.WriteAsync(SyncedEntities.MealGroup, groups[at].Id, OpKind.Update,
                [new OpField(MealGroupRules.PositionField, ListPosition.Format(position))]);
        await a.SyncAsync();
        await b.SyncAsync();

        Assert.Equal(["Beef", "Chicken & egg", "Fish", "Other", "Individual", "Side", "Pasta night", "Snack"],
            (await GroupsAsync(b)).Select(g => g.Name));
    }

    [Theory]
    [InlineData(OpKind.Create, MealGroupRules.NameField, "Pasta", true)]
    [InlineData(OpKind.Update, MealGroupRules.NameField, " Pasta", true)]
    [InlineData(OpKind.Update, MealGroupRules.PositionField, "two", true)]
    [InlineData(OpKind.Update, MealGroupRules.PositionField, "2.5", false)]
    [InlineData(OpKind.Update, "Colour", "red", true)]
    public void Meal_group_ops_are_checked(OpKind kind, string field, string value, bool invalid) =>
        Assert.Equal(invalid, MealGroupRules.OpProblem(kind, [new OpField(field, value)]) is not null);
}

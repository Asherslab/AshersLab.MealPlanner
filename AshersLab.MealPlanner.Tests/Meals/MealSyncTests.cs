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

    [Fact]
    public async Task Every_meal_field_reaches_another_browser_by_op_and_by_snapshot()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser byOps = await TestBrowser.OpenAsync(_server);
        await byOps.SyncAsync();

        Guid bolognese = Guid.CreateVersion7(), pizza = Guid.CreateVersion7();
        MealDraft draft = new("Spaghetti bolognese", CatalogueSeed.MealGroup(0), MealKind.Cooked, 4, null, "Freezes well.\nDouble the sauce.");
        await a.WriteAsync(SyncedEntities.Meal, bolognese, OpKind.Create, draft.FieldsChangedFrom(null));
        await a.WriteAsync(SyncedEntities.Meal, pizza, OpKind.Create,
            new MealDraft("Pizza", null, MealKind.FixedCost, 3, 4500, null).FieldsChangedFrom(null));
        await a.SyncAsync();
        await byOps.SyncAsync();

        a.Now = a.Now.AddMinutes(1);
        await a.WriteAsync(SyncedEntities.Meal, bolognese, OpKind.Update, (draft with { ServesPerBatch = 6 }).FieldsChangedFrom(draft));
        await a.SyncAsync();
        await byOps.SyncAsync();
        await using TestBrowser bySnapshot = await TestBrowser.OpenAsync(_server);
        await bySnapshot.SyncAsync();

        foreach (TestBrowser browser in new[] { byOps, bySnapshot })
        {
            await using LocalDbContext db = browser.CreateDbContext();
            Dictionary<Guid, Meal> meals = await db.Meals.AsNoTracking().Where(m => !m.Deleted).ToDictionaryAsync(m => m.Id, Ct);
            Assert.Equal(draft with { ServesPerBatch = 6 }, meals[bolognese].Draft());
            Assert.Equal(new MealDraft("Pizza", null, MealKind.FixedCost, 3, 4500, null), meals[pizza].Draft());
        }
    }

    [Fact]
    public async Task Two_browsers_editing_different_fields_of_one_meal_offline_both_keep_their_edit()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Guid meal = Guid.CreateVersion7();
        MealDraft draft = new("Risoni", null, MealKind.Cooked, 4, null, null);
        await a.WriteAsync(SyncedEntities.Meal, meal, OpKind.Create, draft.FieldsChangedFrom(null));
        await a.SyncAsync();
        await b.SyncAsync();

        a.Now = a.Now.AddMinutes(1);
        await a.WriteAsync(SyncedEntities.Meal, meal, OpKind.Update, (draft with { GroupId = CatalogueSeed.MealGroup(0) }).FieldsChangedFrom(draft));
        b.Now = b.Now.AddMinutes(2);
        await b.WriteAsync(SyncedEntities.Meal, meal, OpKind.Update, (draft with { ServesPerBatch = 8 }).FieldsChangedFrom(draft));
        await b.SyncAsync();
        await a.SyncAsync();
        await b.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            await using LocalDbContext db = browser.CreateDbContext();
            Meal row = await db.Meals.AsNoTracking().SingleAsync(m => m.Id == meal, Ct);
            Assert.Equal(((Guid?)CatalogueSeed.MealGroup(0), 8), (row.GroupId, row.ServesPerBatch));
        }
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

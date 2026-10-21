using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Meals;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Products;
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

    [Fact]
    public async Task Ingredients_added_on_two_browsers_offline_both_survive_and_both_cost_the_meal_the_same()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);

        // Mince at Costco, $24.99 for 2 kg; an onion with no price.
        Guid mince = await a.CreateIngredientAsync("beef mince", BaseUnit.Gram,
            new OpField(IngredientRules.UsualStoreField, OpValues.FormatId(CatalogueSeed.Costco)));
        Guid onion = await a.CreateIngredientAsync("onion", BaseUnit.Each);
        Guid product = Guid.CreateVersion7();
        await a.WriteAsync(SyncedEntities.Product, product, OpKind.Create,
            new ProductDraft("Beef mince 8x250g", CatalogueSeed.Costco, null, 2000, mince).FieldsChangedFrom(null));
        await a.WriteAsync(SyncedEntities.PriceObservation, Guid.CreateVersion7(), OpKind.Create,
            new PriceEntry(product, 2499, new DateOnly(2026, 10, 14), PriceSource.Manual, null).Fields());
        Guid meal = Guid.CreateVersion7();
        await a.WriteAsync(SyncedEntities.Meal, meal, OpKind.Create, new MealDraft("Bolognese", null, MealKind.Cooked, 4, null, null).FieldsChangedFrom(null));
        await a.SyncAsync();
        await b.SyncAsync();

        // Offline, each adds a line to the same meal.
        // Lines list in the order they were added - their GUIDv7 ids' - so these are a millisecond apart on purpose.
        Guid minceLine = Guid.CreateVersion7(DateTimeOffset.UtcNow), onionLine = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddMilliseconds(1));
        await a.WriteAsync(SyncedEntities.MealIngredient, minceLine, OpKind.Create, new MealIngredientDraft(meal, mince, 500, "browned").FieldsChangedFrom(null));
        await b.WriteAsync(SyncedEntities.MealIngredient, onionLine, OpKind.Create, new MealIngredientDraft(meal, onion, null, null).FieldsChangedFrom(null));
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            await using LocalDbContext db = browser.CreateDbContext();
            MealCostBook book = await MealCostBook.LoadAsync(db, Ct);
            Meal row = await db.Meals.AsNoTracking().SingleAsync(m => m.Id == meal, Ct);
            Assert.Equal([minceLine, onionLine], book.LinesOf(meal).Select(l => l.Id));
            MealCost cost = book.CostOf(row);
            Assert.Equal(new Cost(625, 1), cost.Batch);       // 500 g of $24.99 / 2 kg; the onion has no quantity or price
            Assert.Equal(new Cost(156, 1), cost.PerServe);
        }

        // B gives the onion a quantity, A deletes nothing; with no price it is still incomplete. Then a price for it.
        b.Now = b.Now.AddMinutes(1);
        await b.WriteAsync(SyncedEntities.MealIngredient, onionLine, OpKind.Update, [new OpField(MealIngredientRules.QuantityField, "1")]);
        Guid onions = Guid.CreateVersion7();
        await b.WriteAsync(SyncedEntities.Product, onions, OpKind.Create,
            new ProductDraft("Brown onions 1kg", CatalogueSeed.Woolworths, null, 6, onion).FieldsChangedFrom(null));
        await b.WriteAsync(SyncedEntities.PriceObservation, Guid.CreateVersion7(), OpKind.Create,
            new PriceEntry(onions, 450, new DateOnly(2026, 10, 20), PriceSource.Manual, null).Fields());
        await b.SyncAsync();
        await a.SyncAsync();

        await using (LocalDbContext db = a.CreateDbContext())
        {
            MealCostBook book = await MealCostBook.LoadAsync(db, Ct);
            MealCost cost = book.CostOf(await db.Meals.AsNoTracking().SingleAsync(m => m.Id == meal, Ct));
            Assert.Equal(new Cost(625 + 75, 0), cost.Batch);  // an onion is $4.50 / 6
            Assert.Equal(new Cost(175, 0), cost.PerServe);
        }

        // A new browser by snapshot holds the same lines, every field.
        await using TestBrowser bySnapshot = await TestBrowser.OpenAsync(_server);
        await bySnapshot.SyncAsync();
        await using (LocalDbContext db = bySnapshot.CreateDbContext())
        {
            Dictionary<Guid, MealIngredient> lines = await db.MealIngredients.AsNoTracking().ToDictionaryAsync(l => l.Id, Ct);
            Assert.Equal(new MealIngredientDraft(meal, mince, 500, "browned"), lines[minceLine].Draft());
            Assert.Equal(new MealIngredientDraft(meal, onion, 1, null), lines[onionLine].Draft());
        }
    }

    [Fact]
    public async Task A_line_whose_ingredient_was_deleted_elsewhere_survives_and_costs_as_unpriced()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Guid cheese = await a.CreateIngredientAsync("grated cheese", BaseUnit.Gram);
        Guid meal = Guid.CreateVersion7();
        await a.WriteAsync(SyncedEntities.Meal, meal, OpKind.Create, new MealDraft("Nachos", null, MealKind.Simple, 1, null, null).FieldsChangedFrom(null));
        await a.SyncAsync();
        await b.SyncAsync();

        // The race the ingredient editor's refusal cannot stop: B deletes the cheese while A, offline, adds it.
        await b.WriteAsync(SyncedEntities.Ingredient, cheese, OpKind.Delete, []);
        Guid line = Guid.CreateVersion7();
        await a.WriteAsync(SyncedEntities.MealIngredient, line, OpKind.Create, new MealIngredientDraft(meal, cheese, 50, null).FieldsChangedFrom(null));
        await b.SyncAsync();
        await a.SyncAsync();
        await b.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            await using LocalDbContext db = browser.CreateDbContext();
            MealCostBook book = await MealCostBook.LoadAsync(db, Ct);
            Assert.Null(book.Ingredient(cheese));
            Assert.Equal([line], book.LinesOf(meal).Select(l => l.Id));
            MealCost cost = book.CostOf(await db.Meals.AsNoTracking().SingleAsync(m => m.Id == meal, Ct));
            Assert.Equal(new Cost(0, 1), cost.PerServe);
            Assert.Equal(LineState.Unpriced, Assert.Single(cost.Lines).State);
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

using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.LocalData.Import;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Import;
using AshersLab.MealPlanner.Shared.Data.Planning;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Import;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using AshersLab.MealPlanner.Tests.Grpc;
using AshersLab.MealPlanner.Tests.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Tests.Import;

/// <summary>The import written through the op log of a real browser database, synced through the real host.</summary>
public sealed class ImportSyncTests : IAsyncLifetime
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

    private static readonly DateOnly Today = new(2026, 10, 21);

    private static async Task<ParsedSheet> ParseAsync(TestBrowser browser)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        ImportKnown known = await ImportKnownReader.ReadAsync(db, Ct);
        return SpreadsheetLayout.Parse(XlsxReader.Read(SyntheticSheet.Xlsx(SyntheticSheet.Both())), known.StoreWords);
    }

    private static async Task<ImportPlan> ImportAsync(TestBrowser browser, ImportChoices choices, ParsedSheet? sheet = null)
    {
        sheet ??= await ParseAsync(browser);
        await using LocalDbContext db = await browser.Contexts.CreateDbContextAsync(Ct);
        return await SheetImportWriter.WriteAsync(db, browser.DeviceId, browser.Now, Today, sheet, choices, Ct);
    }

    private static async Task<(int Ingredients, int Meals, int Lines, int Products, int Prices, int Groups)> CountsAsync(TestBrowser browser)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return (await db.Ingredients.CountAsync(Ct), await db.Meals.CountAsync(Ct), await db.MealIngredients.CountAsync(Ct),
            await db.Products.CountAsync(Ct), await db.PriceObservations.CountAsync(Ct), await db.MealGroups.CountAsync(Ct));
    }

    [Fact]
    public async Task An_import_reaches_another_browser_and_a_second_run_on_either_writes_nothing()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();

        ImportPlan first = await ImportAsync(a, new ImportChoices { HouseholdStore = SheetImportTests.Costco });
        Assert.Equal(17, first.NewIngredients.Count);
        Assert.Equal(first.Edits.Count, await a.PendingAsync());
        await a.SyncAsync();
        await b.SyncAsync();

        Assert.Equal(await CountsAsync(a), await CountsAsync(b));
        // 7 meal ideas and the plan's 7 single-serve meals.
        Assert.Equal((17, 14, 15), ((await CountsAsync(b)).Ingredients, (await CountsAsync(b)).Meals, (await CountsAsync(b)).Lines));
        Assert.Equal(0, await a.PendingAsync());
        // The plan too: its template by its fixed id, every entry with its choices.
        await using (LocalDbContext db = b.CreateDbContext())
        {
            PlanBook plans = await PlanBook.LoadAsync(db, Ct);
            Assert.Equal(2, plans.Template(SheetImport.PlanTemplateId)!.WeekCount);
            Assert.Equal(first.PlanCounts.Entries, plans.EntriesOf(SheetImport.PlanTemplateId).Count());
            Assert.All(plans.EntriesOf(SheetImport.PlanTemplateId), e => Assert.NotEmpty(plans.ChoicesOf(e.Id)));
        }

        ImportPlan againOnA = await ImportAsync(a, new ImportChoices { HouseholdStore = SheetImportTests.Costco });
        ImportPlan againOnB = await ImportAsync(b, new ImportChoices { HouseholdStore = SheetImportTests.Costco });
        Assert.True(againOnA.NothingToDo, string.Join("; ", againOnA.Edits.Select(e => e.Entity)));
        Assert.True(againOnB.NothingToDo);
        Assert.Equal(0, await b.PendingAsync());
    }

    [Fact]
    public async Task A_plan_deleted_after_the_import_stays_deleted_and_so_do_its_meals()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();
        await ImportAsync(a, new ImportChoices { HouseholdStore = SheetImportTests.Costco });

        Guid porridge;
        await using (LocalDbContext db = a.CreateDbContext())
            porridge = (await db.Meals.SingleAsync(m => m.Name == "Porridge", Ct)).Id;
        a.Now = a.Now.AddSeconds(1);
        await a.WriteAsync(SyncedEntities.PlanTemplate, SheetImport.PlanTemplateId, OpKind.Delete, []);
        await a.WriteAsync(SyncedEntities.Meal, porridge, OpKind.Delete, []);
        await a.SyncAsync();

        // A fresh browser, so what it compares with is what the server holds - tombstones included.
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        await b.SyncAsync();
        ImportPlan again = await ImportAsync(b, new ImportChoices { HouseholdStore = SheetImportTests.Costco });
        Assert.True(again.NothingToDo, string.Join("; ", again.Edits.Select(e => e.Entity)));
        Assert.Contains("The plan: deleted here before, so not imported again.", again.LeftOut);
    }

    [Fact]
    public async Task A_meal_deleted_before_the_plans_first_import_is_not_made_again_by_it_nor_is_a_typo_of_one()
    {
        // Before any import: two meals made and deleted - one by the plan's exact name, one spelt one letter off.
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();
        Guid porridge = Guid.CreateVersion7(), eatingOut = Guid.CreateVersion7();
        foreach ((Guid id, string name) in new[] { (porridge, "Porridge"), (eatingOut, "Eatting out") })
        {
            await a.WriteAsync(SyncedEntities.Meal, id, OpKind.Create,
                new Shared.Domain.Meals.MealDraft(name, null, Shared.Domain.Meals.MealKind.Simple, 1, null, null).FieldsChangedFrom(null));
            a.Now = a.Now.AddSeconds(1);
            await a.WriteAsync(SyncedEntities.Meal, id, OpKind.Delete, []);
        }
        await a.SyncAsync();

        // A fresh browser imports the plan for the first time, with the review's defaults.
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        await b.SyncAsync();
        ImportPlan plan = await ImportAsync(b, new ImportChoices { HouseholdStore = SheetImportTests.Costco });

        Assert.True(plan.PlanCounts.Entries > 0);
        Assert.Contains("Porridge (Plan C3): that meal was deleted here, so it is left out of the plan.", plan.LeftOut);
        Assert.Contains("Eating out (Plan B5): read as Eatting out, which was deleted here, so it is left out of the plan.", plan.LeftOut);
        await b.SyncAsync();
        await using LocalDbContext db = b.CreateDbContext();
        Assert.Empty(await db.Meals.Where(m => !m.Deleted && (m.Name == "Porridge" || m.Name == "Eating out")).ToListAsync(Ct));
    }

    [Fact]
    public async Task Units_arrive_marked_as_guesses_and_a_meals_ingredients_keep_the_sheets_order()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();
        await ImportAsync(a, new ImportChoices());
        await a.SyncAsync();
        await b.SyncAsync();

        await using LocalDbContext db = b.CreateDbContext();
        Assert.All(await db.Ingredients.ToListAsync(Ct), i => Assert.True(i.UnitGuessed, i.Name));
        Assert.Equal(BaseUnit.Each, (await db.Ingredients.SingleAsync(i => i.Name == "Eggs", Ct)).BaseUnit);

        Guid shepherd = (await db.Meals.SingleAsync(m => m.Name == "Shepherd pie", Ct)).Id;
        Dictionary<Guid, string> names = await db.Ingredients.ToDictionaryAsync(i => i.Id, i => i.Name, Ct);
        List<string> lines = [.. (await db.MealIngredients.Where(l => l.MealId == shepherd).ToListAsync(Ct)).OrderBy(l => l.Id).Select(l => names[l.IngredientId])];
        Assert.Equal(["Lamb mince", "Potatoes", "Peas", "Gravy"], lines);
        Assert.All(await db.MealIngredients.ToListAsync(Ct), l => Assert.Null(l.Quantity));
    }

    [Fact]
    public async Task A_later_download_with_more_prices_adds_only_those_prices()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();
        await ImportAsync(a, new ImportChoices());
        var before = await CountsAsync(a);

        // The planner has since filled in a price for beef mince, and changed lamb's.
        SheetGrid main = SyntheticSheet.Main();
        Assert.Null(main.At(17, 3));
        SheetGrid later = new(main.Name, main.Cells
            .Select(c => (c.Row, c.Column) == (16, 3) ? c with { Text = "26.49/2kg" } : c)
            .Append(new SheetCell(17, 3, "19.99/1kg", false)));
        ParsedSheet sheet = SpreadsheetLayout.Parse([later, SyntheticSheet.List()], ["Costco", "Woolworths", "Woolies"]);

        ImportPlan second = await ImportAsync(a, new ImportChoices(), sheet);

        Assert.Equal((0, 0, 0, 1, 2), (second.NewIngredients.Count, second.NewMeals, second.NewMealIngredients, second.NewProducts, second.NewPrices));
        var after = await CountsAsync(a);
        Assert.Equal(before.Prices + 2, after.Prices);
        Assert.Equal(before.Ingredients, after.Ingredients);
    }

    [Fact]
    public async Task What_the_planner_changed_or_deleted_after_an_import_stays_that_way_on_the_next()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();
        await ImportAsync(a, new ImportChoices());

        Dictionary<Guid, string> ingredients = await a.IngredientsAsync();
        Guid gravy = ingredients.Single(i => i.Value == "Gravy").Key;
        Guid chips = ingredients.Single(i => i.Value == "Popcorn").Key;
        a.Now = a.Now.AddSeconds(1);
        await a.WriteAsync(SyncedEntities.Ingredient, gravy, OpKind.Update,
            new IngredientDraft("Gravy granules", BaseUnit.Gram, null, null, false).FieldsChangedFrom(new("Gravy", BaseUnit.Gram, null, null, false, true)));
        await a.DeleteAsync(chips);

        ImportPlan again = await ImportAsync(a, new ImportChoices());

        // "Gravy" is new again - the planner renamed theirs - and Popcorn stays deleted.
        Assert.Equal(["Gravy"], again.NewIngredients.Select(i => i.Name));
        Assert.Contains("Popcorn: deleted here before, so not added again.", again.LeftOut);
        Assert.False((await a.IngredientRowsAsync())[gravy].UnitGuessed);
    }

    [Fact]
    public async Task Nothing_is_written_when_one_edit_would_be_refused()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();
        await using LocalDbContext db = await a.Contexts.CreateDbContextAsync(Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => AshersLab.MealPlanner.LocalData.Sync.LocalOps.WriteAllAsync(db, a.DeviceId, a.Now,
        [
            new(SyncedEntities.Ingredient, Guid.CreateVersion7(), OpKind.Create,
                [new OpField(IngredientRules.NameField, "Fine"), new OpField(IngredientRules.BaseUnitField, "Gram")]),
            new(SyncedEntities.Ingredient, Guid.CreateVersion7(), OpKind.Create, [new OpField(IngredientRules.NameField, "No unit")]),
        ], Ct));

        Assert.Empty(await a.IngredientsAsync());
        Assert.Equal(0, await a.PendingAsync());
    }
}

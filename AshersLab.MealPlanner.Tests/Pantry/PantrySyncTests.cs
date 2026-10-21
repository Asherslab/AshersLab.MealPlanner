using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Pantry;
using AshersLab.MealPlanner.Shared.Data.Planning;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Pantry;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using AshersLab.MealPlanner.Tests.Grpc;
using AshersLab.MealPlanner.Tests.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Tests.Pantry;

/// <summary>
/// The pantry from real browser databases, synced through the real host: stock drawn down by the calendar on every
/// device, a skip giving it back, counts and purchases from two phones offline, a staple's state, and an undo.
/// </summary>
public sealed class PantrySyncTests : IAsyncLifetime
{
    private readonly MealPlannerHost _host = new();
    private ISyncService _server = null!;

    private static readonly Guid Breakfast = CatalogueSeed.Slot(0);
    private static readonly Guid Dinner    = CatalogueSeed.Slot(4);

    /// <summary>Monday 26 October 2026.</summary>
    private static readonly DateOnly Mon = new(2026, 10, 26);

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

    private static async Task<Guid> MealAsync(TestBrowser browser, string name, MealKind kind, params (Guid Ingredient, decimal? Quantity)[] lines)
    {
        Guid meal = NextId(browser);
        await browser.WriteAsync(SyncedEntities.Meal, meal, OpKind.Create, new MealDraft(name, null, kind, 4, null, null).FieldsChangedFrom(null));
        foreach ((Guid ingredient, decimal? quantity) in lines)
            await browser.WriteAsync(SyncedEntities.MealIngredient, NextId(browser), OpKind.Create,
                new MealIngredientDraft(meal, ingredient, quantity, null).FieldsChangedFrom(null));
        return meal;
    }

    private static async Task<Guid> EntryAsync(TestBrowser browser, DateOnly day, Guid slot, Guid meal)
    {
        Guid id = NextId(browser);
        await browser.WriteAsync(SyncedEntities.CalendarEntry, id, OpKind.Create,
        [
            new OpField(CalendarEntryRules.DateField, OpValues.FormatDate(day)),
            new OpField(CalendarEntryRules.SlotField, OpValues.FormatId(slot)),
            new OpField(CalendarEntryRules.MealField, OpValues.FormatId(meal)),
        ]);
        return id;
    }

    private static async Task<Guid> MoveAsync(TestBrowser browser, Guid ingredient, decimal quantity, StockReason reason, DateOnly day)
    {
        Guid id = NextId(browser);
        await browser.WriteAsync(SyncedEntities.StockMovement, id, OpKind.Create, new StockEntry(ingredient, quantity, day, reason).Fields());
        return id;
    }

    private static async Task<PantryBook> PantryAsync(TestBrowser browser, DateOnly through)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await PantryBook.LoadAsync(db, Mon, through, Ct);
    }

    private static async Task PeopleAsync(TestBrowser a)
    {
        foreach (string name in new[] { "Sam", "Alex" })
            await a.WriteAsync(SyncedEntities.Person, NextId(a), OpKind.Create,
                [new OpField(PersonRules.NameField, name), new OpField(PersonRules.ColourField, PersonRules.Palette[name.Length % 2])]);
    }

    [Fact]
    public async Task Meals_on_the_calendar_draw_stock_down_on_every_browser_and_a_skip_gives_it_back()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        await PeopleAsync(a);
        Guid mince = await a.CreateIngredientAsync("Mince");
        Guid bread = await a.CreateIngredientAsync("Bread", BaseUnit.Each);
        Guid oil   = await a.CreateIngredientAsync("Oil", BaseUnit.Millilitre, new OpField(IngredientRules.IsStapleField, "true"));
        Guid onion = await a.CreateIngredientAsync("Onion", BaseUnit.Each);
        Guid stew  = await MealAsync(a, "Stew", MealKind.Cooked, (mince, 500), (oil, 15), (onion, 1));
        // A simple meal's lines are one serve's: two people eat it, so it uses four slices.
        Guid toast = await MealAsync(a, "Toast", MealKind.Simple, (bread, 2), (mince, null));
        Guid soup  = await MealAsync(a, "Leftover stew", MealKind.Leftovers, (mince, 100));

        await MoveAsync(a, mince, 2000, StockReason.Bought, Mon);
        await MoveAsync(a, bread, 10, StockReason.Bought, Mon);
        await MoveAsync(a, oil, 1000, StockReason.Bought, Mon);
        await EntryAsync(a, Mon, Dinner, stew);
        await EntryAsync(a, Mon.AddDays(1), Breakfast, toast);
        await EntryAsync(a, Mon.AddDays(2), Dinner, soup);
        Guid thursday = await EntryAsync(a, Mon.AddDays(3), Dinner, stew);
        await a.SyncAsync();
        await b.SyncAsync();

        PantryBook onB = await PantryAsync(b, Mon.AddDays(6));
        Assert.Equal(2000, onB.At(mince, Mon).Quantity);
        Assert.Equal(1500, onB.Ledger.After(mince, Mon).Quantity);
        // Projected: Thursday's stew takes another 500 g; leftovers and a line with no amount take nothing.
        Assert.Equal(1000, onB.At(mince, Mon.AddDays(4)).Quantity);
        Assert.Equal(6, onB.At(bread, Mon.AddDays(2)).Quantity);
        Assert.Equal([Mon, Mon.AddDays(3)], onB.UsesOf(mince).Select(u => u.Day));
        // A staple is never drawn down, nor tracked by amount.
        Assert.Equal(StockLevel.NotTracked, onB.At(oil, Mon.AddDays(4)));
        Assert.Empty(onB.UsesOf(oil));
        // Never counted: what the plan takes from it is still listed ("coming up"), but there is no stock to draw down.
        Assert.Equal(2, onB.UsesOf(onion).Count());
        Assert.Equal(StockLevel.NotTracked, onB.At(onion, Mon.AddDays(4)));

        // A: "we didn't have the stew on Thursday". B gets the 500 g back - no stock op was written for it.
        PlannedEntry entry = (await CalendarAsync(a)).On(Mon.AddDays(3)).Single(e => e.Id == thursday);
        a.Now = a.Now.AddSeconds(1);
        await a.WriteAsync(SyncedEntities.CalendarEntry, thursday, OpKind.Update,
            new CalendarEntryDraft(entry.MealId, entry.People, true, entry.Day, entry.SlotId).ChangesFrom(entry.Now));
        await a.SyncAsync();
        await b.SyncAsync();
        Assert.Equal(1500, (await PantryAsync(b, Mon.AddDays(6))).At(mince, Mon.AddDays(4)).Quantity);
        await using LocalDbContext db = b.CreateDbContext();
        Assert.Equal(3, await db.StockMovements.CountAsync(Ct));
    }

    [Fact]
    public async Task Stock_read_after_the_last_movement_has_the_meals_eaten_since_taken_off()
    {
        // The pantry page and the shopping list read from today, which is usually days after the last shop or count.
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        Guid mince = await a.CreateIngredientAsync("Mince");
        Guid stew  = await MealAsync(a, "Stew", MealKind.Cooked, (mince, 500));
        await MoveAsync(a, mince, 2000, StockReason.Bought, Mon.AddDays(-7));
        await EntryAsync(a, Mon.AddDays(-3), Dinner, stew);
        await EntryAsync(a, Mon.AddDays(1), Dinner, stew);

        PantryBook pantry = await PantryAsync(a, Mon.AddDays(6));
        Assert.Equal(new StockLevel(1500, StockState.InStock), pantry.At(mince, Mon));
        Assert.Equal(1000, pantry.At(mince, Mon.AddDays(2)).Quantity);
    }

    [Fact]
    public async Task Counts_and_purchases_made_offline_on_two_phones_end_the_same_everywhere()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Guid mayo = await a.CreateIngredientAsync("Mayo");
        Guid soy  = await a.CreateIngredientAsync("Soy sauce", BaseUnit.Millilitre, new OpField(IngredientRules.IsStapleField, "true"));
        await MoveAsync(a, mayo, 500, StockReason.Bought, Mon);
        await a.SyncAsync();
        await b.SyncAsync();

        // Both offline on Wednesday. A looks in the fridge: about half a jar. B, later, buys another jar - and also says
        // it is down to 100 g before that (a count, then a purchase, on B).
        b.Now = a.Now.AddMinutes(5);
        await MoveAsync(a, mayo, 250, StockReason.Counted, Mon.AddDays(2));
        await MoveAsync(b, mayo, 100, StockReason.Counted, Mon.AddDays(2));
        await MoveAsync(b, mayo, 500, StockReason.Bought, Mon.AddDays(2));
        // A says soy sauce is running low; B, later, that it is out.
        a.Now = a.Now.AddSeconds(1);
        await a.WriteAsync(SyncedEntities.Ingredient, soy, OpKind.Update, [new OpField(IngredientRules.StapleStockField, "RunningLow")]);
        b.Now = b.Now.AddSeconds(1);
        await b.WriteAsync(SyncedEntities.Ingredient, soy, OpKind.Update, [new OpField(IngredientRules.StapleStockField, "Out")]);

        // B reconnects first, then A.
        await b.SyncAsync();
        await a.SyncAsync();
        await b.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            PantryBook pantry = await PantryAsync(browser, Mon.AddDays(6));
            // In the order they were made: A's 250, B's 100, B's +500. Nothing merged; three rows.
            Assert.Equal(new StockLevel(600, StockState.InStock), pantry.At(mayo, Mon.AddDays(2)));
            Assert.Equal(4, pantry.MovementsOf(mayo).Count());
            Assert.Equal(StapleStock.Out, pantry.Ingredients[soy].StapleStock);
        }
    }

    [Fact]
    public async Task Deleting_a_mistaken_movement_on_one_phone_takes_it_off_the_other()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Guid eggs = await a.CreateIngredientAsync("Eggs", BaseUnit.Each);
        await MoveAsync(a, eggs, 12, StockReason.Bought, Mon);
        Guid mistake = await MoveAsync(a, eggs, 0, StockReason.Counted, Mon.AddDays(1));
        await a.SyncAsync();
        await b.SyncAsync();
        Assert.Equal(StockState.Out, (await PantryAsync(b, Mon.AddDays(6))).At(eggs, Mon.AddDays(1)).State);

        // B: undo.
        b.Now = b.Now.AddMinutes(1);
        await b.WriteAsync(SyncedEntities.StockMovement, mistake, OpKind.Delete, []);
        await b.SyncAsync();
        await a.SyncAsync();
        Assert.Equal(new StockLevel(12, StockState.InStock), (await PantryAsync(a, Mon.AddDays(6))).At(eggs, Mon.AddDays(1)));
    }

    [Fact]
    public async Task A_movement_whose_amount_does_not_fit_its_reason_is_refused_before_it_is_queued()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        Guid eggs = await a.CreateIngredientAsync("Eggs", BaseUnit.Each);
        await Assert.ThrowsAsync<ArgumentException>(() => a.WriteAsync(SyncedEntities.StockMovement, NextId(a), OpKind.Create,
            new StockEntry(eggs, -12, Mon, StockReason.Bought).Fields()));
        await using LocalDbContext db = a.CreateDbContext();
        Assert.Equal(0, await db.StockMovements.CountAsync(Ct));
    }

    private static async Task<CalendarBook> CalendarAsync(TestBrowser browser)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await CalendarBook.LoadAsync(db, Mon.AddDays(-7), Mon.AddDays(14), Ct);
    }
}

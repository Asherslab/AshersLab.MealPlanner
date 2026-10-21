using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Pantry;
using AshersLab.MealPlanner.Shared.Data.Shopping;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Pantry;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Products;
using AshersLab.MealPlanner.Shared.Domain.Shopping;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using AshersLab.MealPlanner.Tests.Grpc;
using AshersLab.MealPlanner.Tests.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Tests.Shopping;

/// <summary>
/// The shopping list from real browser databases, synced through the real host: lines from the calendar less the pantry,
/// whole packs, staples, the Spend and Eaten views, ticks made offline on a phone reaching the desktop, what a tick writes
/// to the pantry and what unticking takes back.
/// </summary>
public sealed class ShoppingSyncTests : IAsyncLifetime
{
    private readonly MealPlannerHost _host = new();
    private ISyncService _server = null!;

    private static readonly Guid Breakfast = CatalogueSeed.Slot(0);
    private static readonly Guid Lunch     = CatalogueSeed.Slot(2);
    private static readonly Guid Dinner    = CatalogueSeed.Slot(4);
    private static readonly Guid Meat      = CatalogueSeed.Category(0);
    private static readonly Guid Carbs     = CatalogueSeed.Category(4);
    private static readonly Guid Pantry    = CatalogueSeed.Category(5);
    private static readonly Guid Fridge    = CatalogueSeed.Category(6);

    /// <summary>Monday 26 October 2026: the list is Monday to Sunday.</summary>
    private static readonly DateOnly Mon = new(2026, 10, 26);
    private static readonly DateOnly Sun = Mon.AddDays(6);

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

    private sealed record Kitchen(Guid Mince, Guid Bread, Guid Mayo, Guid Soy, Guid Oil, Guid MinceProduct);

    /// <summary>
    /// A week: stew Monday and Thursday (500 g of mince each), its leftovers Wednesday, toast for two Tuesday (2 slices a
    /// serve), a salad Friday (300 g of mayo, unpriced), pizza Saturday ($30 feeding 3), a stew skipped Sunday. 600 g of mince
    /// bought the Saturday before; 500 g of mayo counted the Sunday before. Soy sauce is running low; oil is fine.
    /// </summary>
    private static async Task<Kitchen> KitchenAsync(TestBrowser a)
    {
        foreach (string name in new[] { "Sam", "Alex" })
            await a.WriteAsync(SyncedEntities.Person, NextId(a), OpKind.Create,
                [new OpField(PersonRules.NameField, name), new OpField(PersonRules.ColourField, PersonRules.Palette[name.Length % 2])]);

        Guid mince = await a.CreateIngredientAsync("Mince", BaseUnit.Gram, Usual(CatalogueSeed.Costco), In(Meat));
        Guid bread = await a.CreateIngredientAsync("Bread", BaseUnit.Each, Usual(CatalogueSeed.Woolworths), In(Carbs));
        Guid mayo  = await a.CreateIngredientAsync("Mayo", BaseUnit.Gram, Usual(CatalogueSeed.Woolworths), In(Fridge));
        Guid soy   = await a.CreateIngredientAsync("Soy sauce", BaseUnit.Millilitre, Usual(CatalogueSeed.Woolworths), In(Pantry),
            new OpField(IngredientRules.IsStapleField, "true"));
        Guid oil   = await a.CreateIngredientAsync("Oil", BaseUnit.Millilitre, Usual(CatalogueSeed.Woolworths), In(Pantry),
            new OpField(IngredientRules.IsStapleField, "true"));
        a.Now = a.Now.AddSeconds(1);
        await a.WriteAsync(SyncedEntities.Ingredient, soy, OpKind.Update, [new OpField(IngredientRules.StapleStockField, "RunningLow")]);

        Guid minceProduct = await ProductAsync(a, "Beef mince 2kg", CatalogueSeed.Costco, 2000, mince, 2499);
        // A dearer, bigger mince elsewhere: never chosen, the usual store has one.
        await ProductAsync(a, "Mince 5kg", CatalogueSeed.Woolworths, 5000, mince, 9000);
        await ProductAsync(a, "Bread 10 slices", CatalogueSeed.Woolworths, 10, bread, 300);
        await ProductAsync(a, "Soy sauce 250ml", CatalogueSeed.Woolworths, 250, soy, 400);
        await ProductAsync(a, "Oil 1L", CatalogueSeed.Woolworths, 1000, oil, 800);

        Guid stew   = await MealAsync(a, "Stew", MealKind.Cooked, 4, null, (mince, 500), (oil, 15));
        Guid soup   = await MealAsync(a, "Leftover stew", MealKind.Leftovers, 1, null);
        Guid toast  = await MealAsync(a, "Toast", MealKind.Simple, 1, null, (bread, 2));
        Guid salad  = await MealAsync(a, "Salad", MealKind.Cooked, 2, null, (mayo, 300));
        Guid pizza  = await MealAsync(a, "Pizza", MealKind.FixedCost, 3, 3000);

        await EntryAsync(a, Mon, Dinner, stew);
        await EntryAsync(a, Mon.AddDays(1), Breakfast, toast);
        await EntryAsync(a, Mon.AddDays(2), Lunch, soup);
        await EntryAsync(a, Mon.AddDays(3), Dinner, stew);
        await EntryAsync(a, Mon.AddDays(4), Dinner, salad);
        await EntryAsync(a, Mon.AddDays(5), Dinner, pizza);
        Guid skipped = await EntryAsync(a, Sun, Dinner, stew);
        a.Now = a.Now.AddSeconds(1);
        await a.WriteAsync(SyncedEntities.CalendarEntry, skipped, OpKind.Update,
            [new OpField(CalendarEntryRules.StatusField, CalendarEntryRules.FormatStatus(CalendarStatus.Skipped))]);

        await a.WriteAsync(SyncedEntities.StockMovement, NextId(a), OpKind.Create, new StockEntry(mince, 600, Mon.AddDays(-2), StockReason.Bought).Fields());
        await a.WriteAsync(SyncedEntities.StockMovement, NextId(a), OpKind.Create, new StockEntry(mayo, 500, Mon.AddDays(-1), StockReason.Counted).Fields());
        return new Kitchen(mince, bread, mayo, soy, oil, minceProduct);
    }

    private static OpField Usual(Guid store) => new(IngredientRules.UsualStoreField, OpValues.FormatId(store));

    private static OpField In(Guid category) => new(IngredientRules.CategoryField, OpValues.FormatId(category));

    private static async Task<Guid> ProductAsync(TestBrowser a, string name, Guid store, decimal pack, Guid ingredient, long cents)
    {
        Guid product = NextId(a);
        await a.WriteAsync(SyncedEntities.Product, product, OpKind.Create, new ProductDraft(name, store, null, pack, ingredient).FieldsChangedFrom(null));
        await a.WriteAsync(SyncedEntities.PriceObservation, NextId(a), OpKind.Create,
            new PriceEntry(product, cents, new DateOnly(2026, 10, 20), PriceSource.Manual, null).Fields());
        return product;
    }

    private static async Task<Guid> MealAsync(TestBrowser a, string name, MealKind kind, int serves, long? fixedCents, params (Guid Ingredient, decimal Quantity)[] lines)
    {
        Guid meal = NextId(a);
        await a.WriteAsync(SyncedEntities.Meal, meal, OpKind.Create, new MealDraft(name, null, kind, serves, fixedCents, null).FieldsChangedFrom(null));
        foreach ((Guid ingredient, decimal quantity) in lines)
            await a.WriteAsync(SyncedEntities.MealIngredient, NextId(a), OpKind.Create, new MealIngredientDraft(meal, ingredient, quantity, null).FieldsChangedFrom(null));
        return meal;
    }

    private static async Task<Guid> EntryAsync(TestBrowser a, DateOnly day, Guid slot, Guid meal)
    {
        Guid id = NextId(a);
        await a.WriteAsync(SyncedEntities.CalendarEntry, id, OpKind.Create,
        [
            new OpField(CalendarEntryRules.DateField, OpValues.FormatDate(day)),
            new OpField(CalendarEntryRules.SlotField, OpValues.FormatId(slot)),
            new OpField(CalendarEntryRules.MealField, OpValues.FormatId(meal)),
        ]);
        return id;
    }

    private static async Task<ShoppingBook> ListAsync(TestBrowser browser, DateOnly? from = null, DateOnly? to = null)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await ShoppingBook.LoadAsync(db, from ?? Mon, to ?? Sun, Ct);
    }

    /// <summary>A tap on a line, as the page writes it: every op of the tick in one transaction.</summary>
    private static async Task TickAsync(TestBrowser browser, Guid ingredient, ShoppingTick to, DateOnly today)
    {
        ShoppingBook book = await ListAsync(browser);
        ShoppingLine line = book.Lines.Single(l => l.Ingredient.Id == ingredient);
        await using LocalDbContext db = browser.CreateDbContext();
        browser.Now = browser.Now.AddSeconds(1);
        List<ShoppingEdit> edits = await ShoppingTicking.EditsAsync(db, book, line, to, today, () => NextId(browser), Ct);
        await LocalOps.WriteAllAsync(db, browser.DeviceId, browser.Now, [.. edits.Select(e => new LocalEdit(e.Type, e.Id, e.Kind, e.Fields))], Ct);
    }

    private static async Task<List<StockMovement>> MovementsAsync(TestBrowser browser)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await db.StockMovements.AsNoTracking().Where(m => !m.Deleted).ToListAsync(Ct);
    }

    private static async Task<PantryBook> PantryAsync(TestBrowser browser)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await PantryBook.LoadAsync(db, Mon, Sun.AddDays(7), Ct);
    }

    [Fact]
    public async Task The_list_is_the_weeks_meals_less_the_pantry_in_whole_packs_by_store_then_category()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);
        await a.SyncAsync();
        await b.SyncAsync();

        ShoppingBook list = await ListAsync(b);
        ShoppingLine mince = list.Lines.Single(l => l.Ingredient.Id == k.Mince);
        // Two stews (the skipped one counts for nothing, leftovers use nothing): 1 kg, less the 600 g bought Saturday.
        Assert.Equal((LineNeed.Buy, 1000m, 600m, 400m), (mince.Plan.Need, mince.Plan.Needed, mince.Plan.Have, mince.Plan.Shortfall));
        Assert.Equal(new PackChoice(k.MinceProduct, CatalogueSeed.Costco, 2000, 1, 1600, 2499), mince.Plan.Pack);
        // Toast for two: 4 slices, never counted, one pack of 10.
        ShoppingLine bread = list.Lines.Single(l => l.Ingredient.Id == k.Bread);
        Assert.Equal((4m, 0m, 1, 6m), (bread.Plan.Needed, bread.Plan.Have, bread.Plan.Pack!.Packs, bread.Plan.Pack.Spare));
        // 300 g of the 500 g of mayo counted Sunday: in the pantry, not to buy.
        Assert.Equal([k.Mayo], list.InPantry.Select(l => l.Ingredient.Id));
        // Soy sauce running low: one bottle. Oil is fine: not on the list, though the stew uses it.
        Assert.Equal(LineNeed.Staple, list.Lines.Single(l => l.Ingredient.Id == k.Soy).Plan.Need);
        Assert.DoesNotContain(list.Lines, l => l.Ingredient.Id == k.Oil);
        Assert.All(list.Lines, l => Assert.Equal(ShoppingTick.None, l.Tick));

        // By usual store, then category in the categories' order.
        Assert.Equal(["Costco", "Woolworths"], list.Groups.Select(g => g.Store?.Name));
        Assert.Equal([(Meat, "Mince")], list.Groups[0].Categories.SelectMany(c => c.Lines.Select(l => (c.Category!.Id, l.Ingredient.Name))));
        Assert.Equal([(Carbs, "Bread"), (Pantry, "Soy sauce")], list.Groups[1].Categories.SelectMany(c => c.Lines.Select(l => (c.Category!.Id, l.Ingredient.Name))));

        // Spend: the packs ($24.99 + $3.00 + $4.00) and the pizza ($30 for 3 serves, eaten by 2: $20). Leftovers and the mayo
        // already in the pantry cost nothing.
        Assert.Equal(new Cost(2000, 0), list.FixedMeals);
        Assert.Equal(new Cost(2499 + 300 + 400 + 2000, 0), list.Spend);
        // Eaten: stew $6.37 a batch of 4 (500 g of mince $6.25, 15 ml of oil 12c) -> $1.59 a serve, 2 eat it Monday, 2
        // Wednesday's leftovers, 2 Thursday; toast 60c a serve for 2; the pizza $20; the salad's mayo is unpriced - one
        // missing, never a low number dressed as complete.
        Assert.Equal(new Cost(159 * 6 + 120 + 2000, 1), list.Eaten);
    }

    [Fact]
    public async Task Ticks_made_offline_on_a_phone_reach_the_desktop_and_put_the_whole_packs_in_the_pantry()
    {
        await using TestBrowser desktop = await TestBrowser.OpenAsync(_server);
        await using TestBrowser phone = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(desktop);
        await desktop.SyncAsync();
        await phone.SyncAsync();
        phone.Now = desktop.Now.AddMinutes(1);

        // In the shop, no reception, the Sunday before: the mince in the trolley, "we have bread", soy sauce in the trolley.
        await TickAsync(phone, k.Mince, ShoppingTick.Bought, Mon.AddDays(-1));
        await TickAsync(phone, k.Bread, ShoppingTick.HaveIt, Mon.AddDays(-1));
        await TickAsync(phone, k.Soy, ShoppingTick.Bought, Mon.AddDays(-1));
        // And something not on the plan.
        Guid bags = NextId(phone);
        await phone.WriteAsync(SyncedEntities.ShoppingListItem, bags, OpKind.Create,
            new ExtraItemDraft("Bin bags", CatalogueSeed.Woolworths, 1299).FieldsChangedFrom(ShoppingListRules.ListId(Mon, Sun), null));

        ShoppingBook onPhone = await ListAsync(phone);
        // Ticked bought, the mince is still a line to buy - the list reads the pantry without its own ticks - not "in the pantry".
        ShoppingLine mince = onPhone.Lines.Single(l => l.Ingredient.Id == k.Mince);
        Assert.Equal((LineNeed.Buy, ShoppingTick.Bought), (mince.Plan.Need, mince.Tick));
        Assert.Equal(ShoppingTick.HaveIt, onPhone.Lines.Single(l => l.Ingredient.Id == k.Bread).Tick);
        // The soy sauce is back to Have, and stays on the list, ticked.
        ShoppingLine soy = onPhone.Lines.Single(l => l.Ingredient.Id == k.Soy);
        Assert.Equal((StapleStock.Have, ShoppingTick.Bought), (soy.Ingredient.StapleStock, soy.Tick));
        // Spend: bread is no longer bought; the bags are.
        Assert.Equal(new Cost(2499 + 400 + 2000 + 1299, 0), onPhone.Spend);

        // What the pantry was told: the whole pack of mince (2 kg, not the 400 g short), and the 4 slices it lacked.
        List<StockMovement> written = await MovementsAsync(phone);
        StockMovement bought = written.Single(m => m.Reason == StockReason.Bought && m.ShoppingItemId is not null);
        Assert.Equal((k.Mince, 2000m, Mon.AddDays(-1), ShoppingItemRules.TickId(onPhone.ListId, k.Mince)),
            (bought.IngredientId, bought.Quantity, bought.OccurredOn, bought.ShoppingItemId!.Value));
        StockMovement haveIt = written.Single(m => m.Reason == StockReason.Adjusted);
        Assert.Equal((k.Bread, 4m), (haveIt.IngredientId, haveIt.Quantity));
        // The spare is in the pantry for next week: 600 + 2000 - two stews.
        Assert.Equal(1600, (await PantryAsync(phone)).At(k.Mince, Mon.AddDays(7)).Quantity);

        // Back online; the desktop sees it all.
        await phone.SyncAsync();
        await desktop.SyncAsync();
        ShoppingBook onDesktop = await ListAsync(desktop);
        Assert.Equal(onPhone.Lines.Select(l => (l.Ingredient.Id, l.Tick)), onDesktop.Lines.Select(l => (l.Ingredient.Id, l.Tick)));
        Assert.Equal(["Bin bags"], onDesktop.Extras.Select(e => e.Name));
        Assert.Equal(onPhone.Spend, onDesktop.Spend);
        Assert.Equal(1600, (await PantryAsync(desktop)).At(k.Mince, Mon.AddDays(7)).Quantity);

        // The desktop unticks the mince: its pack comes back out of the pantry, everywhere. And the soy sauce: running low again.
        await TickAsync(desktop, k.Mince, ShoppingTick.None, Mon);
        await TickAsync(desktop, k.Soy, ShoppingTick.None, Mon);
        await desktop.SyncAsync();
        await phone.SyncAsync();
        Assert.DoesNotContain(await MovementsAsync(phone), m => m.Reason == StockReason.Bought && m.ShoppingItemId is not null);
        Assert.Equal(StockState.ProbablyOut, (await PantryAsync(phone)).At(k.Mince, Mon.AddDays(7)).State);
        ShoppingBook after = await ListAsync(phone);
        Assert.Equal(ShoppingTick.None, after.Lines.Single(l => l.Ingredient.Id == k.Mince).Tick);
        Assert.Equal(StapleStock.RunningLow, after.Lines.Single(l => l.Ingredient.Id == k.Soy).Ingredient.StapleStock);
    }

    [Fact]
    public async Task Ticking_bought_then_have_it_then_bought_again_adds_one_pack_not_three()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);

        await TickAsync(a, k.Mince, ShoppingTick.Bought, Mon);
        await TickAsync(a, k.Mince, ShoppingTick.HaveIt, Mon);
        // "Have it" on mince the pantry thought had 600 g of the 1 kg: the 400 g it lacked, and the bought pack is gone.
        Assert.Equal([(StockReason.Adjusted, 400m)], (await MovementsAsync(a)).Where(m => m.ShoppingItemId is not null).Select(m => (m.Reason, m.Quantity)));
        await TickAsync(a, k.Mince, ShoppingTick.Bought, Mon);
        Assert.Equal([(StockReason.Bought, 2000m)], (await MovementsAsync(a)).Where(m => m.ShoppingItemId is not null).Select(m => (m.Reason, m.Quantity)));

        // "Have it" on a line the pantry already covers tells it nothing.
        await TickAsync(a, k.Mayo, ShoppingTick.HaveIt, Mon);
        Assert.DoesNotContain(await MovementsAsync(a), m => m.IngredientId == k.Mayo && m.ShoppingItemId is not null);

        await using LocalDbContext db = a.CreateDbContext();
        Assert.Equal(1, await db.ShoppingLists.CountAsync(Ct));
        Assert.Equal(2, await db.ShoppingListItems.CountAsync(Ct));
    }

    [Fact]
    public async Task Two_phones_ticking_the_same_line_offline_tick_one_row_and_the_later_tick_wins()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);
        await a.SyncAsync();
        await b.SyncAsync();

        // Both make this week's list offline; A ticks bread bought, B - a minute later - says they have it.
        b.Now = a.Now.AddMinutes(1);
        await TickAsync(a, k.Bread, ShoppingTick.Bought, Mon);
        await TickAsync(b, k.Bread, ShoppingTick.HaveIt, Mon);
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            await using LocalDbContext db = browser.CreateDbContext();
            Assert.Equal(1, await db.ShoppingLists.CountAsync(Ct));
            Assert.Equal(ShoppingTick.HaveIt, (await db.ShoppingListItems.SingleAsync(Ct)).Tick);
            Assert.Equal(ShoppingTick.HaveIt, (await ListAsync(browser)).Lines.Single(l => l.Ingredient.Id == k.Bread).Tick);
            // Both movements are rows, but stock follows the tick: B's "have it" (the 4 slices it lacked), not A's pack too.
            Assert.Equal(2, (await MovementsAsync(browser)).Count(m => m.ShoppingItemId is not null));
            Assert.Equal(4, (await PantryAsync(browser)).At(k.Bread, Mon).Quantity);
        }
    }

    [Fact]
    public async Task Two_phones_both_ticking_the_mince_bought_offline_put_one_pack_in_the_pantry_not_two()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);
        await a.SyncAsync();
        await b.SyncAsync();

        // Two people in the shop, no reception, both put the mince in a trolley and tick it.
        b.Now = a.Now.AddMinutes(1);
        await TickAsync(a, k.Mince, ShoppingTick.Bought, Mon);
        await TickAsync(b, k.Mince, ShoppingTick.Bought, Mon);
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            // Each phone wrote a pack; the line was bought once. 600 g + one 2 kg pack - two stews = 1.6 kg next week, not 3.6 kg.
            Assert.Equal(2, (await MovementsAsync(browser)).Count(m => m.Reason == StockReason.Bought && m.ShoppingItemId is not null));
            Assert.Equal(1600, (await PantryAsync(browser)).At(k.Mince, Mon.AddDays(7)).Quantity);
        }

        // A unticks: no pack at all, though B's movement was never A's to delete.
        await TickAsync(a, k.Mince, ShoppingTick.None, Mon);
        await a.SyncAsync();
        await b.SyncAsync();
        Assert.Equal(StockState.ProbablyOut, (await PantryAsync(b)).At(k.Mince, Mon.AddDays(7)).State);
    }

    [Fact]
    public async Task Spend_counts_an_unpriced_line_and_an_unpriced_extra_as_missing_and_a_line_with_no_pack_buys_its_shortfall()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);
        // 400 g of the counted mayo thrown out: 100 g left of the 300 g the salad needs. Mayo has no product at all.
        await a.WriteAsync(SyncedEntities.StockMovement, NextId(a), OpKind.Create, new StockEntry(k.Mayo, -400, Mon.AddDays(-1), StockReason.Adjusted).Fields());
        await a.WriteAsync(SyncedEntities.ShoppingListItem, NextId(a), OpKind.Create,
            new ExtraItemDraft("Candles", null, null).FieldsChangedFrom(ShoppingListRules.ListId(Mon, Sun), null));

        ShoppingBook list = await ListAsync(a);
        ShoppingLine mayo = list.Lines.Single(l => l.Ingredient.Id == k.Mayo);
        Assert.Equal((LineNeed.Buy, 200m, (PackChoice?)null), (mayo.Plan.Need, mayo.Plan.Shortfall, mayo.Plan.Pack));
        // The priced packs and the pizza, and two things nobody priced: "from $... + 2 unpriced", never a quietly low total.
        Assert.Equal(new Cost(2499 + 300 + 400 + 2000, 2), list.Spend);

        // Bought with no pack size known: the 200 g it was short goes into the pantry.
        await TickAsync(a, k.Mayo, ShoppingTick.Bought, Mon);
        StockMovement bought = (await MovementsAsync(a)).Single(m => m.IngredientId == k.Mayo && m.ShoppingItemId is not null);
        Assert.Equal((StockReason.Bought, 200m), (bought.Reason, bought.Quantity));
    }

    [Fact]
    public async Task A_staple_marked_out_is_on_the_list_and_unticking_leaves_out_alone()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);
        a.Now = a.Now.AddSeconds(1);
        await a.WriteAsync(SyncedEntities.Ingredient, k.Oil, OpKind.Update, [new OpField(IngredientRules.StapleStockField, "Out")]);

        ShoppingLine oil = (await ListAsync(a)).Lines.Single(l => l.Ingredient.Id == k.Oil);
        Assert.Equal((LineNeed.Staple, 1, true), (oil.Plan.Need, oil.Plan.Pack!.Packs, oil.Planned));

        // Bought: back to Have. Then someone says it is out after all, and the tick is undone: still out, not "running low".
        await TickAsync(a, k.Oil, ShoppingTick.Bought, Mon);
        Assert.Equal(StapleStock.Have, (await ListAsync(a)).Lines.Single(l => l.Ingredient.Id == k.Oil).Ingredient.StapleStock);
        a.Now = a.Now.AddSeconds(1);
        await a.WriteAsync(SyncedEntities.Ingredient, k.Oil, OpKind.Update, [new OpField(IngredientRules.StapleStockField, "Out")]);
        await TickAsync(a, k.Oil, ShoppingTick.None, Mon);
        Assert.Equal(StapleStock.Out, (await ListAsync(a)).Lines.Single(l => l.Ingredient.Id == k.Oil).Ingredient.StapleStock);
    }

    [Fact]
    public async Task A_line_ticked_and_then_dropped_from_the_plan_stays_on_the_list_ticked()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        Kitchen k = await KitchenAsync(a);
        await TickAsync(a, k.Bread, ShoppingTick.Bought, Mon);

        // Tuesday's toast is skipped after the bread is in the trolley.
        await using (LocalDbContext db = a.CreateDbContext())
        {
            Guid toast = (await db.CalendarEntries.AsNoTracking().ToListAsync(Ct)).Single(e => e.Date == Mon.AddDays(1)).Id;
            a.Now = a.Now.AddSeconds(1);
            await LocalOps.WriteAsync(db, a.DeviceId, a.Now, SyncedEntities.CalendarEntry, toast, OpKind.Update,
                [new OpField(CalendarEntryRules.StatusField, CalendarEntryRules.FormatStatus(CalendarStatus.Skipped))], Ct);
        }

        ShoppingBook list = await ListAsync(a);
        ShoppingLine bread = list.Lines.Single(l => l.Ingredient.Id == k.Bread);
        Assert.Equal((false, ShoppingTick.Bought, 0m), (bread.Planned, bread.Tick, bread.Plan.Needed));
        // What was bought stays bought: still a line to buy with its pack, under its store, and still in the Spend.
        Assert.Equal((LineNeed.Buy, 1, 300L), (bread.Plan.Need, bread.Plan.Pack!.Packs, bread.Plan.Pack.Cents));
        Assert.DoesNotContain(list.InPantry, l => l.Ingredient.Id == k.Bread);
        Assert.Equal(new Cost(2499 + 300 + 400 + 2000, 0), list.Spend);

        // Changed to "have it": the pack leaves the pantry and nothing is claimed missing - the plan needs none.
        await TickAsync(a, k.Bread, ShoppingTick.HaveIt, Mon);
        Assert.DoesNotContain(await MovementsAsync(a), m => m.IngredientId == k.Bread && m.ShoppingItemId is not null);
        // And bought after all: one pack again.
        await TickAsync(a, k.Bread, ShoppingTick.Bought, Mon);
        Assert.Equal([(StockReason.Bought, 10m)],
            (await MovementsAsync(a)).Where(m => m.IngredientId == k.Bread && m.ShoppingItemId is not null).Select(m => (m.Reason, m.Quantity)));
    }
}

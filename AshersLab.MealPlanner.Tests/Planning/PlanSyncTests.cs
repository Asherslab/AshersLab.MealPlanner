using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Planning;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using AshersLab.MealPlanner.Tests.Grpc;
using AshersLab.MealPlanner.Tests.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Tests.Planning;

/// <summary>People, slots, plan templates, their entries and choices through the real host.</summary>
public sealed class PlanSyncTests : IAsyncLifetime
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

    private static async Task<PlanBook> PlansAsync(TestBrowser browser)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await PlanBook.LoadAsync(db, Ct);
    }

    private static async Task<Guid> MealAsync(TestBrowser browser, string name)
    {
        Guid id = Guid.CreateVersion7(browser.Now);
        await browser.WriteAsync(SyncedEntities.Meal, id, OpKind.Create, new MealDraft(name, null, MealKind.Simple, 1, null, null).FieldsChangedFrom(null));
        return id;
    }

    /// <summary>A plan of <paramref name="weeks"/> weeks with one entry on Monday's dinner, for <paramref name="people"/>, of <paramref name="meals"/>.</summary>
    private static async Task<(Guid Template, Guid Entry)> PlanAsync(TestBrowser browser, int weeks, IReadOnlyList<Guid>? people, params Guid[] meals)
    {
        Guid template = Guid.CreateVersion7(browser.Now), entry = Guid.CreateVersion7(browser.Now.AddMilliseconds(1));
        await browser.WriteAsync(SyncedEntities.PlanTemplate, template, OpKind.Create,
            [new OpField(PlanTemplateRules.NameField, "Rotation"), new OpField(PlanTemplateRules.WeekCountField, OpValues.FormatInt(weeks))]);
        TemplateEntryChange change = new TemplateEntryDraft(meals, people).ChangesFrom(null);
        await browser.WriteAsync(SyncedEntities.TemplateEntry, entry, OpKind.Create,
        [
            new OpField(TemplateEntryRules.TemplateField, OpValues.FormatId(template)),
            new OpField(TemplateEntryRules.WeekField, "1"),
            new OpField(TemplateEntryRules.DayField, "Monday"),
            new OpField(TemplateEntryRules.SlotField, OpValues.FormatId(CatalogueSeed.Slot(4))),
            .. change.EntryFields,
        ]);
        for (int i = 0; i < change.AddMeals.Count; i++)
            await AddChoiceAsync(browser, entry, change.AddMeals[i], i + 2);
        return (template, entry);
    }

    private static Task AddChoiceAsync(TestBrowser browser, Guid entry, Guid meal, int ms = 0) =>
        browser.WriteAsync(SyncedEntities.TemplateChoice, Guid.CreateVersion7(browser.Now.AddMilliseconds(ms)), OpKind.Create,
        [
            new OpField(TemplateChoiceRules.EntryField, OpValues.FormatId(entry)),
            new OpField(TemplateChoiceRules.MealField, OpValues.FormatId(meal)),
        ]);

    [Fact]
    public async Task A_new_browser_gets_the_seeded_slots_in_order_and_nobody_in_the_household()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();

        await using LocalDbContext db = a.CreateDbContext();
        List<Slot> slots = [.. Slot.InOrder(await db.Slots.AsNoTracking().ToListAsync(Ct))];
        Assert.Equal(["Breakfast", "Snack", "Lunch", "Snack", "Dinner"], slots.Select(s => s.Name));
        Assert.Equal(Enumerable.Range(0, 5).Select(CatalogueSeed.Slot), slots.Select(s => s.Id));
        Assert.Empty(await db.People.ToListAsync(Ct));
    }

    [Fact]
    public async Task A_plan_with_its_people_and_choices_reaches_another_browser_by_op_and_by_snapshot()
    {
        await using TestBrowser a     = await TestBrowser.OpenAsync(_server);
        await using TestBrowser byOps = await TestBrowser.OpenAsync(_server);
        await byOps.SyncAsync();

        Guid sam = Guid.CreateVersion7(a.Now);
        await a.WriteAsync(SyncedEntities.Person, sam, OpKind.Create,
            [new OpField(PersonRules.NameField, "Sam"), new OpField(PersonRules.ColourField, PersonRules.Palette[1])]);
        Guid noodles = await MealAsync(a, "Noodles"), soup = await MealAsync(a, "Soup");
        (Guid template, Guid entry) = await PlanAsync(a, 4, [sam], soup, noodles);
        await a.SyncAsync();
        await byOps.SyncAsync();
        await using TestBrowser bySnapshot = await TestBrowser.OpenAsync(_server);
        await bySnapshot.SyncAsync();

        foreach (TestBrowser b in new[] { byOps, bySnapshot })
        {
            PlanBook plans = await PlansAsync(b);
            Assert.Equal(4, plans.Template(template)!.WeekCount);
            TemplateEntry got = Assert.Single(plans.EntriesOf(template));
            Assert.Equal((entry, 1, DayOfWeek.Monday, CatalogueSeed.Slot(4)), (got.Id, got.Week, got.Day, got.SlotId));
            Assert.Equal([sam], got.PeopleIds());
            // The usual one first: rows list by id.
            Assert.Equal([soup, noodles], plans.ChoicesOf(entry).Select(c => c.MealId));
            Assert.Equal(["Rotation"], plans.PlansWithMeal(noodles));
            Assert.Equal(["Rotation"], plans.PlansWithPerson(sam));
            Assert.Equal(["Rotation"], plans.PlansWithSlot(CatalogueSeed.Slot(4)));
            await using LocalDbContext db = b.CreateDbContext();
            Assert.Equal(PersonRules.Palette[1], (await db.People.SingleAsync(Ct)).Colour);
        }
    }

    [Fact]
    public async Task Choices_added_to_one_entry_on_two_browsers_offline_both_survive()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Guid noodles = await MealAsync(a, "Noodles"), soup = await MealAsync(a, "Soup"), pizza = await MealAsync(a, "Pizza");
        (Guid template, Guid entry) = await PlanAsync(a, 1, null, noodles);
        await a.SyncAsync();
        await b.SyncAsync();

        // Both offline: each adds a choice.
        a.Now = a.Now.AddSeconds(5);
        b.Now = b.Now.AddSeconds(6);
        await AddChoiceAsync(a, entry, soup);
        await AddChoiceAsync(b, entry, pizza);
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        Assert.Equal([noodles, soup, pizza], (await PlansAsync(a)).ChoicesOf(entry).Select(c => c.MealId));
        Assert.Equal([noodles, soup, pizza], (await PlansAsync(b)).ChoicesOf(entry).Select(c => c.MealId));
        Assert.Single((await PlansAsync(b)).EntriesOf(template));
    }

    [Fact]
    public async Task An_entry_of_a_deleted_plan_plans_nothing_and_a_deleted_choice_no_longer_names_its_meal()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Guid noodles = await MealAsync(a, "Noodles"), soup = await MealAsync(a, "Soup");
        (Guid template, Guid entry) = await PlanAsync(a, 1, null, noodles, soup);
        await a.SyncAsync();
        await b.SyncAsync();

        TemplateChoice soupRow = (await PlansAsync(b)).ChoicesOf(entry).Single(c => c.MealId == soup);
        await b.WriteAsync(SyncedEntities.TemplateChoice, soupRow.Id, OpKind.Delete, []);
        await b.SyncAsync();
        await a.SyncAsync();
        Assert.Empty((await PlansAsync(a)).PlansWithMeal(soup));
        Assert.Equal(["Rotation"], (await PlansAsync(a)).PlansWithMeal(noodles));

        // Only the template deleted - as if its entries' deletes were still on their way: nothing shows or uses them.
        await a.WriteAsync(SyncedEntities.PlanTemplate, template, OpKind.Delete, []);
        await a.SyncAsync();
        await b.SyncAsync();
        PlanBook plans = await PlansAsync(b);
        Assert.Empty(plans.Templates);
        Assert.Empty(plans.EntriesOf(template));
        Assert.Empty(plans.PlansWithMeal(noodles));
    }

    [Fact]
    public async Task Who_eats_it_is_set_as_a_whole_and_the_later_edit_wins()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Guid sam = Guid.CreateVersion7(a.Now), alex = Guid.CreateVersion7(a.Now.AddMilliseconds(1));
        foreach ((Guid id, string name) in new[] { (sam, "Sam"), (alex, "Alex") })
            await a.WriteAsync(SyncedEntities.Person, id, OpKind.Create,
                [new OpField(PersonRules.NameField, name), new OpField(PersonRules.ColourField, PersonRules.Palette[0])]);
        (_, Guid entry) = await PlanAsync(a, 1, null, await MealAsync(a, "Noodles"));
        await a.SyncAsync();
        await b.SyncAsync();

        a.Now = a.Now.AddSeconds(5);
        b.Now = b.Now.AddSeconds(6);
        await a.WriteAsync(SyncedEntities.TemplateEntry, entry, OpKind.Update, [new OpField(TemplateEntryRules.PeopleField, PlanPeople.Format([sam]))]);
        await b.WriteAsync(SyncedEntities.TemplateEntry, entry, OpKind.Update, [new OpField(TemplateEntryRules.PeopleField, PlanPeople.Format([alex, sam]))]);
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        await using LocalDbContext dbA = a.CreateDbContext();
        await using LocalDbContext dbB = b.CreateDbContext();
        Assert.Equal(PlanPeople.Format([alex, sam]), (await dbA.TemplateEntries.SingleAsync(Ct)).People);
        Assert.Equal(PlanPeople.Format([alex, sam]), (await dbB.TemplateEntries.SingleAsync(Ct)).People);
    }
}

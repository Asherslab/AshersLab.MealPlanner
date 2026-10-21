using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.LocalData.Planning;
using AshersLab.MealPlanner.LocalData.Sync;
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

/// <summary>A plan applied to the calendar, changed, skipped and moved, through the real host - and applied again, anywhere.</summary>
public sealed class CalendarSyncTests : IAsyncLifetime
{
    private readonly MealPlannerHost _host = new();
    private ISyncService _server = null!;

    private static readonly Guid Dinner = CatalogueSeed.Slot(4);
    private static readonly Guid Lunch  = CatalogueSeed.Slot(2);

    /// <summary>Monday 26 October 2026 to Sunday 8 November: two weeks.</summary>
    private static readonly DateOnly Mon = new(2026, 10, 26);
    private static readonly DateOnly End = Mon.AddDays(13);

    public ValueTask InitializeAsync()
    {
        _server = _host.CreateGrpcWebClient<ISyncService>();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> MealAsync(TestBrowser browser, string name)
    {
        Guid id = Guid.CreateVersion7(browser.Now);
        await browser.WriteAsync(SyncedEntities.Meal, id, OpKind.Create, new MealDraft(name, null, MealKind.Simple, 1, null, null).FieldsChangedFrom(null));
        return id;
    }

    private static async Task<Guid> EntryAsync(TestBrowser browser, Guid template, int week, DayOfWeek day, Guid slot, params Guid[] meals)
    {
        browser.Now = browser.Now.AddMilliseconds(10);
        Guid entry = Guid.CreateVersion7(browser.Now);
        await browser.WriteAsync(SyncedEntities.TemplateEntry, entry, OpKind.Create,
        [
            new OpField(TemplateEntryRules.TemplateField, OpValues.FormatId(template)),
            new OpField(TemplateEntryRules.WeekField, OpValues.FormatInt(week)),
            new OpField(TemplateEntryRules.DayField, PlanDays.Format(day)),
            new OpField(TemplateEntryRules.SlotField, OpValues.FormatId(slot)),
        ]);
        for (int i = 0; i < meals.Length; i++)
            await browser.WriteAsync(SyncedEntities.TemplateChoice, Guid.CreateVersion7(browser.Now.AddMilliseconds(i + 1)), OpKind.Create,
                [new OpField(TemplateChoiceRules.EntryField, OpValues.FormatId(entry)), new OpField(TemplateChoiceRules.MealField, OpValues.FormatId(meals[i]))]);
        return entry;
    }

    /// <summary>
    /// A two-week plan: week 1 Monday's dinner noodles or soup, and Tuesday's lunch soup; week 2 Monday's dinner pizza, and
    /// Wednesday's noodles.
    /// </summary>
    private static async Task<(Guid Template, Guid MonDinner, Guid TueLunch, Guid MonDinner2, Guid Noodles, Guid Soup, Guid Pizza)> PlanAsync(TestBrowser a)
    {
        Guid noodles = await MealAsync(a, "Noodles"), soup = await MealAsync(a, "Soup"), pizza = await MealAsync(a, "Pizza");
        Guid template = Guid.CreateVersion7(a.Now);
        await a.WriteAsync(SyncedEntities.PlanTemplate, template, OpKind.Create,
            [new OpField(PlanTemplateRules.NameField, "Rotation"), new OpField(PlanTemplateRules.WeekCountField, "2")]);
        Guid monDinner  = await EntryAsync(a, template, 1, DayOfWeek.Monday, Dinner, noodles, soup);
        Guid tueLunch   = await EntryAsync(a, template, 1, DayOfWeek.Tuesday, Lunch, soup);
        Guid monDinner2 = await EntryAsync(a, template, 2, DayOfWeek.Monday, Dinner, pizza);
        await EntryAsync(a, template, 2, DayOfWeek.Wednesday, Dinner, noodles);
        return (template, monDinner, tueLunch, monDinner2, noodles, soup, pizza);
    }

    private static async Task<CalendarApplyPlan> ApplyAsync(TestBrowser browser, Guid template, DateOnly startsOn, DateOnly from, DateOnly to)
    {
        browser.Now = browser.Now.AddSeconds(1);
        await using LocalDbContext db = browser.CreateDbContext();
        CalendarApplyPlan plan = (await CalendarApply.PlanAsync(db, template, startsOn, from, to, Ct))!;
        if (plan.Edits.Count > 0)
            await LocalOps.WriteAllAsync(db, browser.DeviceId, browser.Now, plan.Edits, Ct);
        return plan;
    }

    private static async Task<CalendarBook> CalendarAsync(TestBrowser browser)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await CalendarBook.LoadAsync(db, Mon, End, Ct);
    }

    private static async Task SaveAsync(TestBrowser browser, PlannedEntry entry, CalendarEntryDraft draft)
    {
        browser.Now = browser.Now.AddSeconds(1);
        // As the page does: nothing changed, nothing sent.
        if (draft.ChangesFrom(entry.Now) is { Count: > 0 } fields)
            await browser.WriteAsync(SyncedEntities.CalendarEntry, entry.Id, OpKind.Update, fields);
    }

    private static CalendarEntryDraft As(PlannedEntry e) => new(e.MealId, e.People, e.Skipped, e.Day, e.SlotId);

    /// <summary>Every live entry shown on the two weeks, as (day, slot, meal, skipped).</summary>
    private static async Task<List<(DateOnly, Guid, Guid?, bool)>> ShownAsync(TestBrowser browser)
    {
        CalendarBook book = await CalendarAsync(browser);
        return [.. Enumerable.Range(0, 14).SelectMany(i => book.On(Mon.AddDays(i))).Select(e => (e.Day, e.SlotId, e.MealId, e.Skipped))];
    }

    [Fact]
    public async Task Applying_a_plan_lays_its_weeks_on_the_dates_from_its_start_and_another_browser_sees_the_same()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        var p = await PlanAsync(a);

        // This week (26 October) is week 2: so 2 November is week 1 again.
        DateOnly starts = Rotation.StartFor(Mon, 2, 2);
        CalendarApplyPlan applied = await ApplyAsync(a, p.Template, starts, Mon, End);
        Assert.Equal(2, applied.Week);
        Assert.Equal(4, applied.Result.Create.Count);
        await a.SyncAsync();
        await b.SyncAsync();

        List<(DateOnly, Guid, Guid?, bool)> expected =
        [
            (Mon, Dinner, p.Pizza, false),
            (Mon.AddDays(2), Dinner, p.Noodles, false),
            (Mon.AddDays(7), Dinner, p.Noodles, false),
            (Mon.AddDays(8), Lunch, p.Soup, false),
        ];
        // Week 2 on the 26th: Monday's pizza and Wednesday's noodles; week 1 the week after: noodles (the usual one) and soup.
        Assert.Equal(expected, await ShownAsync(a));
        Assert.Equal(await ShownAsync(a), await ShownAsync(b));
        await using LocalDbContext db = b.CreateDbContext();
        Assert.Equal(starts, (await db.PlanTemplates.SingleAsync(t => t.Id == p.Template, Ct)).StartsOn);
    }

    [Fact]
    public async Task Two_browsers_applying_the_same_plan_offline_make_each_entry_once()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        var p = await PlanAsync(a);
        await a.SyncAsync();
        await b.SyncAsync();

        // Both offline, both apply the same two weeks.
        await ApplyAsync(a, p.Template, Mon, Mon, End);
        await ApplyAsync(b, p.Template, Mon, Mon, End);
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        await using TestBrowser fresh = await TestBrowser.OpenAsync(_server);
        await fresh.SyncAsync();
        foreach (TestBrowser browser in new[] { a, b, fresh })
        {
            await using LocalDbContext db = browser.CreateDbContext();
            Assert.Equal(4, await db.CalendarEntries.CountAsync(Ct));
            Assert.Equal(4, (await ShownAsync(browser)).Count);
        }
        // Both sets of creates are in the log - every browser must see the same ops - and they changed nothing.
        Assert.Equal(await ShownAsync(a), await ShownAsync(fresh));
    }

    [Fact]
    public async Task Applying_again_on_a_browser_that_never_saw_the_changes_undoes_none_of_them()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        var p = await PlanAsync(a);
        await a.SyncAsync();
        await b.SyncAsync();

        // B applies and changes everything: soup instead of noodles, Tuesday's lunch skipped, the next Monday's pizza moved
        // to Tuesday's dinner, and the next Wednesday's taken off the calendar.
        await ApplyAsync(b, p.Template, Mon, Mon, End);
        CalendarBook book = await CalendarAsync(b);
        PlannedEntry noodles = book.On(Mon, Dinner).Single();
        PlannedEntry lunch   = book.On(Mon.AddDays(1), Lunch).Single();
        PlannedEntry pizza   = book.On(Mon.AddDays(7), Dinner).Single();
        PlannedEntry second  = book.On(Mon.AddDays(9), Dinner).Single();
        await SaveAsync(b, noodles, As(noodles) with { Meal = p.Soup });
        await SaveAsync(b, lunch, As(lunch) with { Skipped = true });
        await SaveAsync(b, pizza, As(pizza) with { Day = Mon.AddDays(8), Slot = Dinner });
        b.Now = b.Now.AddSeconds(1);
        await b.WriteAsync(SyncedEntities.CalendarEntry, second.Id, OpKind.Delete, []);
        await b.SyncAsync();

        // A, offline all along, applies the same plan later: its creates clock after every one of B's edits.
        a.Now = b.Now.AddMinutes(5);
        CalendarApplyPlan stale = await ApplyAsync(a, p.Template, Mon, Mon, End);
        Assert.Equal(4, stale.Result.Create.Count);
        await a.SyncAsync();
        await b.SyncAsync();

        List<(DateOnly, Guid, Guid?, bool)> expected =
        [
            (Mon, Dinner, p.Soup, false),
            (Mon.AddDays(1), Lunch, p.Soup, true),
            (Mon.AddDays(8), Dinner, p.Pizza, false),
        ];
        Assert.Equal(expected, await ShownAsync(a));
        Assert.Equal(expected, await ShownAsync(b));
        Assert.Equal([lunch.Id], (await CalendarAsync(a)).ToReplan.Select(e => e.Id));

        // And applying once more, anywhere, writes nothing at all.
        Assert.Empty((await ApplyAsync(a, p.Template, Mon, Mon, End)).Edits);
    }

    [Fact]
    public async Task An_untouched_entry_follows_the_plan_and_a_changed_one_stays()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        var p = await PlanAsync(a);
        await ApplyAsync(a, p.Template, Mon, Mon, End);
        await a.SyncAsync();
        await b.SyncAsync();

        // B makes soup the usual one for Monday dinner; the untouched calendar entries follow.
        TemplateChoice noodlesRow;
        await using (LocalDbContext db = b.CreateDbContext())
            noodlesRow = (await PlanBook.LoadAsync(db, Ct)).ChoicesOf(p.MonDinner).First();
        b.Now = b.Now.AddSeconds(2);
        await b.WriteAsync(SyncedEntities.TemplateChoice, noodlesRow.Id, OpKind.Delete, []);
        await b.SyncAsync();
        await a.SyncAsync();
        Assert.Equal(p.Soup, (await CalendarAsync(a)).On(Mon, Dinner).Single().MealId);

        // A skips Tuesday's lunch; then the plan loses that entry, and its week 2 Monday. The skip stays (to replan); the
        // untouched one goes from the calendar with no op of its own.
        PlannedEntry lunch = (await CalendarAsync(a)).On(Mon.AddDays(1), Lunch).Single();
        await SaveAsync(a, lunch, As(lunch) with { Skipped = true });
        a.Now = a.Now.AddSeconds(1);
        await a.WriteAsync(SyncedEntities.TemplateEntry, p.TueLunch, OpKind.Delete, []);
        await a.WriteAsync(SyncedEntities.TemplateEntry, p.MonDinner2, OpKind.Delete, []);
        await a.SyncAsync();
        await b.SyncAsync();
        foreach (TestBrowser browser in new[] { a, b })
        {
            List<(DateOnly, Guid, Guid?, bool)> shown = await ShownAsync(browser);
            // Still to replan, with no meal: a skip writes only its status, so it follows its plan entry, now gone.
            Assert.Contains((Mon.AddDays(1), Lunch, (Guid?)null, true), shown);
            Assert.DoesNotContain(shown, e => e.Item1 == Mon.AddDays(7));
            // The plan entries still there keep their untouched entries.
            Assert.Contains((Mon.AddDays(9), Dinner, p.Noodles, false), shown);
        }
    }

    [Fact]
    public async Task Moving_the_plans_start_moves_its_untouched_entries_without_doubling_any_day()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        var p = await PlanAsync(a);
        await ApplyAsync(a, p.Template, Mon, Mon, End);
        PlannedEntry pizza = (await CalendarAsync(a)).On(Mon.AddDays(7), Dinner).Single();
        await SaveAsync(a, pizza, As(pizza) with { People = [] });   // unchanged: everyone, as planned
        PlannedEntry noodles = (await CalendarAsync(a)).On(Mon, Dinner).Single();
        await SaveAsync(a, noodles, As(noodles) with { Meal = p.Soup });

        // Oops: this week was meant to be week 2. Applying again from the new start.
        await ApplyAsync(a, p.Template, Rotation.StartFor(Mon, 2, 2), Mon, End);
        await a.SyncAsync();

        List<(DateOnly, Guid, Guid?, bool)> shown = await ShownAsync(a);
        // This Monday: the soup the planner chose stays, and week 2's pizza joins it - one dinner per plan entry, never the
        // old untouched noodles twice.
        Assert.Equal(new Guid?[] { p.Soup, p.Pizza }.Order(), shown.Where(e => e.Item1 == Mon && e.Item2 == Dinner).Select(e => e.Item3).Order());
        Assert.Equal([p.Noodles], shown.Where(e => e.Item1 == Mon.AddDays(7) && e.Item2 == Dinner).Select(e => e.Item3));
        Assert.Equal([p.Soup], shown.Where(e => e.Item1 == Mon.AddDays(8)).Select(e => e.Item3));
        Assert.DoesNotContain(shown, e => e.Item1 == Mon.AddDays(1));

        // Nothing hidden holds anything back: the coming entries are exactly the ones shown.
        await using LocalDbContext db = a.CreateDbContext();
        List<CalendarEntry> coming = await CalendarBook.ComingAsync(db, Mon, Ct);
        Assert.Equal(shown.Count, coming.Count);
        Assert.DoesNotContain(coming, e => e.Date == Mon.AddDays(1));
    }

    [Fact]
    public async Task Moving_the_start_from_a_week_on_keeps_every_week_before_it_as_it_was()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        var p = await PlanAsync(a);
        await ApplyAsync(a, p.Template, Mon, Mon, End);
        List<(DateOnly, Guid, Guid?, bool)> before = await ShownAsync(a);

        // The second week should have been week 1. Fill it again from the new start.
        await ApplyAsync(a, p.Template, Rotation.StartFor(Mon.AddDays(7), 1, 2), Mon.AddDays(7), End);
        await a.SyncAsync();
        await b.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            List<(DateOnly, Guid, Guid?, bool)> shown = await ShownAsync(browser);
            // The first week as eaten, though the new start would make it week 2: Monday's noodles and Tuesday's soup.
            Assert.Equal(before.Where(e => e.Item1 < Mon.AddDays(7)), shown.Where(e => e.Item1 < Mon.AddDays(7)));
            // From the change on, the new rotation alone: week 1's noodles and soup, and week 2's pizza and Wednesday gone.
            Assert.Equal(
                [(Mon.AddDays(7), Dinner, p.Noodles, false), (Mon.AddDays(8), Lunch, p.Soup, false)],
                shown.Where(e => e.Item1 >= Mon.AddDays(7)));
        }

        // Filling the first week again changes nothing: it is still the rotation it had.
        Assert.Empty((await ApplyAsync(a, p.Template, Mon, Mon, Mon.AddDays(6))).Edits);
    }

    [Fact]
    public async Task An_entry_removed_before_this_browser_ever_saw_it_is_not_created_again()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        var p = await PlanAsync(a);
        // A delete of a calendar entry this browser never held: a tombstone with no date, as a snapshot's can be.
        Guid monday = Rotation.EntryId(p.MonDinner, Mon);
        await a.WriteAsync(SyncedEntities.CalendarEntry, monday, OpKind.Delete, []);

        CalendarApplyPlan applied = await ApplyAsync(a, p.Template, Mon, Mon, End);
        Assert.DoesNotContain(applied.Result.Create, g => g.Id == monday);
        Assert.Equal(1, applied.Result.AlreadyThere);
    }

    [Fact]
    public async Task A_skip_from_a_stale_device_keeps_the_meal_another_device_chose()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        var p = await PlanAsync(a);
        await ApplyAsync(a, p.Template, Mon, Mon, End);
        await a.SyncAsync();
        await b.SyncAsync();

        // A chooses soup for Monday; B, offline and not having seen it, skips Monday - later.
        PlannedEntry onA = (await CalendarAsync(a)).On(Mon, Dinner).Single();
        await SaveAsync(a, onA, As(onA) with { Meal = p.Soup });
        PlannedEntry onB = (await CalendarAsync(b)).On(Mon, Dinner).Single();
        b.Now = a.Now.AddMinutes(1);
        await SaveAsync(b, onB, As(onB) with { Skipped = true });
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
            Assert.Equal([(Mon, Dinner, (Guid?)p.Soup, true)], (await ShownAsync(browser)).Where(e => e.Item1 == Mon));
    }

    [Fact]
    public async Task An_entry_added_by_hand_is_shown_and_a_skipped_one_is_replanned_onto_another_day()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        Guid sam = Guid.CreateVersion7(a.Now);
        await a.WriteAsync(SyncedEntities.Person, sam, OpKind.Create,
            [new OpField(PersonRules.NameField, "Sam"), new OpField(PersonRules.ColourField, PersonRules.Palette[0])]);
        Guid curry = await MealAsync(a, "Curry");
        Guid entry = Guid.CreateVersion7(a.Now.AddSeconds(1));
        await a.WriteAsync(SyncedEntities.CalendarEntry, entry, OpKind.Create,
        [
            new OpField(CalendarEntryRules.DateField, OpValues.FormatDate(Mon.AddDays(2))),
            new OpField(CalendarEntryRules.SlotField, OpValues.FormatId(Dinner)),
            new OpField(CalendarEntryRules.MealField, OpValues.FormatId(curry)),
            new OpField(CalendarEntryRules.PeopleField, PlanPeople.Format([sam])),
        ]);
        await a.SyncAsync();
        await b.SyncAsync();

        PlannedEntry got = (await CalendarAsync(b)).On(Mon.AddDays(2), Dinner).Single();
        Assert.Equal(curry, got.MealId);
        Assert.Equal([sam], got.People!);
        Assert.False(got.EatenBy(Mon.AddDays(2)));
        Assert.True(got.EatenBy(Mon.AddDays(3)));

        await SaveAsync(b, got, As(got) with { Skipped = true });
        PlannedEntry skipped = Assert.Single((await CalendarAsync(b)).ToReplan);
        Assert.False(skipped.EatenBy(Mon.AddDays(3)));
        await SaveAsync(b, skipped, As(skipped) with { Skipped = false, Day = Mon.AddDays(5), Slot = Lunch });
        await b.SyncAsync();
        await a.SyncAsync();

        CalendarBook book = await CalendarAsync(a);
        Assert.Empty(book.ToReplan);
        Assert.Empty(book.On(Mon.AddDays(2)));
        Assert.Equal(entry, book.On(Mon.AddDays(5), Lunch).Single().Id);
        await using LocalDbContext db = a.CreateDbContext();
        CalendarEntry row = await db.CalendarEntries.SingleAsync(Ct);
        Assert.Equal((Mon.AddDays(2), Dinner, (DateOnly?)Mon.AddDays(5), (Guid?)Lunch, CalendarStatus.Planned),
            (row.Date, row.SlotId, row.MovedDate, row.MovedSlotId, row.Status));
    }
}

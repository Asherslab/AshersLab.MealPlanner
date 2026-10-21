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

namespace AshersLab.MealPlanner.Tests.Planning;

/// <summary>
/// Batches and serves counted from the calendar of real browser databases, synced through the real host: what each sees
/// after the other skips, sets the serves made, chooses a batch, or adds leftovers offline.
/// </summary>
public sealed class ServesSyncTests : IAsyncLifetime
{
    private readonly MealPlannerHost _host = new();
    private ISyncService _server = null!;

    private static readonly Guid Lunch  = CatalogueSeed.Slot(2);
    private static readonly Guid Dinner = CatalogueSeed.Slot(4);

    /// <summary>Monday 26 October 2026.</summary>
    private static readonly DateOnly Mon = new(2026, 10, 26);

    public ValueTask InitializeAsync()
    {
        _server = _host.CreateGrpcWebClient<ISyncService>();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> MealAsync(TestBrowser browser, string name, MealKind kind, int serves)
    {
        browser.Now = browser.Now.AddMilliseconds(10);
        Guid id = Guid.CreateVersion7(browser.Now);
        await browser.WriteAsync(SyncedEntities.Meal, id, OpKind.Create, new MealDraft(name, null, kind, serves, null, null).FieldsChangedFrom(null));
        return id;
    }

    private static async Task<Guid> EntryAsync(TestBrowser browser, DateOnly day, Guid slot, Guid meal)
    {
        browser.Now = browser.Now.AddMilliseconds(10);
        Guid id = Guid.CreateVersion7(browser.Now);
        await browser.WriteAsync(SyncedEntities.CalendarEntry, id, OpKind.Create,
        [
            new OpField(CalendarEntryRules.DateField, OpValues.FormatDate(day)),
            new OpField(CalendarEntryRules.SlotField, OpValues.FormatId(slot)),
            new OpField(CalendarEntryRules.MealField, OpValues.FormatId(meal)),
        ]);
        return id;
    }

    private static async Task SaveAsync(TestBrowser browser, Guid entry, Func<CalendarEntryDraft, CalendarEntryDraft> change)
    {
        PlannedEntry now = Find(await CalendarAsync(browser), entry);
        CalendarEntryDraft draft = new(now.MealId, now.People, now.Skipped, now.Day, now.SlotId, now.Row.EatsFrom, now.Row.ServesMade);
        browser.Now = browser.Now.AddSeconds(1);
        if (change(draft).ChangesFrom(now.Now) is { Count: > 0 } fields)
            await browser.WriteAsync(SyncedEntities.CalendarEntry, entry, OpKind.Update, fields);
    }

    private static PlannedEntry Find(CalendarBook book, Guid id) =>
        Enumerable.Range(-7, 22).SelectMany(i => book.On(Mon.AddDays(i))).Single(e => e.Id == id);

    private static async Task<ServesBook> ServesAsync(TestBrowser browser)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await ServesBook.LoadAsync(db, Mon, Mon.AddDays(6), Ct);
    }

    /// <summary>Two people; a 6-serve stew cooked Monday dinner; leftovers for lunch Tuesday and Wednesday.</summary>
    private static async Task<(Guid Stew, Guid Leftovers, Guid Cook, Guid Tue, Guid Wed)> WeekAsync(TestBrowser a)
    {
        foreach (string name in new[] { "Sam", "Alex" })
            await a.WriteAsync(SyncedEntities.Person, Guid.CreateVersion7(a.Now.AddMilliseconds(name.Length)), OpKind.Create,
                [new OpField(PersonRules.NameField, name), new OpField(PersonRules.ColourField, PersonRules.Palette[name.Length % 2])]);
        Guid stew      = await MealAsync(a, "Stew", MealKind.Cooked, 6);
        Guid leftovers = await MealAsync(a, "Leftovers", MealKind.Leftovers, 1);
        Guid cook      = await EntryAsync(a, Mon, Dinner, stew);
        Guid tue       = await EntryAsync(a, Mon.AddDays(1), Lunch, leftovers);
        Guid wed       = await EntryAsync(a, Mon.AddDays(2), Lunch, leftovers);
        return (stew, leftovers, cook, tue, wed);
    }

    [Fact]
    public async Task Another_browser_counts_the_same_serves_and_a_skip_of_the_cooking_flags_its_leftovers()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        var w = await WeekAsync(a);
        await a.SyncAsync();
        await b.SyncAsync();

        // Two eat it Monday, two Tuesday, two Wednesday: 6 made, nothing left.
        ServesBook onB = await ServesAsync(b);
        Assert.Equal([4, 2, 0], onB.BatchOf(w.Cook)!.Draws.Select(d => d.LeftAfter));
        Assert.Equal(w.Cook, onB.LeftoversOf(w.Wed)!.BatchId);

        // B: "we didn't cook it". A sees both lunches flagged, pointing at the skipped batch, not silently at nothing.
        await SaveAsync(b, w.Cook, d => d with { Skipped = true });
        await b.SyncAsync();
        await a.SyncAsync();
        ServesBook onA = await ServesAsync(a);
        Assert.False(onA.BatchOf(w.Cook)!.Cooked);
        Assert.All(new[] { w.Tue, w.Wed }, e => Assert.Equal((w.Cook, LeftoversProblem.NotCooked), (onA.LeftoversOf(e)!.BatchId, onA.LeftoversOf(e)!.Problem)));

        // Replanned onto Tuesday dinner: Tuesday's lunch is before it now, Wednesday's eats.
        await SaveAsync(a, w.Cook, d => d with { Skipped = false, Day = Mon.AddDays(1) });
        await a.SyncAsync();
        await b.SyncAsync();
        onB = await ServesAsync(b);
        Assert.Equal(LeftoversProblem.NothingLeft, onB.LeftoversOf(w.Tue)!.Problem);
        Assert.Equal((LeftoversProblem.None, 2), (onB.LeftoversOf(w.Wed)!.Problem, onB.LeftoversOf(w.Wed)!.LeftAfter));
        Assert.Equal(2, onB.BatchOf(w.Cook)!.Left);
    }

    [Fact]
    public async Task Leftovers_added_offline_on_two_phones_both_eat_and_a_skipped_one_gives_its_serves_back()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        var w = await WeekAsync(a);
        // A says the stew made 8.
        await SaveAsync(a, w.Cook, d => d with { ServesMade = 8 });
        await a.SyncAsync();
        await b.SyncAsync();

        // Both offline, each adds Thursday's lunch from what is left: two entries, two draws - no counter to lose one.
        Guid onA = await EntryAsync(a, Mon.AddDays(3), Lunch, w.Leftovers);
        Guid onB = await EntryAsync(b, Mon.AddDays(3), Lunch, w.Leftovers);
        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        foreach (TestBrowser browser in new[] { a, b })
        {
            ServesBook serves = await ServesAsync(browser);
            Assert.Equal(8, serves.BatchOf(w.Cook)!.Made);
            Assert.Equal(0, serves.BatchOf(w.Cook)!.Left);
            // Whichever orders first eats; the other has nothing.
            Assert.Equal([LeftoversProblem.None, LeftoversProblem.NothingLeft],
                new[] { onA, onB }.Select(e => serves.LeftoversOf(e)!.Problem).Order());
        }

        // B skips Tuesday's lunch: its two serves go to Thursday's second lunch.
        await SaveAsync(b, w.Tue, d => d with { Skipped = true });
        await b.SyncAsync();
        await a.SyncAsync();
        ServesBook after = await ServesAsync(a);
        Assert.All(new[] { onA, onB }, e => Assert.Equal(LeftoversProblem.None, after.LeftoversOf(e)!.Problem));
    }

    [Fact]
    public async Task A_chosen_batch_is_eaten_and_a_removed_one_is_flagged_as_gone()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);
        var w = await WeekAsync(a);
        Guid soup  = await MealAsync(a, "Soup", MealKind.Cooked, 4);
        Guid sunday = await EntryAsync(a, Mon.AddDays(-1), Dinner, soup);
        await a.SyncAsync();
        await b.SyncAsync();

        // Whatever is left is the oldest first: Sunday's soup for Tuesday. B chooses Monday's stew instead.
        Assert.Equal(sunday, (await ServesAsync(b)).LeftoversOf(w.Tue)!.BatchId);
        ServesBook before = await ServesAsync(b);
        Assert.Equal([w.Cook, sunday], before.ChoicesFor(Find(await CalendarAsync(b), w.Tue)).Select(c => c.Id));
        await SaveAsync(b, w.Tue, d => d with { EatsFrom = w.Cook });
        await b.SyncAsync();
        await a.SyncAsync();
        ServesBook onA = await ServesAsync(a);
        Assert.Equal(w.Cook, onA.LeftoversOf(w.Tue)!.BatchId);
        Assert.Equal(sunday, onA.LeftoversOf(w.Wed)!.BatchId);

        // A removes Monday's cooking: Tuesday's lunch was set to eat it, and says it is gone.
        a.Now = a.Now.AddSeconds(1);
        await a.WriteAsync(SyncedEntities.CalendarEntry, w.Cook, OpKind.Delete, []);
        await a.SyncAsync();
        await b.SyncAsync();
        Assert.Equal(LeftoversProblem.BatchGone, (await ServesAsync(b)).LeftoversOf(w.Tue)!.Problem);
    }

    private static async Task<CalendarBook> CalendarAsync(TestBrowser browser)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await CalendarBook.LoadAsync(db, Mon.AddDays(-7), Mon.AddDays(14), Ct);
    }
}

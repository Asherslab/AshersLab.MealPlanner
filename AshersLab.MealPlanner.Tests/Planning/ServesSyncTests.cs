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
        Enumerable.Range(-7, 43).SelectMany(i => book.On(Mon.AddDays(i))).Single(e => e.Id == id);

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

        // Whichever orders first eats, the other has nothing - and both browsers agree on which.
        List<LeftoversProblem[]> seen = [];
        foreach (TestBrowser browser in new[] { a, b })
        {
            ServesBook serves = await ServesAsync(browser);
            Assert.Equal(8, serves.BatchOf(w.Cook)!.Made);
            Assert.Equal(0, serves.BatchOf(w.Cook)!.Left);
            seen.Add([.. new[] { onA, onB }.Select(e => serves.LeftoversOf(e)!.Problem)]);
        }
        Assert.Equal(seen[0], seen[1]);
        Assert.Equal([LeftoversProblem.None, LeftoversProblem.NothingLeft], seen[0].Order());

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
        // "Stew · Mon · 4 left", "Soup · Sun · 2 left": what each has before Tuesday's lunch, its own draw on the soup not counted.
        Assert.Equal([(w.Cook, 4), (sunday, 2)],
            before.ChoicesFor(Find(await CalendarAsync(b), w.Tue)).Select(c => (c.Id, c.LeftBefore)));
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

    [Fact]
    public async Task A_choice_counts_what_ate_earlier_the_same_day_and_never_offers_a_batch_cooked_later_that_day()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        var w = await WeekAsync(a);
        Guid breakfast = await EntryAsync(a, Mon.AddDays(1), CatalogueSeed.Slot(0), w.Leftovers);
        Guid mondayLunch = await EntryAsync(a, Mon, Lunch, w.Leftovers);
        await a.SyncAsync();   // the seeded slots: what orders breakfast before lunch

        ServesBook serves = await ServesAsync(a);
        CalendarBook calendar = await CalendarAsync(a);
        // Tuesday's breakfast ate 2 before lunch: 6 made, 2 at dinner, 2 at breakfast.
        Assert.Equal(breakfast, serves.BatchOf(w.Cook)!.Leftovers.First().EntryId);
        Assert.Equal([(w.Cook, 2)], serves.ChoicesFor(Find(calendar, w.Tue)).Select(c => (c.Id, c.LeftBefore)));
        // Monday's lunch is before Monday's dinner: nothing to choose.
        Assert.Empty(serves.ChoicesFor(Find(calendar, mondayLunch)));
    }

    [Fact]
    public async Task A_weeks_serves_are_the_same_whichever_week_is_on_screen()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        var w = await WeekAsync(a);
        // Set by hand to eat the stew two weeks on (the oldest a lunch can choose): the stew's two last serves are planned,
        // not "uneaten" - seen from either week.
        Guid later = await EntryAsync(a, Mon.AddDays(ServesBook.ChoosableDays), Lunch, w.Leftovers);
        await SaveAsync(a, w.Wed, d => d with { Skipped = true });
        await SaveAsync(a, later, d => d with { EatsFrom = w.Cook });

        await using LocalDbContext db = a.CreateDbContext();
        ServesBook thisWeek = await ServesBook.LoadAsync(db, Mon, Mon.AddDays(6), Ct);
        ServesBook thatWeek = await ServesBook.LoadAsync(db, Mon.AddDays(14), Mon.AddDays(20), Ct);
        Assert.Equal(0, thisWeek.BatchOf(w.Cook)!.Left);
        Assert.Equal((LeftoversProblem.None, 0), (thatWeek.LeftoversOf(later)!.Problem, thatWeek.LeftoversOf(later)!.LeftAfter));
    }

    [Fact]
    public async Task A_chain_of_whatever_is_left_picks_is_counted_from_the_calendars_first_day()
    {
        // Found by searching for the smallest calendar where three weeks of history are not enough: day 0's batch is eaten
        // by day 7's lunch, which leaves day 5's for day 10's, and so on - each pick moving the next. Without day 0 (a
        // window of three weeks before day 22), the last lunch gets 1 serve instead of 2.
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.SyncAsync();   // the seeded slots: lunch before dinner on days 7 and 13
        Guid sam = Guid.CreateVersion7(a.Now);
        await a.WriteAsync(SyncedEntities.Person, sam, OpKind.Create, [new OpField(PersonRules.NameField, "Sam"), new OpField(PersonRules.ColourField, PersonRules.Palette[0])]);
        await a.WriteAsync(SyncedEntities.Person, Guid.CreateVersion7(a.Now.AddMilliseconds(1)), OpKind.Create,
            [new OpField(PersonRules.NameField, "Alex"), new OpField(PersonRules.ColourField, PersonRules.Palette[1])]);
        Guid stew      = await MealAsync(a, "Stew", MealKind.Cooked, 6);
        Guid leftovers = await MealAsync(a, "Leftovers", MealKind.Leftovers, 1);

        // (day, cooked: serves made / leftovers: how many eat)
        (int Day, int? Made, int Eat)[] calendar =
        [
            (0, 5, 2), (5, 4, 2), (7, 5, 2), (7, null, 2), (10, null, 2), (13, 4, 2), (13, null, 1), (14, null, 1),
            (15, 6, 2), (18, null, 2), (21, null, 1), (22, null, 2),
        ];
        List<Guid> ids = [];
        foreach ((int day, int? made, int eat) in calendar)
        {
            Guid id = await EntryAsync(a, Mon.AddDays(day), made is null ? Lunch : Dinner, made is null ? leftovers : stew);
            await SaveAsync(a, id, d => d with { ServesMade = made, People = eat == 1 ? [sam] : null });
            ids.Add(id);
        }

        await using LocalDbContext db = a.CreateDbContext();
        ServesBook all  = await ServesBook.LoadAsync(db, Mon, Mon.AddDays(27), Ct);
        ServesBook last = await ServesBook.LoadAsync(db, Mon.AddDays(22), Mon.AddDays(28), Ct);
        Assert.Equal((2, LeftoversProblem.None), (all.LeftoversOf(ids[^1])!.Got, all.LeftoversOf(ids[^1])!.Problem));
        Assert.Equal(all.LeftoversOf(ids[^1]), last.LeftoversOf(ids[^1]));
    }

    private static async Task<CalendarBook> CalendarAsync(TestBrowser browser)
    {
        await using LocalDbContext db = browser.CreateDbContext();
        return await CalendarBook.LoadAsync(db, Mon.AddDays(-7), Mon.AddDays(35), Ct);
    }
}

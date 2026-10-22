using MudBlazor;

namespace AshersLab.MealPlanner.WASM.Layout;

/// <summary>Where a destination is listed.</summary>
public enum NavGroup
{
    /// <summary>The four daily pages: the phone's bottom bar and the top of the desktop drawer.</summary>
    Main,

    /// <summary>The phone's fifth tab itself. Not in the drawer - the drawer already lists everything it holds.</summary>
    MoreTab,

    /// <summary>Used week to week, but not daily: on More, and in the drawer's Kitchen group.</summary>
    Kitchen,

    /// <summary>The single way into Settings, from More and the drawer.</summary>
    Settings,

    /// <summary>Set up once a season - on the Settings page only.</summary>
    Household,
    Shopping,
    Data,
    Diagnostics,
}

/// <param name="Route">Base-relative, no leading slash ("" is Today) - the app runs under a base path in Home Assistant.</param>
/// <param name="Key">The testid suffix: <c>nav-</c>, <c>more-</c>, <c>settings-</c> and, for a tab, <c>bottom-nav-</c>.</param>
/// <param name="Title">The page's name, in the drawer, on More and Settings, and in the phone's app bar.</param>
/// <param name="Parent">The route the phone's back arrow goes to; null for a bottom-bar tab.</param>
/// <param name="Description">One line on More or Settings saying what the page is for, in the planner's words.</param>
/// <param name="TabLabel">The bottom bar's short label; null when the page is not a tab.</param>
public sealed record NavDestination(
    string   Route,
    string   Key,
    string   Title,
    string   Icon,
    NavGroup Group,
    string?  Parent,
    string   Description,
    string?  TabLabel = null);

/// <summary>
/// Every page the menus reach, in one list: the bottom bar, the desktop drawer, More, Settings and the app bar's back
/// arrow all read it, so a page added or moved here moves everywhere at once.
/// </summary>
public static class NavModel
{
    public const string MoreRoute     = "more";
    public const string SettingsRoute = "settings";

    public static readonly IReadOnlyList<NavDestination> All =
    [
        new("",         "home",     "Today",    Icons.Material.Filled.Today,          NavGroup.Main, null,
            "What's on today", "Today"),
        new("calendar", "calendar", "Calendar", Icons.Material.Filled.CalendarMonth,  NavGroup.Main, null,
            "The meals on each day of the week", "Plan"),
        new("shopping", "shopping", "Shopping", Icons.Material.Filled.ShoppingCart,   NavGroup.Main, null,
            "What to buy, store by store", "Shop"),
        new("meals",    "meals",    "Meals",    Icons.Material.Filled.RestaurantMenu, NavGroup.Main, null,
            "Every meal and what goes in it", "Meals"),
        new(MoreRoute,  "more",     "More",     Icons.Material.Filled.Menu,           NavGroup.MoreTab, null,
            "Everything else", "More"),

        new("pantry",      "pantry",      "Pantry",      Icons.Material.Filled.Kitchen,          NavGroup.Kitchen, MoreRoute,
            "What's in the cupboard and fridge"),
        new("receipts",    "receipts",    "Receipts",    Icons.Material.Filled.Receipt,          NavGroup.Kitchen, MoreRoute,
            "Add a shop's receipt to update prices"),
        new("prices",      "prices",      "Prices",      Icons.Material.Filled.Sell,             NavGroup.Kitchen, MoreRoute,
            "What things cost, and where"),
        new("ingredients", "ingredients", "Ingredients", Icons.Material.Filled.Egg,              NavGroup.Kitchen, MoreRoute,
            "Everything meals are made from"),
        new("plans",       "plans",       "Plans",       Icons.Material.Filled.CalendarViewWeek, NavGroup.Kitchen, MoreRoute,
            "Weeks of meals to fill the calendar from"),

        new(SettingsRoute, "settings", "Settings", Icons.Material.Filled.Settings, NavGroup.Settings, MoreRoute,
            "The household, stores, and set-up done once a season"),

        new("people",      "people",      "People",           Icons.Material.Filled.People,     NavGroup.Household, SettingsRoute,
            "Who eats, and how many serves a meal needs"),
        new("settings/access", "access",  "Who can sign in",  Icons.Material.Filled.Key,        NavGroup.Household, SettingsRoute,
            "Passkeys, invitations, and signing out"),
        new("slots",       "slots",       "Slots of the day", Icons.Material.Filled.Schedule,   NavGroup.Household, SettingsRoute,
            "Breakfast, lunch, dinner - the times you plan for"),
        new("meal-groups", "meal-groups", "Meal groups",      Icons.Material.Filled.ViewList,   NavGroup.Household, SettingsRoute,
            "How the meals list is sorted"),
        new("categories",  "categories",  "Categories",       Icons.Material.Filled.Category,   NavGroup.Shopping,  SettingsRoute,
            "The aisles a shopping list is grouped by"),
        new("stores",      "stores",      "Stores",           Icons.Material.Filled.Storefront, NavGroup.Shopping,  SettingsRoute,
            "Where you shop"),
        new("import",      "import",      "Import spreadsheet", Icons.Material.Filled.UploadFile, NavGroup.Data,    SettingsRoute,
            "Bring in meals and plans from the old spreadsheet"),
        new("settings/diagnostics", "diagnostics", "Diagnostics", Icons.Material.Filled.MonitorHeart, NavGroup.Diagnostics, SettingsRoute,
            "This device and the server, for when something is wrong"),
    ];

    public static IEnumerable<NavDestination> In(NavGroup group) => All.Where(d => d.Group == group);

    public static IEnumerable<NavDestination> Tabs => All.Where(d => d.TabLabel is not null);

    public static NavDestination? ByRoute(string route) => All.FirstOrDefault(d => d.Route == route);

    /// <summary>
    /// The destination a path is on or under - <c>meals/3f…</c> is under Meals - or null for a page no menu lists.
    /// The longest match wins, so <c>settings/diagnostics</c> is Diagnostics rather than Settings.
    /// </summary>
    public static NavDestination? Find(string path)
    {
        path = Normalise(path);
        return All.Where(d => d.Route == path || (d.Route.Length > 0 && path.StartsWith(d.Route + "/", StringComparison.Ordinal)))
                  .MaxBy(d => d.Route.Length);
    }

    /// <summary>
    /// Where the phone's back arrow goes from a path, or null on a tab: a listed page goes to its parent, a page under
    /// one (a meal, a plan) goes to that page.
    /// </summary>
    public static NavDestination? BackFrom(string path)
    {
        path = Normalise(path);
        if (Find(path) is not { } here)
            return null;
        if (here.Route != path)
            return here;
        return here.Parent is { } parent ? ByRoute(parent) : null;
    }

    /// <summary>The bottom-bar tab a path belongs to: its own, or its nearest ancestor's. None for an unlisted page.</summary>
    public static NavDestination? TabFor(string path)
    {
        NavDestination? d = Find(path);
        while (d is { TabLabel: null, Parent: { } parent })
            d = ByRoute(parent);
        return d is { TabLabel: not null } ? d : null;
    }

    /// <summary>A base-relative URI to a route: query, fragment and trailing slash dropped.</summary>
    public static string Normalise(string path)
    {
        int cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
            path = path[..cut];
        return path.Trim('/');
    }
}

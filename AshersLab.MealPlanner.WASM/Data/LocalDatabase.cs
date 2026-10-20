using AshersLab.MealPlanner.LocalData;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using SqliteWasmBlazor;

namespace AshersLab.MealPlanner.WASM.Data;

/// <summary>
/// The one way into the browser database. Pages call <see cref="StartAsync"/> from <c>OnAfterRenderAsync</c>
/// and only touch <see cref="Contexts"/> once it has answered <see cref="DbInitState.READY"/>.
/// </summary>
/// <remarks>
/// Not from <c>OnInitializedAsync</c>: the worker starts and migrates after the first render, so that a slow
/// migration can be shown instead of a blank page. <see cref="StartAsync"/> is the only thing that starts
/// SqliteWasmBlazor - there is deliberately no <c>&lt;SqliteWasmDatabaseInitializer/&gt;</c> in the layout,
/// because the cross-window script has to be loaded first. It is cached, so every caller in one app start
/// awaits the same work and the start is counted once.
/// <para>
/// Every window of the app in this browser shares the database (<c>wwwroot/js/shared-database.js</c>), so
/// another window can change rows under this one; <see cref="ChangedElsewhere"/> says when.
/// </para>
/// </remarks>
public sealed class LocalDatabase(
    ISqliteWasmInitializer              initializer,
    IDbInitializationStatus             status,
    IDbContextFactory<LocalDbContext>   contexts,
    IJSRuntime                          js
)
{
    private Task<LocalDatabaseStart>? _start;

    /// <summary>Short-lived contexts, one per operation - never hold one across renders.</summary>
    public IDbContextFactory<LocalDbContext> Contexts => contexts;

    /// <summary>Another window of the app committed a change. Re-read what you show.</summary>
    public event Func<Task>? ChangedElsewhere;

    public Task<LocalDatabaseStart> StartAsync() => _start ??= Start();

    /// <summary>
    /// Whether this window runs the database (<c>"leader"</c>) or reaches it through another window
    /// (<c>"follower"</c>). Changes when the leading window closes.
    /// </summary>
    public ValueTask<string?> RoleAsync() => js.InvokeAsync<string?>("mealPlannerDatabase.role");

    [JSInvokable]
    public async Task OnChangedElsewhere()
    {
        if (ChangedElsewhere is { } handlers)
            foreach (Func<Task> handler in handlers.GetInvocationList().Cast<Func<Task>>())
                await handler();
    }

    private async Task<LocalDatabaseStart> Start()
    {
        if (!await LoadSharedDatabaseScript())
            return new LocalDatabaseStart(DbInitState.FAILED,
                "part of the app did not download. Reload the page.", null, null);

        await initializer.InitializeAsync();

        if (status.State != DbInitState.READY)
            return new LocalDatabaseStart(status.State, status.Failure?.DefaultMessage, null, null);

        await js.InvokeVoidAsync("mealPlannerDatabase.subscribe", DotNetObjectReference.Create(this));

        await using LocalDbContext db     = await contexts.CreateDbContextAsync();
        Device                     device = await DeviceRegistration.RecordOpenAsync(db, DateTimeOffset.UtcNow);

        return new LocalDatabaseStart(DbInitState.READY, null, device, await RequestPersistentStorage());
    }

    /// <summary>
    /// Loads <c>wwwroot/js/shared-database.js</c>, which must replace SqliteWasmBlazor's Worker before
    /// <see cref="ISqliteWasmInitializer.InitializeAsync"/> creates one. Here rather than a <c>&lt;script&gt;</c>
    /// tag because a script tag whose download is dropped fails silently: the app then opens the database
    /// unshared, and a second window cannot. A retry uses a new URL so no failed fetch is reused.
    /// </summary>
    private async Task<bool> LoadSharedDatabaseScript()
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            string url = attempt == 0 ? "./js/shared-database.js" : $"./js/shared-database.js?attempt={attempt}";
            try
            {
                await using IJSObjectReference _ = await js.InvokeAsync<IJSObjectReference>("import", url);
                await js.InvokeAsync<string?>("mealPlannerDatabase.role");
                return true;
            }
            catch (JSException)
            {
                await Task.Delay(250 * (attempt + 1));
            }
        }

        return false;
    }

    /// <summary>
    /// Asks the browser not to evict this origin's storage under pressure. <c>null</c> where the browser has
    /// no such API; <c>false</c> where it declined - Chrome decides from site engagement, Safari from whether
    /// the app is installed to the home screen.
    /// </summary>
    private async Task<bool?> RequestPersistentStorage()
    {
        await using IJSObjectReference storage = await js.InvokeAsync<IJSObjectReference>("import", "./js/storage.js");

        return await storage.InvokeAsync<bool?>("persist");
    }
}

/// <param name="State">Anything but <see cref="DbInitState.READY"/> means there is no local database this start.</param>
/// <param name="Failure">SqliteWasmBlazor's own sentence for a failed start.</param>
/// <param name="Device">This browser, after counting this start.</param>
/// <param name="StoragePersisted">See <see cref="LocalDatabase"/>'s RequestPersistentStorage.</param>
public sealed record LocalDatabaseStart(DbInitState State, string? Failure, Device? Device, bool? StoragePersisted);

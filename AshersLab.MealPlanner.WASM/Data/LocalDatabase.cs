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
/// Not from <c>OnInitializedAsync</c>: SqliteWasmBlazor starts its worker and migrates after the first
/// render, so that a slow migration can be shown instead of a blank page. <see cref="StartAsync"/> is cached,
/// so every caller in one app start awaits the same work and the start is counted once.
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

    public Task<LocalDatabaseStart> StartAsync() => _start ??= Start();

    private async Task<LocalDatabaseStart> Start()
    {
        await initializer.InitializeAsync();

        if (status.State != DbInitState.READY)
            return new LocalDatabaseStart(status.State, status.Failure?.DefaultMessage, null, null);

        await using LocalDbContext db     = await contexts.CreateDbContextAsync();
        Device                     device = await DeviceRegistration.RecordOpenAsync(db, DateTimeOffset.UtcNow);

        return new LocalDatabaseStart(DbInitState.READY, null, device, await RequestPersistentStorage());
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

using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using AshersLab.MealPlanner.WASM.Sync;
using Microsoft.AspNetCore.Components;
using SqliteWasmBlazor;

namespace AshersLab.MealPlanner.WASM.Data;

/// <summary>
/// A page that shows and edits the browser's synced data. Starts the local database after the first render,
/// reloads whenever another window or a sync round changes the data, and writes every edit as an op.
/// </summary>
/// <remarks>
/// A page implements <see cref="LoadAsync"/> - read what it shows into fields, with the one context it is given -
/// and calls <see cref="WriteAsync"/> for every edit. Wrap the markup in <c>&lt;LocalDataGate Start="Start"&gt;</c>
/// for the loading and failure states. Never write to a synced table any other way: see
/// docs/modules/sync/offline-sync.md.
/// </remarks>
public abstract class LocalDataPage : ComponentBase, IDisposable
{
    [Inject] protected LocalDatabase Database { get; set; } = null!;

    [Inject] protected SyncLoop Sync { get; set; } = null!;

    /// <summary>Null until the database has answered; then whether it opened.</summary>
    protected LocalDatabaseStart? Start { get; private set; }

    protected bool Ready => Start?.State == DbInitState.READY;

    /// <summary>Reads what the page shows. Runs on start and after every change, wherever it was made.</summary>
    protected abstract Task LoadAsync(LocalDbContext db);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        Start = await Database.StartAsync();
        if (Ready)
        {
            Database.ChangedElsewhere += Reload;
            Sync.DataChanged          += Reload;
            await Reload();
        }
        StateHasChanged();
    }

    protected async Task Reload()
    {
        await using (LocalDbContext db = await Database.Contexts.CreateDbContextAsync())
            await LoadAsync(db);

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// One edit: applied here and queued for the server in one transaction (<see cref="LocalOps.WriteAsync"/>),
    /// then synced and the page reloaded. <b>Send only fields that changed</b> - an op that changes nothing still
    /// wins its merge, and would undo an edit made meanwhile on another device.
    /// </summary>
    protected async Task WriteAsync(SyncedEntity type, Guid id, OpKind kind, IReadOnlyList<OpField> fields)
    {
        await using (LocalDbContext db = await Database.Contexts.CreateDbContextAsync())
            await LocalOps.WriteAsync(db, Start!.Device!.Id, DateTimeOffset.UtcNow, type, id, kind, fields);

        Sync.Kick();
        await Reload();
    }

    public virtual void Dispose()
    {
        Database.ChangedElsewhere -= Reload;
        Sync.DataChanged          -= Reload;
        GC.SuppressFinalize(this);
    }
}

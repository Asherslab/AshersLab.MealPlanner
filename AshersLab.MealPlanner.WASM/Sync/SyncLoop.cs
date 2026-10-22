using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Messages.Requests.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Messages.Responses.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.WASM.Data;
using Grpc.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace AshersLab.MealPlanner.WASM.Sync;

/// <summary>
/// Decides when this window runs a <see cref="SyncRound"/> (push the outbox, pull past the cursor) and tells the
/// app bar how it went. Runs for the life of the window once <see cref="Start"/> is called.
/// </summary>
/// <remarks>
/// A round runs on start, after every local edit (<see cref="Kick"/>), whenever the change stream says the server
/// has moved past this browser's cursor, when the stream comes back after a drop, on the browser's <c>online</c>
/// event, every 5 s while the server cannot be reached, and every 30 s regardless (<see cref="SyncSchedule"/>).
/// A failure to reach the server counts as "offline" and is retried every 5 s - the outbox is in SQLite, so nothing
/// is lost by waiting. Any other failure is "failed": logged as an error, shown on the chip, retried every 30 s and
/// on the planner's own edits, but not on change notices (<see cref="SyncSchedule.KickOnNotice"/>). The server
/// refusing this build's sync protocol version reloads the app once. A server
/// that came back empty while this browser holds what it lacks stops every round until the planner answers
/// <see cref="DecideAsync"/> (<c>ServerReplacedDialog</c> asks).
/// <para>
/// Every window of the app runs its own loop on the one shared database. That is safe because every step is
/// idempotent - the server ignores an op id it already has, and a pulled op already applied changes nothing - so
/// two windows pushing the same outbox only do the work twice.
/// </para>
/// </remarks>
public sealed class SyncLoop(
    LocalDatabase     database,
    ISyncService      server,
    IJSRuntime        js,
    ILogger<SyncLoop> logger
) : IAsyncDisposable
{
    private const string ReloadedKey = "mealplanner-sync-protocol-reload";

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim           _wake     = new(0, 1);
    private          Guid                    _deviceId;
    private          bool                    _started;
    private          long                    _pulledThrough = -1;
    private          Guid?                   _epoch;

    public SyncStatus Status { get; private set; } = new(SyncConnection.Connecting, 0, null, null);

    public event Func<Task>? StatusChanged;

    /// <summary>A pull changed rows in this browser's database. Re-read what you show.</summary>
    public event Func<Task>? DataChanged;

    /// <summary>Once the local database is ready. Later calls do nothing.</summary>
    public void Start(Guid deviceId)
    {
        if (_started)
            return;

        _started  = true;
        _deviceId = deviceId;

        // Only while waiting on the planner (OnChangedElsewhere): kicking on every other window's commit would have
        // two windows' rounds wake each other forever.
        database.ChangedElsewhere += OnChangedElsewhere;

        _ = RunRounds();
        _ = WatchChanges();
        _ = WatchConnectivity();
    }

    [JSInvokable]
    public void OnOnline() => Kick();

    private async Task WatchConnectivity()
    {
        try
        {
            await using IJSObjectReference module = await js.InvokeAsync<IJSObjectReference>("import", "./js/connectivity.js");
            await module.InvokeVoidAsync("onOnline", DotNetObjectReference.Create(this));
        }
        catch (JSException ex)
        {
            // Only a hint; the retry timer covers it.
            logger.LogInformation("No online/offline events: {Message}", ex.Message);
        }
    }

    /// <summary>Run a round soon - after a local edit, or when the planner taps the status.</summary>
    public void Kick()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    private async Task RunRounds()
    {
        while (!_stopping.IsCancellationRequested)
        {
            await Round();

            try
            {
                // Failed waits the 30 s fallback: retrying a broken pull every 5 s only fills the log.
                await _wake.WaitAsync(SyncSchedule.Wait(Status.Connection), _stopping.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task Round()
    {
        CancellationToken ct = _stopping.Token;
        try
        {
            SyncRoundResult result = await SyncRound.RunAsync(database.Contexts, server, _deviceId, ct);

            _epoch         = result.ServerEpoch;
            _pulledThrough = result.PulledThroughSeq;

            if (result.Replaced is { } replaced)
            {
                if (Status.Replaced is null)
                    logger.LogWarning("The server's database is new and lacks {Rows} row(s) this browser holds; waiting for the planner",
                        replaced.MissingRows);
                await SetStatus(SyncConnection.WaitingForChoice, null, Status.LastSyncedAt, replaced);
                return;
            }

            string? note = null;
            if (result.Refused.Count > 0)
            {
                // A bug, not a conflict: this browser checks every edit with the server's own rules before applying
                // it, so a refusal means the two builds disagree. The edit stays applied here (offline-sync.md).
                logger.LogError("The server refused {Count} op(s): {Reason}", result.Refused.Count, result.Refused.FirstReason);
                note = $"The server refused {result.Refused.Count} change{(result.Refused.Count == 1 ? "" : "s")}: {result.Refused.FirstReason}";
            }

            if (result.PushedBack > 0)
                logger.LogWarning("The server had lost {Count} edit(s) this browser holds (restored from a backup); pushed them back",
                    result.PushedBack);

            await SetStatus(SyncConnection.Online, note, DateTimeOffset.Now);
            await js.InvokeVoidAsync("sessionStorage.removeItem", ReloadedKey);

            if (result.Changed && DataChanged is { } handlers)
                foreach (Func<Task> handler in handlers.GetInvocationList().Cast<Func<Task>>())
                    await handler();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            switch (SyncSchedule.AfterFailure(ex))
            {
                case SyncConnection.Refused:
                    await Refused();
                    break;
                case SyncConnection.SignedOut:
                    // The sign-in proxy's session expired. A reload goes to its login page: page loads try the network
                    // first (service-worker.published.js), so the login is not hidden behind the cached app.
                    logger.LogInformation("Sync round was refused a sign-in: {Message}", ex.Message);
                    await SetStatus(SyncConnection.SignedOut,
                        "Signed out. Reload the app to sign in again - changes are kept on this device until then.", Status.LastSyncedAt);
                    break;
                case SyncConnection.Offline:
                    // Offline, the server restarting, a proxy timing out: all the same to the planner. Edits wait.
                    logger.LogInformation("Sync round could not reach the server, will retry: {Message}", ex.Message);
                    await SetStatus(SyncConnection.Offline, null, Status.LastSyncedAt);
                    break;
                default:
                    // Not the network: a pulled op that will not apply, a server error, a bug. Retrying does not fix it -
                    // a pull that fails keeps the cursor where it is, so nothing after it arrives - so it is shown.
                    logger.LogError(ex, "Sync round failed");
                    await SetStatus(SyncConnection.Failed, $"Sync is not working: {ex.Message}. Changes are kept on this device.",
                        Status.LastSyncedAt);
                    break;
            }
        }
    }

    /// <summary>
    /// Listens for "the newest op is N" and starts a round when N is past this browser's cursor, or when the
    /// stream has just come back. Reconnects with back-off from 1 s to 10 s.
    /// </summary>
    private async Task WatchChanges()
    {
        TimeSpan backoff = TimeSpan.FromSeconds(1);

        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                await foreach (ChangesNotice notice in server.Changes(new ChangesRequest { ProtocolVersion = SyncProtocol.Current }, _stopping.Token))
                {
                    backoff = TimeSpan.FromSeconds(1);

                    if (SyncSchedule.KickOnNotice(Status.Connection, _epoch, _pulledThrough, notice.ServerEpoch, notice.LatestSeq))
                        Kick();
                }
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (RpcException ex) when (SyncSchedule.IsRefusal(ex))
            {
                await Refused();
                return;
            }
            catch (Exception ex)
            {
                logger.LogInformation("Change stream dropped, reconnecting in {Seconds}s: {Message}", backoff.TotalSeconds, ex.Message);
                if (SyncSchedule.AfterStreamDropped(Status.Connection) is { } connection)
                    await SetStatus(connection, null, Status.LastSyncedAt);
            }

            try
            {
                await Task.Delay(backoff, _stopping.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    /// <summary>
    /// The server cannot read this build's ops: a newer app has been deployed. Reload once to fetch it - the
    /// outbox is in SQLite and survives, and the new build migrates it and pushes it. A second refusal in the
    /// same session means the reload did not help (a cached old build); say so instead of reloading in a loop.
    /// </summary>
    private async Task Refused()
    {
        await SetStatus(SyncConnection.Refused, "This copy of the app is out of date. Reload to update it - nothing is lost.", Status.LastSyncedAt);

        if (await js.InvokeAsync<string?>("sessionStorage.getItem", ReloadedKey) is null)
        {
            await js.InvokeVoidAsync("sessionStorage.setItem", ReloadedKey, "1");
            // Not a plain reload: the service worker would serve the same cached old build. This fetches the new one,
            // lets it take over, then reloads (js/app-update.js).
            await js.InvokeVoidAsync("mealPlannerApp.update");
        }
    }

    /// <summary>
    /// The planner's answer to the server coming back empty. The first answer in any window is the one that applies;
    /// a later one (another window got there first) changes nothing. Either way a round runs, and every window's
    /// question closes when its next round finds it answered.
    /// </summary>
    public async Task DecideAsync(ServerReplacedChoice choice)
    {
        if (Status.Replaced is not { } replaced)
            return;

        bool applied;
        await using (LocalDbContext db = await database.Contexts.CreateDbContextAsync())
            applied = await LocalOps.DecideAsync(db, replaced.By, choice);

        logger.LogWarning(applied ? "The planner chose {Choice} for the new server database" : "{Choice} was chosen too late - another window answered first",
            choice);

        if (applied && choice == ServerReplacedChoice.ClearMyCopy && DataChanged is { } handlers)
            foreach (Func<Task> handler in handlers.GetInvocationList().Cast<Func<Task>>())
                await handler();

        Kick();
    }

    /// <summary>Another window may have answered the question this one is showing.</summary>
    private Task OnChangedElsewhere()
    {
        if (Status.Connection == SyncConnection.WaitingForChoice)
            Kick();
        return Task.CompletedTask;
    }

    private async Task SetStatus(SyncConnection connection, string? note, DateTimeOffset? lastSyncedAt, ServerReplaced? replaced = null)
    {
        int pending;
        await using (LocalDbContext db = await database.Contexts.CreateDbContextAsync())
            pending = await db.Outbox.CountAsync();

        Status = new SyncStatus(connection, pending, note, lastSyncedAt, replaced);

        if (StatusChanged is { } handlers)
            foreach (Func<Task> handler in handlers.GetInvocationList().Cast<Func<Task>>())
                await handler();
    }

    public async ValueTask DisposeAsync()
    {
        database.ChangedElsewhere -= OnChangedElsewhere;
        await _stopping.CancelAsync();
        _stopping.Dispose();
    }
}

/// <param name="Pending">Edits in the outbox, not yet confirmed by the server.</param>
/// <param name="Note">Something the planner should read - a refused change, an out-of-date app.</param>
/// <param name="Replaced">The open question while <see cref="SyncConnection.WaitingForChoice"/>.</param>
public sealed record SyncStatus(
    SyncConnection Connection, int Pending, string? Note, DateTimeOffset? LastSyncedAt, ServerReplaced? Replaced = null);

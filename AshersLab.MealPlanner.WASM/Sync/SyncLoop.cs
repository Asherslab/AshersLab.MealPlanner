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
/// event, every 5 s while rounds are failing, and every 30 s regardless.
/// A failure to reach the server counts as "offline" and is retried every 5 s - the outbox is in SQLite, so nothing
/// is lost by waiting. Any other failure is "failed": logged as an error, shown on the chip, retried every 30 s
/// (<see cref="SyncFailure"/>). The server refusing this build's sync protocol version reloads the app once.
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
    NavigationManager navigation,
    ILogger<SyncLoop> logger
) : IAsyncDisposable
{
    private const string ReloadedKey = "mealplanner-sync-protocol-reload";

    private static readonly TimeSpan Fallback     = TimeSpan.FromSeconds(30);

    /// <summary>
    /// While a round is failing. Short, because signal in a shop comes and goes for seconds at a time, and an
    /// open change stream does not always notice the network went (an idle connection can survive it).
    /// </summary>
    private static readonly TimeSpan OfflineRetry = TimeSpan.FromSeconds(5);
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
                await _wake.WaitAsync(Status.Connection == SyncConnection.Offline ? OfflineRetry : Fallback, _stopping.Token);
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
        catch (RpcException ex) when (IsRefusal(ex))
        {
            await Refused();
        }
        catch (Exception ex) when (SyncFailure.IsOffline(ex))
        {
            // Offline, the server restarting, a proxy timing out: all the same to the planner. Edits wait.
            logger.LogInformation("Sync round could not reach the server, will retry: {Message}", ex.Message);
            await SetStatus(SyncConnection.Offline, null, Status.LastSyncedAt);
        }
        catch (Exception ex)
        {
            // Not the network: a pulled op that will not apply, a server error, a bug. Retrying does not fix it -
            // a pull that fails keeps the cursor where it is, so nothing after it arrives - so it is shown.
            logger.LogError(ex, "Sync round failed");
            await SetStatus(SyncConnection.Failed, $"Sync is not working: {ex.Message}. Changes are kept on this device.",
                Status.LastSyncedAt);
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

                    // != rather than >: a server restored from an older backup announces a seq below the cursor.
                    if (Status.Connection != SyncConnection.Online || notice.ServerEpoch != _epoch || notice.LatestSeq != _pulledThrough)
                        Kick();
                }
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (RpcException ex) when (IsRefusal(ex))
            {
                await Refused();
                return;
            }
            catch (Exception ex)
            {
                logger.LogInformation("Change stream dropped, reconnecting in {Seconds}s: {Message}", backoff.TotalSeconds, ex.Message);
                // Only the rounds say Failed; a dropped stream says nothing about whether they work.
                if (Status.Connection is not (SyncConnection.Refused or SyncConnection.Failed))
                    await SetStatus(SyncConnection.Offline, null, Status.LastSyncedAt);
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

    private static bool IsRefusal(RpcException ex) =>
        ex.StatusCode == StatusCode.FailedPrecondition && ex.Status.Detail == SyncProtocol.RefusedStatus;

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
            navigation.Refresh(forceReload: true);
        }
    }

    private async Task SetStatus(SyncConnection connection, string? note, DateTimeOffset? lastSyncedAt)
    {
        int pending;
        await using (LocalDbContext db = await database.Contexts.CreateDbContextAsync())
            pending = await db.Outbox.CountAsync();

        Status = new SyncStatus(connection, pending, note, lastSyncedAt);

        if (StatusChanged is { } handlers)
            foreach (Func<Task> handler in handlers.GetInvocationList().Cast<Func<Task>>())
                await handler();
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _stopping.Dispose();
    }
}

public enum SyncConnection
{
    Connecting,
    Online,
    Offline,

    /// <summary>The server refused this build's sync protocol version.</summary>
    Refused,

    /// <summary>The server was reachable and the round still failed - see <see cref="SyncFailure"/>.</summary>
    Failed,
}

/// <param name="Pending">Edits in the outbox, not yet confirmed by the server.</param>
/// <param name="Note">Something the planner should read - a refused change, an out-of-date app.</param>
public sealed record SyncStatus(SyncConnection Connection, int Pending, string? Note, DateTimeOffset? LastSyncedAt);

using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;
using Grpc.Core;

namespace AshersLab.MealPlanner.LocalData.Sync;

/// <summary>What the app bar's sync chip says, and what decides when the next round runs.</summary>
public enum SyncConnection
{
    Connecting,
    Online,
    Offline,

    /// <summary>The server refused this build's sync protocol version.</summary>
    Refused,

    /// <summary>The server was reachable and the round still failed - see <see cref="SyncFailure"/>.</summary>
    Failed,

    /// <summary>The server came back empty; nothing syncs until the planner chooses - see <see cref="ServerReplaced"/>.</summary>
    WaitingForChoice,
}

/// <summary>
/// The WASM <c>SyncLoop</c>'s decisions - what a failure means, when a change notice starts a round, how long to
/// wait - kept here, out of the loop's timers and JS interop, so the test suite can pin them.
/// </summary>
public static class SyncSchedule
{
    /// <summary>The round that runs regardless, and the retry while <see cref="SyncConnection.Failed"/>.</summary>
    public static readonly TimeSpan Fallback = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The retry while <see cref="SyncConnection.Offline"/>. Short, because signal in a shop comes and goes for seconds
    /// at a time, and an open change stream does not always notice the network went (an idle connection can survive it).
    /// </summary>
    public static readonly TimeSpan OfflineRetry = TimeSpan.FromSeconds(5);

    /// <summary>
    /// What a round that threw leaves the chip saying. Only failing to reach the server is
    /// <see cref="SyncConnection.Offline"/>; the server refusing this build's protocol is
    /// <see cref="SyncConnection.Refused"/>; anything else - a pulled op that will not apply, a server error, a bug -
    /// is <see cref="SyncConnection.Failed"/>, because waiting does not fix it.
    /// </summary>
    public static SyncConnection AfterFailure(Exception exception) =>
        exception is RpcException rpc && IsRefusal(rpc) ? SyncConnection.Refused
        : SyncFailure.IsOffline(exception)              ? SyncConnection.Offline
        : SyncConnection.Failed;

    public static bool IsRefusal(RpcException exception) =>
        exception.StatusCode == StatusCode.FailedPrecondition && exception.Status.Detail == SyncProtocol.RefusedStatus;

    /// <summary>How long the loop waits for a kick before running a round anyway.</summary>
    public static TimeSpan Wait(SyncConnection connection) =>
        connection == SyncConnection.Offline ? OfflineRetry : Fallback;

    /// <summary>
    /// Whether a change notice ("the newest op is <paramref name="latestSeq"/>") starts a round. Online, only when
    /// the server has moved: != rather than >, because a server restored from an older backup announces a seq below
    /// the cursor. Offline or connecting, always - a notice getting through says the server is reachable again.
    /// <b>Failed, never</b>: the stream's heartbeat (every 25 s) and every other device's edit would each re-run a
    /// round that cannot succeed, filling the log; it waits for <see cref="Fallback"/>, or for the planner.
    /// </summary>
    public static bool KickOnNotice(SyncConnection connection, Guid? epoch, long pulledThrough, Guid noticeEpoch, long latestSeq) =>
        connection switch
        {
            SyncConnection.Failed => false,
            SyncConnection.Online => noticeEpoch != epoch || latestSeq != pulledThrough,
            _                     => true,
        };

    /// <summary>
    /// What the chip says when the change stream drops; null leaves it as it is. A dropped stream says nothing about
    /// whether rounds work, so it never hides <see cref="SyncConnection.Failed"/>, <see cref="SyncConnection.Refused"/>
    /// or the open question of <see cref="SyncConnection.WaitingForChoice"/> behind "Offline".
    /// </summary>
    public static SyncConnection? AfterStreamDropped(SyncConnection connection) =>
        connection is SyncConnection.Refused or SyncConnection.Failed or SyncConnection.WaitingForChoice ? null : SyncConnection.Offline;
}

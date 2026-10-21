using Grpc.Core;

namespace AshersLab.MealPlanner.LocalData.Sync;

/// <summary>
/// Tells "the server could not be reached" from "sync is broken". The first is normal - a shop with no signal, the
/// add-on restarting - and waits quietly. The second never fixes itself by waiting: a pulled op that fails to
/// apply rolls back and the cursor never moves, so this browser stops receiving every later op. It has to be
/// seen, on the chip and in the log.
/// </summary>
public static class SyncFailure
{
    public static bool IsOffline(Exception exception) => exception switch
    {
        // Grpc.Net.Client's answer to a fetch that failed, and the Supervisor's 502/503/504 while the add-on
        // restarts (gRPC-Web maps those HTTP statuses to Unavailable).
        RpcException { StatusCode: StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Cancelled } => true,

        // A response cut off part way: the transport's exception is wrapped as Internal.
        RpcException { StatusCode: StatusCode.Internal, Status.DebugException: { } inner } => IsTransport(inner),

        _ => IsTransport(exception),
    };

    private static bool IsTransport(Exception exception) => exception switch
    {
        HttpRequestException or IOException or TimeoutException => true,
        { InnerException: { } inner } => IsTransport(inner),
        _ => false,
    };
}

using AshersLab.MealPlanner.LocalData.Sync;
using Grpc.Core;
using Microsoft.Data.Sqlite;

namespace AshersLab.MealPlanner.Tests.Sync;

/// <summary>Which failures the chip calls "Offline" (wait quietly) and which "Sync problem" (shown, logged as an error).</summary>
public class SyncFailureTests
{
    public static TheoryData<Exception> Offline => new()
    {
        new RpcException(new Status(StatusCode.Unavailable, "Error starting gRPC call. HttpRequestException: TypeError: Failed to fetch")),
        new RpcException(new Status(StatusCode.DeadlineExceeded, "")),
        new RpcException(new Status(StatusCode.Cancelled, "")),
        new RpcException(new Status(StatusCode.Internal, "Error reading next message.", new IOException("The response ended prematurely."))),
        new HttpRequestException("TypeError: Failed to fetch"),
        new TaskCanceledException("timed out", new TimeoutException()),
    };

    public static TheoryData<Exception> Broken => new()
    {
        // A pulled op that will not apply: the cursor never moves past it.
        new InvalidOperationException("Pulled an op for unknown entity 'Meal'"),
        new SqliteException("SQLite Error 19: 'UNIQUE constraint failed'", 19),
        new FormatException("Not a clock reading: 'x'."),
        // The server answered, with an error.
        new RpcException(new Status(StatusCode.Internal, "Exception was thrown by handler.")),
        new RpcException(new Status(StatusCode.Unimplemented, "")),
        new RpcException(new Status(StatusCode.InvalidArgument, "At most 500 ops per push.")),
    };

    [Theory]
    [MemberData(nameof(Offline))]
    public void Not_reaching_the_server_is_offline(Exception exception) => Assert.True(SyncFailure.IsOffline(exception));

    [Theory]
    [MemberData(nameof(Broken))]
    public void Anything_else_is_a_failure(Exception exception) => Assert.False(SyncFailure.IsOffline(exception));
}

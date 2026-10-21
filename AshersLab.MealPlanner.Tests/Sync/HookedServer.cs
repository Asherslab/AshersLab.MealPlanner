using AshersLab.MealPlanner.Shared.Contracts.Messages.Requests.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Messages.Responses.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace AshersLab.MealPlanner.Tests.Sync;

/// <summary>
/// The real server, with a hand on the wire: something can happen between a round's push and its pull (the planner
/// tapping while a round is in flight), an answer can be lost after the server committed, the network can drop
/// mid-round. Without these a round always pushes before it pulls, and the outbox is empty whenever a snapshot
/// arrives - which is how the snapshot paths went untested.
/// </summary>
public sealed class HookedServer(ISyncService inner) : ISyncService
{
    /// <summary>Runs before each pull is forwarded.</summary>
    public Func<Task>? BeforePull { get; set; }

    /// <summary>The next push reaches the server and commits, and then its answer is lost.</summary>
    public bool LoseNextPushAnswer { get; set; }

    /// <summary>Every push after this many more fails as if offline, without reaching the server.</summary>
    public int? PushesBeforeOffline { get; set; }

    public List<PushResponse> PushAnswers { get; } = [];

    public List<PullResponse> PullAnswers { get; } = [];

    public async Task<PushResponse> Push(PushRequest request, CallContext context = default)
    {
        if (PushesBeforeOffline is { } left)
        {
            if (left <= 0)
                throw Offline();
            PushesBeforeOffline = left - 1;
        }

        PushResponse response = await inner.Push(request, context);

        if (LoseNextPushAnswer)
        {
            LoseNextPushAnswer = false;
            throw Offline();
        }

        PushAnswers.Add(response);
        return response;
    }

    public async Task<PullResponse> Pull(PullRequest request, CallContext context = default)
    {
        if (BeforePull is { } hook)
            await hook();

        PullResponse response = await inner.Pull(request, context);
        PullAnswers.Add(response);
        return response;
    }

    public IAsyncEnumerable<ChangesNotice> Changes(ChangesRequest request, CallContext context = default) =>
        inner.Changes(request, context);

    private static RpcException Offline() => new(new Status(StatusCode.Unavailable, "The test cut the network."));
}

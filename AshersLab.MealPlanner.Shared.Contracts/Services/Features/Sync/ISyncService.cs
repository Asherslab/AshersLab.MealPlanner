using AshersLab.MealPlanner.Shared.Contracts.Messages.Requests.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Messages.Responses.Features.Sync;

namespace AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;

/// <summary>
/// The only write path from a browser to the server: every edit is an op in the browser's outbox, pushed here,
/// and every browser pulls every op back (docs/modules/sync/offline-sync.md).
/// </summary>
/// <remarks>
/// Push and pull are unary because gRPC-Web has no client or bidirectional streaming; <see cref="Changes"/> is
/// server streaming, which gRPC-Web does carry, and only ever says "the newest op is N" - the client pulls.
/// Every call refuses a client whose <see cref="Entities.Features.Sync.SyncProtocol"/> version it cannot read.
/// </remarks>
[Service("gRPC/AshersLab.MealPlanner.Sync")]
public interface ISyncService
{
    Task<PushResponse> Push(PushRequest request, CallContext context = default);

    Task<PullResponse> Pull(PullRequest request, CallContext context = default);

    IAsyncEnumerable<ChangesNotice> Changes(ChangesRequest request, CallContext context = default);
}

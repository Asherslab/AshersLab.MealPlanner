using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;

namespace AshersLab.MealPlanner.Shared.Contracts.Messages.Requests.Features.Sync;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record PushRequest
{
    /// <summary><see cref="SyncProtocol.Current"/> as the client was built.</summary>
    public required int ProtocolVersion { get; init; }

    /// <summary>In clock order. Applied in one transaction.</summary>
    public List<SyncOp> Ops { get; init; } = [];
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record PullRequest
{
    public required int ProtocolVersion { get; init; }

    /// <summary>
    /// The server database the client's cursor belongs to; null on a first pull. A different one (the server's
    /// database was replaced) gets a snapshot instead of ops.
    /// </summary>
    public Guid? ServerEpoch { get; init; }

    /// <summary>The highest <see cref="SyncOp.Seq"/> the client has applied. Past the end of the log gets a snapshot.</summary>
    public long SinceSeq { get; init; }

    /// <summary>At most this many ops; the server caps it.</summary>
    public int Limit { get; init; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record ChangesRequest
{
    public required int ProtocolVersion { get; init; }
}

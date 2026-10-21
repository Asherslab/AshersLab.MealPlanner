using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;

namespace AshersLab.MealPlanner.Shared.Contracts.Messages.Responses.Features.Sync;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record PushResponse
{
    /// <summary>One per pushed op, in the same order.</summary>
    public List<PushResult> Results { get; init; } = [];

    /// <summary>The server database the seqs belong to.</summary>
    public Guid ServerEpoch { get; init; }
}

/// <summary>What became of one pushed op. On the wire as its number - add at the end.</summary>
public enum PushOutcome
{
    /// <summary>Logged with a new <see cref="PushResult.Seq"/>, whether or not it won its merge.</summary>
    Accepted = 0,

    /// <summary>This op id was already logged (a retry). <see cref="PushResult.Seq"/> is the original.</summary>
    Duplicate = 1,

    /// <summary>
    /// Not logged: the server could not accept it (unknown entity, invalid field). The client drops it from its
    /// outbox - a retry would be refused again - and says so. The edit stays applied in that browser: client
    /// validation is a superset of the server's, so this is a bug, not a conflict (offline-sync.md).
    /// </summary>
    Rejected = 2,
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record PushResult
{
    public required Guid OpId { get; init; }

    public required PushOutcome Outcome { get; init; }

    public long Seq { get; init; }

    /// <summary>Why it was rejected.</summary>
    public string? Reason { get; init; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record PullResponse
{
    /// <summary>Which server database these ops come from. The client keeps it with its cursor.</summary>
    public required Guid ServerEpoch { get; init; }

    /// <summary>Set instead of <see cref="Ops"/> when the cursor means nothing here; see <see cref="SyncSnapshot.Reason"/>.</summary>
    public SyncSnapshot? Snapshot { get; init; }

    /// <summary>Ops after the requested seq, ascending, including the client's own.</summary>
    public List<SyncOp> Ops { get; init; } = [];

    /// <summary>More ops follow; pull again from the last one's seq.</summary>
    public bool HasMore { get; init; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record ChangesNotice
{
    public required Guid ServerEpoch { get; init; }

    /// <summary>The newest op the server has logged. Sent on connect, on every push, and as a heartbeat.</summary>
    public required long LatestSeq { get; init; }
}

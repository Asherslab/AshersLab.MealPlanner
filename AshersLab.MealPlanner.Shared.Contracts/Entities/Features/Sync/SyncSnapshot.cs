namespace AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;

/// <summary>
/// Every synced entity as the server holds it, with the clock behind each field, so the browser can merge it
/// with its own unsynced edits by the same rules as an op. Sent instead of ops on a first pull and whenever
/// the browser's cursor belongs to a different server database.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record SyncSnapshot
{
    /// <summary>The newest op the snapshot includes. The browser pulls from here.</summary>
    public required long AsOfSeq { get; init; }

    public List<SnapshotEntity> Entities { get; init; } = [];
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record SnapshotEntity
{
    public required string Entity { get; init; }

    public required Guid EntityId { get; init; }

    public bool Deleted { get; init; }

    public List<SnapshotField> Fields { get; init; } = [];
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record SnapshotField
{
    public required string Name { get; init; }

    public string? Value { get; init; }

    /// <summary>The clock of the op that set this value.</summary>
    public required string Hlc { get; init; }
}

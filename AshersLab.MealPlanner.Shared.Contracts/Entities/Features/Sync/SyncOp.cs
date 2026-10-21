using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;

/// <summary>
/// One field-level change, as pushed by a browser and as pulled back by every browser.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record SyncOp
{
    /// <summary>Minted by the device that made the edit. The server's dedupe key: a retried push is skipped.</summary>
    public required Guid OpId { get; init; }

    public required Guid DeviceId { get; init; }

    /// <summary><see cref="Domain.Sync.Hlc"/>'s string form.</summary>
    public required string Hlc { get; init; }

    /// <summary>The entity type, e.g. <c>Ingredient</c> - each type's <c>*Rules.Entity</c>.</summary>
    public required string Entity { get; init; }

    public required Guid EntityId { get; init; }

    public required OpKind Kind { get; init; }

    /// <summary>Only the fields this op changes.</summary>
    public List<SyncField> Fields { get; init; } = [];

    /// <summary>The server's order. 0 on a push - the server assigns it.</summary>
    public long Seq { get; init; }

    /// <summary>
    /// The log's <see cref="Domain.Sync.LogChain"/> through this op. Null on a push. The browser sends it back
    /// with its cursor, so the server can tell a log restored from a backup from the one the browser pulled.
    /// </summary>
    public string? Chain { get; init; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record SyncField
{
    public required string Name { get; init; }

    /// <summary>Invariant-culture text. Null clears the field.</summary>
    public string? Value { get; init; }
}

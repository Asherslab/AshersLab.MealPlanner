namespace AshersLab.MealPlanner.LocalData.Sync;

/// <summary>
/// One row: how far this browser has pulled, from which server database, and its clock. Local only. Shared by
/// every window of the app in this browser, so every read-modify-write of it is in an IMMEDIATE transaction.
/// </summary>
public class SyncState
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>The server database <see cref="PulledThroughSeq"/> belongs to. Null before the first pull.</summary>
    public Guid? ServerEpoch { get; set; }

    /// <summary>Every op up to this seq has been applied here.</summary>
    public long PulledThroughSeq { get; set; }

    /// <summary>
    /// The server log's chain at <see cref="PulledThroughSeq"/>, sent with every pull so the server can tell
    /// whether its log still holds what this browser has seen. Empty before anything was pulled.
    /// </summary>
    public string PulledThroughChain { get; set; } = "";

    /// <summary>This device's hybrid logical clock state, as a <c>Hlc</c> string. Null before its first event.</summary>
    public string? Clock { get; set; }

    public DateTimeOffset? LastPulledAt { get; set; }
}

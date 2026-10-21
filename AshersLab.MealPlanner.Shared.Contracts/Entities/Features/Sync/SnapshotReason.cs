namespace AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;

/// <summary>Why the server sent a snapshot instead of ops. On the wire as its number - add at the end.</summary>
public enum SnapshotReason
{
    /// <summary>The browser has no cursor yet. Merge it.</summary>
    FirstLoad = 0,

    /// <summary>
    /// The cursor names another server database (this one was wiped and started again). What the browser
    /// synced from the old one is not here and never will be: forget it, keep the outbox.
    /// </summary>
    NewDatabase = 1,

    /// <summary>
    /// Same database, but its log no longer holds the op the cursor names - it was restored from a backup older
    /// than what this browser has seen. The browser holds data the server lost: push it back, then merge.
    /// </summary>
    HistoryLost = 2,
}

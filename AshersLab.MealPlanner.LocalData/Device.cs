namespace AshersLab.MealPlanner.LocalData;

/// <summary>
/// This browser profile, created the first time the app opens in it. Local only - never synced.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is the device id every op will carry (docs/work/2026-10-offline-sync.md), so it must
/// outlive reloads and browser restarts. That makes this row the first evidence that the database persists:
/// a reload that shows a new id, or an <see cref="OpenCount"/> back at 1, means the storage was lost.
/// </remarks>
public class Device
{
    public Guid Id { get; set; }

    public DateTimeOffset FirstOpenedAt { get; set; }

    public DateTimeOffset LastOpenedAt { get; set; }

    /// <summary>How many times the app has started in this browser profile.</summary>
    public int OpenCount { get; set; }
}

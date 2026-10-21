namespace AshersLab.MealPlanner.Shared.Domain.Sync;

/// <summary>
/// What an op does. On the wire as its number, so never renumber - add at the end.
/// </summary>
/// <remarks>
/// There is no <c>Increment</c> and none is planned: anything counted (serves eaten, stock on hand) is counted from rows
/// when read, so two devices adding one each add two rows and nothing merges. See docs/modules/sync/offline-sync.md.
/// </remarks>
public enum OpKind
{
    Create = 0,
    Update = 1,
    Delete = 2,
}

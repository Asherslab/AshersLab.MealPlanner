namespace AshersLab.MealPlanner.Shared.Domain.Sync;

/// <summary>
/// What an op does. On the wire as its number, so never renumber - add at the end.
/// </summary>
/// <remarks>
/// <c>Increment</c> (the design's counters - serves consumed) is added with the first entity that has a
/// counter; nothing before slice 10 needs one.
/// </remarks>
public enum OpKind
{
    Create = 0,
    Update = 1,
    Delete = 2,
}

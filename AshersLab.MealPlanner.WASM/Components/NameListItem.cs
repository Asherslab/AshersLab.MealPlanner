namespace AshersLab.MealPlanner.WASM.Components;

/// <summary>One row of a <see cref="NameList"/>.</summary>
/// <param name="UsedBy">The names of whatever refers to it. Not empty: it cannot be deleted.</param>
public sealed record NameListItem(Guid Id, string Name, IReadOnlyList<string> UsedBy);

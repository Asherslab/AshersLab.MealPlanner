namespace AshersLab.MealPlanner.WASM.Components;

/// <summary>One row of a <see cref="NameList"/>.</summary>
/// <param name="UsedBy">The names of the ingredients that refer to it. Not empty: it cannot be deleted.</param>
/// <param name="UsedByProducts">The names of the products that refer to it (a store's); likewise.</param>
public sealed record NameListItem(Guid Id, string Name, IReadOnlyList<string> UsedBy, IReadOnlyList<string>? UsedByProducts = null)
{
    public bool InUse => UsedBy.Count > 0 || UsedByProducts is { Count: > 0 };
}

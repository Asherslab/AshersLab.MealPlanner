namespace AshersLab.MealPlanner.Shared.Domain.Sync;

/// <summary>
/// The checks every synced entity makes on an op's shape, before its own checks on each field's value. Each
/// entity's rules (<c>IngredientRules</c>, <c>StoreRules</c>, ...) call <see cref="Problem"/> with its fields.
/// </summary>
public static class OpRules
{
    /// <summary>
    /// Null when an op's fields are acceptable for <paramref name="kind"/>; otherwise why not. A delete carries no
    /// fields; anything else carries at least one, each known and present once; a create carries every field in
    /// <paramref name="requiredOnCreate"/>; and each field passes <paramref name="fieldProblem"/>.
    /// </summary>
    /// <param name="noun">"An ingredient", "A store" - used in the messages.</param>
    public static string? Problem(
        OpKind                    kind,
        IReadOnlyList<OpField>    fields,
        string                    noun,
        IReadOnlySet<string>      known,
        IReadOnlyList<string>     requiredOnCreate,
        Func<OpField, string?>    fieldProblem
    )
    {
        if (kind == OpKind.Delete)
            return fields.Count == 0 ? null : "A delete carries no fields.";

        if (fields.Count == 0)
            return "The op changes nothing.";

        if (fields.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != fields.Count)
            return "A field appears twice.";

        if (fields.FirstOrDefault(f => !known.Contains(f.Name)) is { } unknown)
            return $"{noun} has no field '{unknown.Name}'.";

        if (kind == OpKind.Create && requiredOnCreate.FirstOrDefault(r => fields.All(f => f.Name != r)) is { } missing)
            return $"A new one needs '{missing}'.";

        foreach (OpField field in fields)
            if (fieldProblem(field) is { } problem)
                return problem;

        return null;
    }
}

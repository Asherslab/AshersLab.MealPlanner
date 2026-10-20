using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Ingredients;

/// <summary>
/// An ingredient as the op log sees it: its entity name, its fields, and what a valid value is. The page, the
/// browser's op writer and the server's push all check the same rules, so an op the browser queued is one
/// the server accepts.
/// </summary>
public static class IngredientRules
{
    /// <summary>The op log's <c>Entity</c> for ingredients. On the wire and in both databases - never rename.</summary>
    public const string Entity = "Ingredient";

    public const string NameField = "Name";

    public const int MaxNameLength = 100;

    /// <summary>Every field an ingredient op may carry. Anything else is refused.</summary>
    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal) { NameField };

    /// <summary>Trims and collapses inner runs of whitespace: " beef  mince " is "beef mince".</summary>
    public static string NormaliseName(string? name) =>
        string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Null when <paramref name="name"/> (already normalised) is acceptable; otherwise why not.</summary>
    public static string? NameProblem(string name) => name switch
    {
        { Length: 0 }                  => "An ingredient needs a name.",
        { Length: > MaxNameLength }    => $"Keep the name to {MaxNameLength} characters.",
        _                              => null,
    };

    /// <summary>
    /// Null when an op's fields are acceptable for <paramref name="kind"/>; otherwise why not. A create must
    /// name the ingredient; an update may carry only known fields, each valid.
    /// </summary>
    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields)
    {
        if (kind == OpKind.Delete)
            return fields.Count == 0 ? null : "A delete carries no fields.";

        if (fields.Count == 0)
            return "The op changes nothing.";

        if (fields.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != fields.Count)
            return "A field appears twice.";

        if (fields.FirstOrDefault(f => !Fields.Contains(f.Name)) is { } unknown)
            return $"An ingredient has no field '{unknown.Name}'.";

        if (kind == OpKind.Create && fields.All(f => f.Name != NameField))
            return "A new ingredient needs a name.";

        foreach (OpField field in fields)
        {
            if (field.Name == NameField
             && (field.Value is null || NormaliseName(field.Value) != field.Value || NameProblem(field.Value) is not null))
                return NameProblem(NormaliseName(field.Value)) ?? "The name is not normalised.";
        }

        return null;
    }
}

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

    public const int MaxNameLength = Names.MaxLength;

    private const string Noun = "An ingredient";

    /// <summary>Every field an ingredient op may carry. Anything else is refused.</summary>
    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal) { NameField };

    private static readonly string[] RequiredOnCreate = [NameField];

    /// <summary>Trims and collapses inner runs of whitespace: " beef  mince " is "beef mince".</summary>
    public static string NormaliseName(string? name) => Names.Normalise(name);

    /// <summary>Null when <paramref name="name"/> (already normalised) is acceptable; otherwise why not.</summary>
    public static string? NameProblem(string name) => Names.Problem(name, Noun);

    /// <summary>
    /// Null when an op's fields are acceptable for <paramref name="kind"/>; otherwise why not. A create must
    /// name the ingredient; an update may carry only known fields, each valid.
    /// </summary>
    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            NameField => Names.FieldProblem(field.Value, Noun),
            _         => null,
        });
}

using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Planning;

/// <summary>
/// One meal a plan entry may be: "Leftovers/noodles/pizza" is an entry with three. A row of its own rather than a list
/// on the entry, for the reason a meal's ingredients are: two phones each adding a choice offline both keep theirs.
/// Choices list in the order they were added (GUIDv7 ids); the first is the usual one.
/// </summary>
public static class TemplateChoiceRules
{
    /// <summary>The op log's <c>Entity</c>. On the wire and in both databases - never rename.</summary>
    public const string Entity = "TemplateChoice";

    /// <summary>The entry's id. Required, never null, never changed by the pages.</summary>
    public const string EntryField = "EntryId";

    /// <summary>The meal's id. Required, never null.</summary>
    public const string MealField = "MealId";

    public const string Noun = "A plan choice";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal) { EntryField, MealField };

    private static readonly string[] RequiredOnCreate = [EntryField, MealField];

    /// <summary>Whether the entry and the meal exist is not checked: either may not have arrived yet, or be gone.</summary>
    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            EntryField => OpValues.TryParseId(field.Value, out Guid? entry) && entry is not null ? null : "It needs a plan entry.",
            MealField  => OpValues.TryParseId(field.Value, out Guid? meal) && meal is not null ? null : "It needs a meal.",
            _          => null,
        });
}

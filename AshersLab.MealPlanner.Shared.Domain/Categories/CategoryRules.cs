using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Categories;

/// <summary>
/// A shopping category - Meat, Dairy, Pantry - as the op log sees it: a name and a hand-set position. Groups the
/// ingredients list and, later, the shopping list, in this order.
/// </summary>
public static class CategoryRules
{
    /// <summary>The op log's <c>Entity</c> for categories. On the wire and in both databases - never rename.</summary>
    public const string Entity = "Category";

    public const string NameField = "Name";

    /// <summary>A <see cref="ListPosition"/>, in its <see cref="ListPosition.Format"/>.</summary>
    public const string PositionField = "Position";

    public const string Noun = "A category";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal) { NameField, PositionField };

    private static readonly string[] RequiredOnCreate = [NameField, PositionField];

    public static string? NameProblem(string name) => Names.Problem(name, Noun);

    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            NameField     => Names.FieldProblem(field.Value, Noun),
            PositionField => ListPosition.TryParse(field.Value, out _) ? null : "The position is not a number.",
            _             => null,
        });
}

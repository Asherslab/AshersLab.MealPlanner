using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Meals;

/// <summary>
/// A meal group - Beef, Chicken &amp; egg, Fish, Side, Snack: the columns of the planner's "meal ideas" - as the op log
/// sees it: a name and a hand-set position, exactly like a shopping category. Groups the meals list in this order.
/// </summary>
public static class MealGroupRules
{
    /// <summary>The op log's <c>Entity</c> for meal groups. On the wire and in both databases - never rename.</summary>
    public const string Entity = "MealGroup";

    public const string NameField = "Name";

    /// <summary>A <see cref="ListPosition"/>, in its <see cref="ListPosition.Format"/>.</summary>
    public const string PositionField = "Position";

    public const string Noun = "A meal group";

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

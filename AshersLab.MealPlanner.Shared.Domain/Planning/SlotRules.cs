using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Planning;

/// <summary>
/// A slot in the day - Breakfast, Snack, Lunch, Snack, Dinner - as the op log sees it: a name and a hand-set position,
/// exactly like a meal group. <b>Names need not be unique</b>: the day has two snacks, and they are told apart by where
/// they sit.
/// </summary>
public static class SlotRules
{
    /// <summary>The op log's <c>Entity</c> for slots. On the wire and in both databases - never rename.</summary>
    public const string Entity = "Slot";

    public const string NameField = "Name";

    /// <summary>A <see cref="ListPosition"/>, in its <see cref="ListPosition.Format"/>.</summary>
    public const string PositionField = "Position";

    /// <summary>
    /// Who usually eats in the slot: null for everyone, or <see cref="PlanPeople.Format"/>'s list - the toddler's
    /// breakfast and snacks. Only where a new entry's "who eats it" starts; it changes no entry already planned.
    /// Set as a whole, so the later edit wins.
    /// </summary>
    public const string PeopleField = "People";

    public const string Noun = "A slot";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal) { NameField, PositionField, PeopleField };

    private static readonly string[] RequiredOnCreate = [NameField, PositionField];

    public static string? NameProblem(string name) => Names.Problem(name, Noun);

    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            NameField     => Names.FieldProblem(field.Value, Noun),
            PositionField => ListPosition.TryParse(field.Value, out _) ? null : "The position is not a number.",
            PeopleField   => PlanPeople.TryParse(field.Value, out _) ? null : "The people are not a list of people.",
            _             => null,
        });

    /// <summary>
    /// Who a new entry in the slot is for: the slot's <paramref name="usual"/> eaters still in the household
    /// (<paramref name="household"/>), or null - everyone - when it names nobody who is.
    /// </summary>
    public static IReadOnlyList<Guid>? NewEntryPeople(IReadOnlyList<Guid>? usual, IEnumerable<Guid> household)
    {
        if (usual is null)
            return null;
        HashSet<Guid> live = [.. household];
        List<Guid> people = [.. usual.Where(live.Contains)];
        return people.Count == 0 ? null : people;
    }
}

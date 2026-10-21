using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Meals;

/// <summary>
/// A meal - "spaghetti bolognese", "takeaway pizza" - as the op log sees it. Its ingredients are their own rows
/// (<c>MealIngredient</c>, next to this), not a field: two phones each adding an ingredient offline must both keep theirs.
/// </summary>
public static class MealRules
{
    /// <summary>The op log's <c>Entity</c> for meals. On the wire and in both databases - never rename.</summary>
    public const string Entity = "Meal";

    public const string NameField = "Name";

    /// <summary>A meal group's id, or null for none.</summary>
    public const string GroupField = "GroupId";

    /// <summary>A <see cref="MealKind"/> by name.</summary>
    public const string KindField = "Kind";

    /// <summary>How many serves one batch makes - the sheet's "4x" (<see cref="OpValues.FormatInt"/>).</summary>
    public const string ServesPerBatchField = "ServesPerBatch";

    /// <summary>For <see cref="MealKind.FixedCost"/>: the whole thing's price in whole cents, or null when not typed.</summary>
    public const string FixedCostCentsField = "FixedCostCents";

    /// <summary>Free text, or null for none.</summary>
    public const string NotesField = "Notes";

    public const int MaxServes = 99;

    public const int MaxNotesLength = 2000;

    public const string Noun = "A meal";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        NameField, GroupField, KindField, ServesPerBatchField, FixedCostCentsField, NotesField,
    };

    /// <summary>
    /// A new meal names itself, says what kind it is and how many serves it makes - the editor sends 1 for a kind
    /// that has no batch, so the field is never missing and a later change of kind has a number to start from.
    /// </summary>
    private static readonly string[] RequiredOnCreate = [NameField, KindField, ServesPerBatchField];

    public static string? NameProblem(string name) => Names.Problem(name, Noun);

    public static string? ServesProblem(int serves) => serves switch
    {
        < 1         => "A batch makes at least one serve.",
        > MaxServes => $"Keep it to {MaxServes} serves.",
        _           => null,
    };

    /// <summary>Notes are trimmed; empty means none (null), never an empty string. Line breaks are kept.</summary>
    public static string? NormaliseNotes(string? notes) => notes?.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    public static string? NotesProblem(string? notes) =>
        notes is null                         ? null
        : NormaliseNotes(notes) != notes      ? "The notes are not trimmed."
        : notes.Length > MaxNotesLength       ? $"Keep the notes to {MaxNotesLength} characters."
        : null;

    /// <summary>
    /// Null when an op's fields are acceptable for <paramref name="kind"/>; otherwise why not. Whether the group exists
    /// is not checked, and neither is whether a fixed-cost meal has its amount: an update carries only what changed,
    /// so no one op can see both fields. A fixed-cost meal with no amount is costed as incomplete.
    /// </summary>
    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            NameField           => Names.FieldProblem(field.Value, Noun),
            GroupField          => OpValues.TryParseId(field.Value, out _) ? null : "The group is not an id.",
            KindField           => MealKinds.TryParse(field.Value, out _) ? null : "The kind is not one a meal can be.",
            ServesPerBatchField => OpValues.TryParseInt(field.Value, out int serves) ? ServesProblem(serves) : "The serves are not a whole number.",
            FixedCostCentsField => field.Value is null ? null
                : OpValues.TryParseCents(field.Value, out long cents) ? Money.CentsProblem(cents)
                : "The cost is not whole cents.",
            NotesField          => NotesProblem(field.Value),
            _                   => null,
        });
}

using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Meals;

/// <summary>
/// One ingredient of one meal - "500 g beef mince", "grated cheese (no quantity yet)" - as the op log sees it. A row of
/// its own rather than a list on the meal, so two phones each adding an ingredient offline both keep theirs.
/// </summary>
public static class MealIngredientRules
{
    /// <summary>The op log's <c>Entity</c>. On the wire and in both databases - never rename.</summary>
    public const string Entity = "MealIngredient";

    /// <summary>The meal's id. Required, never null, never changed by the pages.</summary>
    public const string MealField = "MealId";

    /// <summary>The ingredient's id. Required, never null.</summary>
    public const string IngredientField = "IngredientId";

    /// <summary>
    /// How much a batch (or, for a single-serve meal, a serve) uses, in the ingredient's base unit
    /// (<see cref="OpValues.FormatDecimal"/>), or null: "not entered yet" is a real state - the spreadsheet import makes
    /// every one of them - and the meal's cost shows as incomplete until it is entered.
    /// </summary>
    public const string QuantityField = "Quantity";

    /// <summary>"grated", "for the sauce" - normalised text, or null.</summary>
    public const string NoteField = "Note";

    public const int MaxNoteLength = 200;

    public const string Noun = "A meal's ingredient";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        MealField, IngredientField, QuantityField, NoteField,
    };

    private static readonly string[] RequiredOnCreate = [MealField, IngredientField];

    /// <summary>
    /// Reads what the planner typed in <paramref name="unit"/>. Empty is allowed - null with no problem, "not known
    /// yet"; anything else typed must be a quantity.
    /// </summary>
    public static decimal? ParseQuantity(string? text, BaseUnit unit, out string? problem)
    {
        decimal? quantity = Quantity.Read(text, unit, out problem);
        if (quantity is { } read)
            problem = QuantityProblem(read);
        return problem is null ? quantity : null;
    }

    public static string? QuantityProblem(decimal quantity) =>
        Quantity.Problem(quantity, "Type how much it uses, or leave it empty.", "That is more than any recipe uses.");

    /// <summary>A note is trimmed and single-spaced; empty is none (null), never an empty string.</summary>
    public static string? NormaliseNote(string? note) => Names.Normalise(note) is { Length: > 0 } normal ? normal : null;

    public static string? NoteProblem(string? note) =>
        note is null                       ? null
        : NormaliseNote(note) != note      ? "The note is not normalised."
        : note.Length > MaxNoteLength      ? $"Keep the note to {MaxNoteLength} characters."
        : null;

    /// <summary>
    /// Null when an op's fields are acceptable for <paramref name="kind"/>; otherwise why not. Whether the meal and the
    /// ingredient exist is not checked - either may not have arrived yet, or been deleted elsewhere.
    /// </summary>
    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            MealField       => OpValues.TryParseId(field.Value, out Guid? meal) && meal is not null ? null : "It needs a meal.",
            IngredientField => OpValues.TryParseId(field.Value, out Guid? ingredient) && ingredient is not null ? null : "It needs an ingredient.",
            QuantityField   => field.Value is null ? null
                : OpValues.TryParseDecimal(field.Value, out decimal quantity) ? QuantityProblem(quantity)
                : "The quantity is not a number.",
            NoteField       => NoteProblem(field.Value),
            _               => null,
        });
}

/// <summary>A meal's ingredient as the sheet holds it.</summary>
public sealed record MealIngredientDraft(Guid MealId, Guid IngredientId, decimal? Quantity, string? Note)
{
    /// <summary>
    /// The fields to send: everything set, for a new one (null <paramref name="before"/>); otherwise only what differs.
    /// Empty when nothing changed: send no op at all.
    /// </summary>
    public List<OpField> FieldsChangedFrom(MealIngredientDraft? before)
    {
        List<OpField> fields = [];
        if (before is null || before.MealId != MealId)
            fields.Add(new(MealIngredientRules.MealField, OpValues.FormatId(MealId)));
        if (before is null || before.IngredientId != IngredientId)
            fields.Add(new(MealIngredientRules.IngredientField, OpValues.FormatId(IngredientId)));
        if (before is null ? Quantity is not null : before.Quantity != Quantity)
            fields.Add(new(MealIngredientRules.QuantityField, Quantity is { } quantity ? OpValues.FormatDecimal(quantity) : null));
        if (before is null ? Note is not null : before.Note != Note)
            fields.Add(new(MealIngredientRules.NoteField, Note));
        return fields;
    }
}

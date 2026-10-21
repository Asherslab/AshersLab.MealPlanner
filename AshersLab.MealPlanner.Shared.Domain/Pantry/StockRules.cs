using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Pantry;

/// <summary>
/// Why a stock movement was written. On the wire and in both databases by <b>name</b> - never rename a member. What a
/// cooked meal uses is not one of these: it is read from the calendar, never stored (<see cref="Stock"/>).
/// </summary>
public enum StockReason
{
    /// <summary>Packs that came into the kitchen: adds its quantity.</summary>
    Bought,

    /// <summary>A correction by hand - "threw some out": adds its quantity, which is usually negative.</summary>
    Adjusted,

    /// <summary>
    /// A stocktake - "we're out", "about half a pack left": <b>sets</b> the level to its quantity. A level, not a
    /// difference: a phone that counted offline states what it saw, whatever the estimate said meanwhile.
    /// </summary>
    Counted,
}

public static class StockReasons
{
    public static string Format(StockReason reason) => reason.ToString();

    /// <summary>Reads exactly a member's name, case and all - nothing else, numbers included.</summary>
    public static bool TryParse(string? value, out StockReason reason)
    {
        foreach (StockReason candidate in Enum.GetValues<StockReason>())
        {
            if (candidate.ToString() == value)
            {
                reason = candidate;
                return true;
            }
        }

        reason = default;
        return false;
    }
}

/// <summary>
/// A stock movement - one change to one ingredient's stock, on one day, in its base unit - as the op log sees it.
/// Append-only by use: the pantry adds movements and deletes mistaken ones (undo), and never edits one. Stock on hand
/// is these summed with what the calendar cooks (<see cref="Stock"/>), so two phones adding stock at once add two rows
/// and nothing merges - there is no counter to lose an edit.
/// </summary>
/// <remarks>
/// Updates are still <b>accepted</b>, as for prices: a browser giving back what a restored server lost sends every field
/// as an update (docs/modules/sync/offline-sync.md).
/// </remarks>
public static class StockRules
{
    /// <summary>The op log's <c>Entity</c> for stock movements. On the wire and in both databases - never rename.</summary>
    public const string Entity = "StockMovement";

    /// <summary>The ingredient's id. Required, never null.</summary>
    public const string IngredientField = "IngredientId";

    /// <summary>In the ingredient's base unit (<see cref="OpValues.FormatDecimal"/>); negative for an adjustment down.</summary>
    public const string QuantityField = "Quantity";

    /// <summary>The day it happened (<see cref="OpValues.FormatDate"/>).</summary>
    public const string OccurredOnField = "OccurredOn";

    /// <summary>A <see cref="StockReason"/> by name.</summary>
    public const string ReasonField = "Reason";

    public const string Noun = "A stock change";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        IngredientField, QuantityField, OccurredOnField, ReasonField,
    };

    private static readonly string[] RequiredOnCreate = [IngredientField, QuantityField, OccurredOnField, ReasonField];

    /// <summary>
    /// Null when an amount is acceptable for a movement of <paramref name="reason"/>: bought is more than nothing, a
    /// count is nothing or more, an adjustment is anything but nothing - and none past <see cref="Quantity.Max"/> or
    /// finer than <see cref="Quantity.MaxDecimals"/> places.
    /// </summary>
    public static string? QuantityProblem(StockReason reason, decimal quantity)
    {
        if (Math.Abs(quantity) > Quantity.Max)
            return "That is more than any kitchen holds.";
        if (decimal.Round(quantity, Quantity.MaxDecimals) != quantity)
            return $"Keep it to {Quantity.MaxDecimals} decimal places.";
        return reason switch
        {
            StockReason.Bought   when quantity <= 0 => "Type how much was bought.",
            StockReason.Counted  when quantity < 0  => "A count is nothing or more.",
            StockReason.Adjusted when quantity == 0 => "Type how much to take off or add.",
            _                                       => null,
        };
    }

    /// <summary>
    /// Null when an op's fields are acceptable for <paramref name="kind"/>; otherwise why not. The amount's sign is checked
    /// against the reason when both travel together - a create always carries both.
    /// </summary>
    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields)
    {
        if (OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
            {
                IngredientField => OpValues.TryParseId(field.Value, out Guid? ingredient) && ingredient is not null ? null : "A stock change needs an ingredient.",
                QuantityField   => OpValues.TryParseDecimal(field.Value, out decimal quantity) && Math.Abs(quantity) <= Quantity.Max
                                   && decimal.Round(quantity, Quantity.MaxDecimals) == quantity ? null : "The amount is not a quantity.",
                OccurredOnField => OpValues.TryParseDate(field.Value, out _) ? null : "The date is not a date.",
                ReasonField     => StockReasons.TryParse(field.Value, out _) ? null : "The reason is not bought, adjusted or counted.",
                _               => null,
            }) is { } problem)
            return problem;

        return fields.FirstOrDefault(f => f.Name == ReasonField) is { } r && StockReasons.TryParse(r.Value, out StockReason reason)
            && fields.FirstOrDefault(f => f.Name == QuantityField) is { } q && OpValues.TryParseDecimal(q.Value, out decimal amount)
                ? QuantityProblem(reason, amount)
                : null;
    }
}

/// <summary>A new movement as the pantry writes it: every field, in one create.</summary>
public sealed record StockEntry(Guid IngredientId, decimal Quantity, DateOnly OccurredOn, StockReason Reason)
{
    public IReadOnlyList<OpField> Fields() =>
    [
        new(StockRules.IngredientField, OpValues.FormatId(IngredientId)),
        new(StockRules.QuantityField, OpValues.FormatDecimal(Quantity)),
        new(StockRules.OccurredOnField, OpValues.FormatDate(OccurredOn)),
        new(StockRules.ReasonField, StockReasons.Format(Reason)),
    ];
}

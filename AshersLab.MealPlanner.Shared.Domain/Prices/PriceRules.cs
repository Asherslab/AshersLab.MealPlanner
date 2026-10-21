using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Prices;

/// <summary>
/// A price observation - one product's price for one pack, seen on one day - as the op log sees it. Append-only by
/// use: the pages add observations and delete mistaken ones, and never edit one, because the history is the point.
/// </summary>
/// <remarks>
/// Updates are still <b>accepted</b>: a browser giving back what a restored server lost sends every field as an
/// update (docs/modules/sync/offline-sync.md), and a create-only rule would reject exactly those.
/// </remarks>
public static class PriceRules
{
    /// <summary>The op log's <c>Entity</c> for price observations. On the wire and in both databases - never rename.</summary>
    public const string Entity = "PriceObservation";

    /// <summary>The product's id. Required, never null.</summary>
    public const string ProductField = "ProductId";

    /// <summary>The price of one pack, in whole cents (<see cref="OpValues.FormatCents"/>).</summary>
    public const string PriceCentsField = "PriceCents";

    /// <summary>The day it was seen (<see cref="OpValues.FormatDate"/>).</summary>
    public const string ObservedOnField = "ObservedOn";

    /// <summary>A <see cref="PriceSource"/> by name.</summary>
    public const string SourceField = "Source";

    /// <summary>
    /// For variable-weight meat, the price per kilogram in cents, or null. The pack price then varies with the pack's
    /// weight, and the per-kg price is what prices a gram.
    /// </summary>
    public const string PerKgCentsField = "PerKgCents";

    public const string Noun = "A price";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        ProductField, PriceCentsField, ObservedOnField, SourceField, PerKgCentsField,
    };

    private static readonly string[] RequiredOnCreate = [ProductField, PriceCentsField, ObservedOnField, SourceField];

    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            ProductField    => OpValues.TryParseId(field.Value, out Guid? product) && product is not null ? null : "A price needs a product.",
            PriceCentsField => OpValues.TryParseCents(field.Value, out long cents) ? Money.CentsProblem(cents) : "The price is not whole cents.",
            ObservedOnField => OpValues.TryParseDate(field.Value, out _) ? null : "The date is not a date.",
            SourceField     => PriceSources.TryParse(field.Value, out _) ? null : "The source is not manual, receipt, live or sheet.",
            PerKgCentsField => field.Value is null ? null
                : OpValues.TryParseCents(field.Value, out long perKg) ? Money.CentsProblem(perKg)
                : "The price per kg is not whole cents.",
            _               => null,
        });
}

/// <summary>A new observation as a page writes it: every field it has, in one create.</summary>
public sealed record PriceEntry(Guid ProductId, long PriceCents, DateOnly ObservedOn, PriceSource Source, long? PerKgCents)
{
    public List<OpField> Fields()
    {
        List<OpField> fields =
        [
            new(PriceRules.ProductField, OpValues.FormatId(ProductId)),
            new(PriceRules.PriceCentsField, OpValues.FormatCents(PriceCents)),
            new(PriceRules.ObservedOnField, OpValues.FormatDate(ObservedOn)),
            new(PriceRules.SourceField, PriceSources.Format(Source)),
        ];
        if (PerKgCents is { } perKg)
            fields.Add(new(PriceRules.PerKgCentsField, OpValues.FormatCents(perKg)));
        return fields;
    }
}

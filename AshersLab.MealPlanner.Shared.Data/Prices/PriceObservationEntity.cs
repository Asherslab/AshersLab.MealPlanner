using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Prices;

/// <summary>How an op's fields map onto a <see cref="PriceObservation"/> row.</summary>
public sealed class PriceObservationEntity : SyncedEntity<PriceObservation>
{
    public override string Entity => PriceRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => PriceRules.OpProblem(kind, fields);

    protected override void Set(PriceObservation row, OpField field)
    {
        switch (field.Name)
        {
            case PriceRules.ProductField:
                row.ProductId = OpValues.TryParseId(field.Value, out Guid? product) && product is { } id ? id : throw Unreadable(field);
                break;
            case PriceRules.PriceCentsField:
                row.PriceCents = OpValues.TryParseCents(field.Value, out long cents) ? cents : throw Unreadable(field);
                break;
            case PriceRules.ObservedOnField:
                row.ObservedOn = OpValues.TryParseDate(field.Value, out DateOnly date) ? date : throw Unreadable(field);
                break;
            case PriceRules.SourceField:
                row.Source = PriceSources.TryParse(field.Value, out PriceSource source) ? source : throw Unreadable(field);
                break;
            case PriceRules.PerKgCentsField:
                row.PerKgCents = field.Value is null ? null
                    : OpValues.TryParseCents(field.Value, out long perKg) ? perKg
                    : throw Unreadable(field);
                break;
            default:
                throw new ArgumentException($"A price has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(PriceObservation row) =>
    [
        new(PriceRules.ProductField, OpValues.FormatId(row.ProductId)),
        new(PriceRules.PriceCentsField, OpValues.FormatCents(row.PriceCents)),
        new(PriceRules.ObservedOnField, OpValues.FormatDate(row.ObservedOn)),
        new(PriceRules.SourceField, PriceSources.Format(row.Source)),
        new(PriceRules.PerKgCentsField, row.PerKgCents is { } perKg ? OpValues.FormatCents(perKg) : null),
    ];

    private static ArgumentException Unreadable(OpField field) =>
        new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}

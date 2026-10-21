using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Prices;

namespace AshersLab.MealPlanner.Shared.Data.Prices;

/// <summary>
/// One product's price for one pack, seen on one day. Added, sometimes deleted as a mistake, never edited: the history
/// is the point. The same row in the server's database and in every browser's; only ever written by applying an op.
/// </summary>
public class PriceObservation : ISyncedRow
{
    public Guid Id { get; set; }

    /// <summary>Not a foreign key: a price of a product deleted elsewhere is kept, and shown nowhere.</summary>
    public Guid ProductId { get; set; }

    /// <summary>One pack, in whole cents.</summary>
    public long PriceCents { get; set; }

    public DateOnly ObservedOn { get; set; }

    public PriceSource Source { get; set; }

    /// <summary>For variable-weight meat: the price per kilogram, in cents. Null otherwise.</summary>
    public long? PerKgCents { get; set; }

    public bool Deleted { get; set; }

    public PriceReading Reading() => new(Id, PriceCents, ObservedOn, Source, PerKgCents);
}

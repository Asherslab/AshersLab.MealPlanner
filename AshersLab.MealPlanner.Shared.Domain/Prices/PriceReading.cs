namespace AshersLab.MealPlanner.Shared.Domain.Prices;

/// <summary>One price observation, as costing reads it - free of the database row.</summary>
public sealed record PriceReading(Guid Id, long PriceCents, DateOnly ObservedOn, PriceSource Source, long? PerKgCents);

public static class PriceReadings
{
    /// <summary>
    /// Newest first: by the day seen, then - two on one day - by id. Ids are GUIDv7, so the later-entered one is
    /// first, and every device orders the same readings the same way.
    /// </summary>
    public static IEnumerable<PriceReading> NewestFirst(IEnumerable<PriceReading> readings) =>
        readings.OrderByDescending(r => r.ObservedOn).ThenByDescending(r => r.Id);

    /// <summary>The observation a price is taken from: the newest, whatever its source. Null when there is none.</summary>
    public static PriceReading? Latest(IEnumerable<PriceReading> readings) => NewestFirst(readings).FirstOrDefault();
}

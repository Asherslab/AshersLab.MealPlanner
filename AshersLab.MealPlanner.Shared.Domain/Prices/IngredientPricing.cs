using AshersLab.MealPlanner.Shared.Domain.Ingredients;

namespace AshersLab.MealPlanner.Shared.Domain.Prices;

/// <summary>One product of an ingredient, with its pack size in the ingredient's base unit and every price seen for it.</summary>
public sealed record PricedProduct(Guid ProductId, Guid StoreId, decimal PackQuantity, IReadOnlyList<PriceReading> Readings);

/// <summary>An ingredient's price: per base unit, and the product and observation it was taken from.</summary>
/// <param name="FromUsualStore">
/// False when the usual store has no priced product and this is the cheapest elsewhere - worth showing, because the
/// shopping list will send the planner to the usual store anyway.
/// </param>
public sealed record IngredientPrice(UnitPrice UnitPrice, Guid ProductId, Guid StoreId, PriceReading Reading, bool FromUsualStore);

/// <summary>
/// Step 1 of "How a cost is computed" (docs/modules/catalogue/products-and-prices.md): what one gram, millilitre or each
/// of an ingredient costs. Run identically in the browser and on the server.
/// </summary>
public static class IngredientPricing
{
    /// <summary>
    /// The ingredient's price per base unit, or null when it is unpriced - never zero.
    /// <list type="number">
    /// <item>Each product is priced by its latest observation (<see cref="PriceReadings.Latest"/>, any source); a
    /// product with none, or with no pack size, prices nothing.</item>
    /// <item>A product at the <paramref name="usualStoreId"/> wins. Two there (two sizes): the cheaper per unit.</item>
    /// <item>Otherwise the cheapest per unit at any store.</item>
    /// </list>
    /// Ties - equal per-unit prices - go to the newer observation, then the product id, so every device picks the
    /// same product.
    /// </summary>
    /// <param name="unit">The ingredient's base unit: a per-kg price prices grams, and nothing else.</param>
    /// <param name="products">This ingredient's live products. The caller leaves out deleted ones.</param>
    public static IngredientPrice? Price(BaseUnit unit, Guid? usualStoreId, IEnumerable<PricedProduct> products)
    {
        List<IngredientPrice> priced = [];
        foreach (PricedProduct product in products)
        {
            if (product.PackQuantity <= 0 || PriceReadings.Latest(product.Readings) is not { } latest)
                continue;

            priced.Add(new IngredientPrice(
                PerUnit(unit, product.PackQuantity, latest), product.ProductId, product.StoreId, latest,
                FromUsualStore: usualStoreId is { } usual && product.StoreId == usual));
        }

        List<IngredientPrice> usualOnes = [.. priced.Where(p => p.FromUsualStore)];
        return Cheapest(usualOnes.Count > 0 ? usualOnes : priced);
    }

    /// <summary>
    /// One observation as a price per base unit. Variable-weight meat (an ingredient in grams with a per-kg price) is
    /// priced by the kilo, since the pack price was for whatever that pack weighed; everything else by the pack.
    /// </summary>
    public static UnitPrice PerUnit(BaseUnit unit, decimal packQuantity, PriceReading reading) =>
        unit == BaseUnit.Gram && reading.PerKgCents is { } perKg
            ? new UnitPrice(perKg, 1000)
            : new UnitPrice(reading.PriceCents, packQuantity);

    private static IngredientPrice? Cheapest(List<IngredientPrice> candidates) =>
        candidates
            .OrderBy(p => p.UnitPrice)
            .ThenByDescending(p => p.Reading.ObservedOn)
            .ThenByDescending(p => p.Reading.Id)
            .ThenBy(p => p.ProductId)
            .FirstOrDefault();
}

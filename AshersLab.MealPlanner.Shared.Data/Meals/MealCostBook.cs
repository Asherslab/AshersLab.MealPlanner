using AshersLab.MealPlanner.Shared.Data.Ingredients;
using AshersLab.MealPlanner.Shared.Data.Prices;
using AshersLab.MealPlanner.Shared.Data.Products;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Meals;

/// <summary>
/// Everything costing a meal reads - live ingredients, their products and prices, and every meal's ingredient lines -
/// loaded once from either database, so a page listing fifty meals reads the tables once, not fifty times. The
/// arithmetic is <see cref="MealCosting"/>'s and <see cref="IngredientPricing"/>'s; this only gathers their inputs.
/// </summary>
public sealed class MealCostBook
{
    private readonly Dictionary<Guid, Ingredient>          _ingredients;
    private readonly ILookup<Guid?, Product>               _products;
    private readonly ILookup<Guid, PriceReading>           _readings;
    private readonly ILookup<Guid, MealIngredient>         _lines;
    private readonly Dictionary<Guid, IngredientPrice?>    _prices = [];

    private MealCostBook(List<Ingredient> ingredients, List<Product> products, List<PriceObservation> prices, List<MealIngredient> lines)
    {
        _ingredients = ingredients.ToDictionary(i => i.Id);
        _products    = products.ToLookup(p => p.IngredientId);
        _readings    = prices.ToLookup(o => o.ProductId, o => o.Reading());
        // Creation order: ids are GUIDv7, minted when the line was added.
        _lines       = lines.OrderBy(l => l.Id).ToLookup(l => l.MealId);
    }

    public static async Task<MealCostBook> LoadAsync(DbContext db, CancellationToken cancellationToken = default) =>
        new(await db.Set<Ingredient>().AsNoTracking().Where(i => !i.Deleted).ToListAsync(cancellationToken),
            await db.Set<Product>().AsNoTracking().Where(p => !p.Deleted && p.IngredientId != null).ToListAsync(cancellationToken),
            await db.Set<PriceObservation>().AsNoTracking().Where(o => !o.Deleted).ToListAsync(cancellationToken),
            await db.Set<MealIngredient>().AsNoTracking().Where(l => !l.Deleted).ToListAsync(cancellationToken));

    /// <summary>The live ingredient, or null when it is gone (deleted, or not arrived yet).</summary>
    public Ingredient? Ingredient(Guid id) => _ingredients.GetValueOrDefault(id);

    public IEnumerable<Ingredient> Ingredients => _ingredients.Values;

    /// <summary>A meal's live ingredient lines, in the order they were added.</summary>
    public IReadOnlyList<MealIngredient> LinesOf(Guid mealId) => [.. _lines[mealId]];

    /// <summary>Every live line that uses the ingredient, for "is it still used?".</summary>
    public IEnumerable<MealIngredient> LinesUsing(Guid ingredientId) => _lines.SelectMany(g => g).Where(l => l.IngredientId == ingredientId);

    /// <summary>What a gram, millilitre or each of the ingredient costs; null when it is unpriced or gone.</summary>
    public IngredientPrice? PriceOf(Guid ingredientId)
    {
        if (_prices.TryGetValue(ingredientId, out IngredientPrice? held))
            return held;

        IngredientPrice? price = Ingredient(ingredientId) is { } ingredient
            ? IngredientPricing.Price(ingredient.BaseUnit, ingredient.UsualStoreId,
                _products[ingredientId].Select(p => new PricedProduct(p.Id, p.StoreId, p.PackQuantity, [.. _readings[p.Id]])))
            : null;
        return _prices[ingredientId] = price;
    }

    /// <summary>The meal's batch and per-serve cost, with one entry in <see cref="MealCost.Lines"/> per line of <see cref="LinesOf"/>.</summary>
    public MealCost CostOf(Meal meal) =>
        MealCosting.Cost(meal.Kind, meal.ServesPerBatch, meal.FixedCostCents,
            [.. LinesOf(meal.Id).Select(l => new CostLine(l.Quantity, PriceOf(l.IngredientId)?.UnitPrice))]);
}

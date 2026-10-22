using AshersLab.MealPlanner.Shared.Data.Categories;
using AshersLab.MealPlanner.Shared.Data.Ingredients;
using AshersLab.MealPlanner.Shared.Data.Meals;
using AshersLab.MealPlanner.Shared.Data.Pantry;
using AshersLab.MealPlanner.Shared.Data.Planning;
using AshersLab.MealPlanner.Shared.Data.Prices;
using AshersLab.MealPlanner.Shared.Data.Products;
using AshersLab.MealPlanner.Shared.Data.Stores;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Pantry;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Shopping;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Shopping;

/// <summary>One ingredient's line on a list: what to buy (<see cref="LinePlan"/>) and how it is ticked.</summary>
/// <param name="ItemId">Its tick's id (<see cref="ShoppingItemRules.TickId"/>), whether or not it has been ticked yet.</param>
/// <param name="Planned">False for a ticked line the plan no longer needs: kept on the list so the tick does not vanish.</param>
public sealed record ShoppingLine(Ingredient Ingredient, Guid ItemId, LinePlan Plan, ShoppingTick Tick, bool Planned)
{
    /// <summary>What a <see cref="ShoppingTick.Bought"/> tick puts into the pantry: the whole packs, or the shortfall when no pack is known.</summary>
    public decimal BoughtQuantity => Plan.Pack?.Quantity ?? Plan.Shortfall;

    /// <summary>
    /// What a <see cref="ShoppingTick.HaveIt"/> tick tells the pantry: the shortfall - it had less than the meals need, and
    /// the shopper says there is enough. Zero for a line already covered.
    /// </summary>
    public decimal HaveItAdjustment => Planned && Plan.Need == LineNeed.Buy ? Plan.Shortfall : 0;
}

/// <summary>One store's part of a list: its lines by category, then its extra items.</summary>
public sealed record StoreGroup(Store? Store, IReadOnlyList<(Category? Category, IReadOnlyList<ShoppingLine> Lines)> Categories, IReadOnlyList<ShoppingListItem> Extras);

/// <summary>
/// A shopping list for a date range, from either database. <b>Its lines are computed, never stored</b>: for every calendar
/// entry in range that is not skipped, what its meal uses (<see cref="PantryBook"/>: a batch once, on its cook date; a
/// simple meal per eater), summed per ingredient, less the pantry's stock at the start of the range - read <b>without this
/// list's own ticks</b>, so ticking a line bought does not turn it into "in the pantry" while the shopper is still in the
/// shop. Then whole packs (<see cref="ShoppingPlan"/>), grouped by usual store, then category.
/// </summary>
/// <remarks>
/// Only the ticks and extra items are rows (<see cref="ShoppingListItem"/>). A ticked line the plan no longer needs stays
/// on the list. Staples are on it only when running low or out, or ticked.
/// </remarks>
public sealed class ShoppingBook
{
    private ShoppingBook(DateOnly startsOn, DateOnly endsOn, List<ShoppingLine> lines, List<ShoppingListItem> extras,
                         List<Store> stores, List<Category> categories, Cost spend, Cost fixedMeals, Cost eaten)
    {
        StartsOn   = startsOn;
        EndsOn     = endsOn;
        Lines      = lines;
        Extras     = extras;
        FixedMeals = fixedMeals;
        Spend      = spend;
        Eaten      = eaten;

        Dictionary<Guid, Store> storeById = stores.ToDictionary(s => s.Id);
        List<Store?> storeOrder = [.. stores.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase), null];
        Store? StoreOf(Guid? id) => id is { } s ? storeById.GetValueOrDefault(s) : null;

        Dictionary<Guid, Category> categoryById = categories.ToDictionary(c => c.Id);
        List<Category?> categoryOrder = [.. categories, null];

        List<ShoppingLine> toBuy = [.. lines.Where(l => l.Plan.Need != LineNeed.InPantry)];
        Groups = [.. storeOrder
            .Select(store => new StoreGroup(store,
                [.. categoryOrder
                    .Select(c => (c, (IReadOnlyList<ShoppingLine>)[.. toBuy
                        .Where(l => StoreOf(l.Ingredient.UsualStoreId) == store
                                    && (l.Ingredient.CategoryId is { } cat ? categoryById.GetValueOrDefault(cat) : null) == c)
                        .OrderBy(l => l.Ingredient.Name, StringComparer.CurrentCultureIgnoreCase)]))
                    .Where(g => g.Item2.Count > 0)],
                [.. extras.Where(e => StoreOf(e.StoreId) == store).OrderBy(e => e.Id)]))
            .Where(g => g.Categories.Count > 0 || g.Extras.Count > 0)];
        InPantry = [.. lines.Where(l => l.Plan.Need == LineNeed.InPantry).OrderBy(l => l.Ingredient.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public DateOnly StartsOn { get; }

    public DateOnly EndsOn { get; }

    public Guid ListId => ShoppingListRules.ListId(StartsOn, EndsOn);

    /// <summary>Every line, to buy and in the pantry.</summary>
    public IReadOnlyList<ShoppingLine> Lines { get; }

    public IReadOnlyList<ShoppingListItem> Extras { get; }

    /// <summary>The lines to buy and the extra items, by store (named stores A-Z, then no store), then category.</summary>
    public IReadOnlyList<StoreGroup> Groups { get; }

    /// <summary>Lines the pantry covers entirely: shown so they can be checked, not bought.</summary>
    public IReadOnlyList<ShoppingLine> InPantry { get; }

    /// <summary>
    /// What the shop costs - the "Spend" view: the whole packs every line to buy buys (except those ticked "have it"), the
    /// extra items, and the range's fixed-cost meals. Leftovers and pantry stock cost nothing here: they were bought already.
    /// </summary>
    public Cost Spend { get; }

    /// <summary>The range's fixed-cost meals (takeaway): a serve each eater, at the meal's amount shared over its serves.</summary>
    public Cost FixedMeals { get; }

    /// <summary>
    /// What the range's food was worth - the "Eaten" view: every serve eaten at its meal's per-serve cost, by quantity, so
    /// half a jar of mayo costs half a jar the week it is eaten. Leftovers cost a serve of the batch they eat.
    /// </summary>
    public Cost Eaten { get; }

    public static async Task<ShoppingBook> LoadAsync(DbContext db, DateOnly startsOn, DateOnly endsOn, CancellationToken cancellationToken = default)
    {
        Guid listId = ShoppingListRules.ListId(startsOn, endsOn);
        List<ShoppingListItem> items = await db.Set<ShoppingListItem>().AsNoTracking().Where(i => !i.Deleted && i.ListId == listId)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, ShoppingListItem> ticks = items.Where(i => i.IngredientId is not null)
            .GroupBy(i => i.IngredientId!.Value).ToDictionary(g => g.Key, g => g.First());

        HashSet<Guid> itemIds = [.. items.Select(i => i.Id)];
        PantryBook pantry = await PantryBook.LoadAsync(db, startsOn, endsOn, cancellationToken, itemIds);
        // What each tick put in the pantry: a line the plan has since dropped still bought it.
        Dictionary<Guid, decimal> bought = StockMovement.Counting((await db.Set<StockMovement>().AsNoTracking()
                .Where(m => !m.Deleted && m.ShoppingItemId != null).ToListAsync(cancellationToken)).Where(m => itemIds.Contains(m.ShoppingItemId!.Value)),
                ticks.Values.ToDictionary(t => t.Id, t => t.Tick))
            .Where(m => m.Reason == StockReason.Bought).ToDictionary(m => m.ShoppingItemId!.Value, m => m.Quantity);
        ILookup<Guid?, Product> products = (await db.Set<Product>().AsNoTracking().Where(p => !p.Deleted && p.IngredientId != null)
            .ToListAsync(cancellationToken)).ToLookup(p => p.IngredientId);
        ILookup<Guid, PriceReading> readings = (await db.Set<PriceObservation>().AsNoTracking().Where(o => !o.Deleted)
            .ToListAsync(cancellationToken)).ToLookup(o => o.ProductId, o => o.Reading());
        List<PackOption> PacksOf(Guid ingredient) =>
            [.. products[ingredient].Select(p => new PackOption(p.Id, p.StoreId, p.PackQuantity, PriceReadings.Latest(readings[p.Id])?.PriceCents))];

        List<ShoppingLine> lines = [];
        foreach (Ingredient ingredient in pantry.Ingredients.Values)
        {
            ShoppingTick tick = ticks.GetValueOrDefault(ingredient.Id)?.Tick ?? ShoppingTick.None;
            Guid itemId = ShoppingItemRules.TickId(listId, ingredient.Id);
            if (ingredient.IsStaple)
            {
                bool low = ingredient.StapleStock != StapleStock.Have;
                if (low || tick != ShoppingTick.None)
                    lines.Add(new ShoppingLine(ingredient, itemId, ShoppingPlan.Staple(ingredient.UsualStoreId, PacksOf(ingredient.Id)), tick, low));
                continue;
            }

            decimal needed = pantry.UsesOf(ingredient.Id).Where(u => u.Day >= startsOn && u.Day <= endsOn).Sum(u => u.Quantity);
            if (needed == 0 && tick == ShoppingTick.None)
                continue;
            LinePlan plan = needed > 0
                ? ShoppingPlan.Line(needed, pantry.At(ingredient.Id, startsOn), ingredient.UsualStoreId, PacksOf(ingredient.Id))
                : ShoppingPlan.Unplanned(bought.GetValueOrDefault(itemId), ingredient.UsualStoreId, PacksOf(ingredient.Id));
            lines.Add(new ShoppingLine(ingredient, itemId, plan, tick, needed > 0));
        }

        List<ShoppingListItem> extras = [.. items.Where(i => i.IsExtra)];
        (Cost fixedMeals, Cost eaten) = await MealsAsync(db, startsOn, endsOn, cancellationToken);
        Cost spend = ShoppingPlan.Sum([
            // A staple ticked bought is no longer running low, but it was bought.
            .. lines.Where(l => (l.Planned || l.Tick == ShoppingTick.Bought) && l.Tick != ShoppingTick.HaveIt).Select(l => ShoppingPlan.CostOf(l.Plan)),
            .. extras.Select(e => ShoppingPlan.Priced(e.PriceCents)),
            fixedMeals,
        ]);

        List<Store> stores = await db.Set<Store>().AsNoTracking().Where(s => !s.Deleted).ToListAsync(cancellationToken);
        List<Category> categories = [.. CategoryEntity.InOrder(await db.Set<Category>().AsNoTracking().Where(c => !c.Deleted).ToListAsync(cancellationToken))];
        return new ShoppingBook(startsOn, endsOn, lines, extras, stores, categories, spend, fixedMeals, eaten);
    }

    /// <summary>The range's fixed-cost meals, and every serve eaten at its per-serve cost. Skipped entries count in neither.</summary>
    private static async Task<(Cost FixedMeals, Cost Eaten)> MealsAsync(DbContext db, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        CalendarBook calendar = await CalendarBook.LoadAsync(db, from, to, cancellationToken);
        ServesBook serves = await ServesBook.LoadAsync(db, from, to, cancellationToken);
        MealCostBook costs = await MealCostBook.LoadAsync(db, cancellationToken);
        Dictionary<Guid, Meal> meals = await db.Set<Meal>().AsNoTracking().Where(m => !m.Deleted).ToDictionaryAsync(m => m.Id, cancellationToken);
        IReadOnlyDictionary<Guid, decimal> household = await Household.PortionsAsync(db, cancellationToken);

        List<Cost> fixedMeals = [];
        List<Cost> eaten = [];
        for (DateOnly day = from; day <= to; day = day.AddDays(1))
        {
            foreach (PlannedEntry entry in calendar.On(day))
            {
                if (entry.Skipped || entry.MealId is not { } mealId || meals.GetValueOrDefault(mealId) is not { } meal)
                    continue;
                if (meal.Kind == MealKind.Leftovers)
                {
                    // A serve of the batch it eats, for each serve it got: nothing to eat costs nothing.
                    if (serves.LeftoversOf(entry.Id) is { Got: > 0, BatchId: { } batch } got
                        && serves.Entry(batch)?.MealId is { } batchMeal && meals.GetValueOrDefault(batchMeal) is { } cooked)
                        eaten.Add(ShoppingPlan.Serves(got.Got, costs.CostOf(cooked).PerServe));
                    continue;
                }

                Cost serve = ShoppingPlan.Serves(Serves.PortionsOf(entry.People, household), costs.CostOf(meal).PerServe);
                eaten.Add(serve);
                if (meal.Kind == MealKind.FixedCost)
                    fixedMeals.Add(serve);
            }
        }
        return (ShoppingPlan.Sum(fixedMeals), ShoppingPlan.Sum(eaten));
    }
}

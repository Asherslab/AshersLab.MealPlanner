using AshersLab.MealPlanner.Shared.Data.Ingredients;
using AshersLab.MealPlanner.Shared.Data.Meals;
using AshersLab.MealPlanner.Shared.Data.Planning;
using AshersLab.MealPlanner.Shared.Data.Shopping;
using AshersLab.MealPlanner.Shared.Domain.Shopping;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Pantry;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Pantry;

/// <summary>What one calendar entry takes from one ingredient: "Stew on Thursday uses 500 g of mince".</summary>
public sealed record StockUse(Guid IngredientId, DateOnly Day, Guid EntryId, Guid MealId, decimal Quantity);

/// <summary>
/// Every ingredient's stock, from either database: its movements and what the calendar's meals use, folded by
/// <see cref="Stock"/>. Nothing here is stored. <b>What meals use is read from the calendar, never written as movements</b>,
/// so a skip, a move, a replan or a changed plan changes the stock at once on every device, with no op of its own - and a
/// draw-down can never be written twice, or left behind by a meal that was then skipped.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A <c>Cooked</c> entry uses its meal's lines once, on the day it is cooked (the batch; leftovers use nothing). A
///   <c>Simple</c> entry's lines are one serve's, so it uses them once per eater. Other kinds use nothing.</item>
/// <item>A skipped entry, or one the calendar no longer shows, uses nothing (<see cref="CalendarBook"/>).</item>
/// <item>A line with no quantity yet uses nothing: there is no amount to take.</item>
/// <item><b>Staples are left out entirely</b> - their stock is the hand-set <see cref="Ingredient.StapleStock"/>.</item>
/// <item>It reads the calendar from the first movement on (or the day asked for, if earlier): before an ingredient's first
///   movement there is no estimate to draw down (<see cref="Stock"/> ignores those uses). A meal's lines are read as they are now, so changing a recipe changes what past meals used back to the
///   last count - recipes are approximate; a count puts it right.</item>
/// </list>
/// </remarks>
public sealed class PantryBook
{
    private readonly ILookup<Guid, StockUse>      _uses;
    private readonly ILookup<Guid, StockMovement> _movements;

    private PantryBook(StockLedger ledger, Dictionary<Guid, Ingredient> ingredients, List<StockMovement> movements, List<StockUse> uses)
    {
        Ledger      = ledger;
        Ingredients = ingredients;
        _movements  = movements.OrderByDescending(m => m.OccurredOn).ThenByDescending(m => m.Id).ToLookup(m => m.IngredientId);
        _uses       = uses.ToLookup(u => u.IngredientId);
    }

    public StockLedger Ledger { get; }

    /// <summary>Every live ingredient, staples included.</summary>
    public IReadOnlyDictionary<Guid, Ingredient> Ingredients { get; }

    /// <summary>The stock at the start of <paramref name="day"/>: what a shopping list starting that day has (<see cref="StockLedger.At"/>).</summary>
    public StockLevel At(Guid ingredient, DateOnly day) => Ledger.At(ingredient, day);

    /// <summary>The ingredient's movements, newest first.</summary>
    public IEnumerable<StockMovement> MovementsOf(Guid ingredient) => _movements[ingredient];

    /// <summary>What the calendar takes from the ingredient, in day order, from its first movement to the last day loaded.</summary>
    public IEnumerable<StockUse> UsesOf(Guid ingredient) => _uses[ingredient];

    /// <param name="from">
    /// The first day whose uses are wanted even for an ingredient nobody has counted yet ("coming up"); days before it are
    /// read only back to the first movement, which is all the stock needs.
    /// </param>
    /// <param name="through">The last day whose meals are drawn down: the furthest a projection is wanted.</param>
    /// <param name="ignoringItems">
    /// Shopping-list items whose ticks' movements are left out: a list reads the stock as it was before anyone ticked it, so
    /// a line ticked bought stays a line to buy (<see cref="StockMovement.ShoppingItemId"/>).
    /// </param>
    public static async Task<PantryBook> LoadAsync(DbContext db, DateOnly from, DateOnly through, CancellationToken cancellationToken = default,
                                                   IReadOnlySet<Guid>? ignoringItems = null)
    {
        Dictionary<Guid, Ingredient> ingredients = await db.Set<Ingredient>().AsNoTracking().Where(i => !i.Deleted)
            .ToDictionaryAsync(i => i.Id, cancellationToken);
        Dictionary<Guid, ShoppingTick> ticks = await db.Set<ShoppingListItem>().AsNoTracking().Where(i => !i.Deleted && i.IngredientId != null)
            .ToDictionaryAsync(i => i.Id, i => i.Tick, cancellationToken);
        List<StockMovement> movements = [.. StockMovement.Counting(await db.Set<StockMovement>().AsNoTracking().Where(m => !m.Deleted).ToListAsync(cancellationToken), ticks)
            .Where(m =>ingredients.TryGetValue(m.IngredientId, out Ingredient? i) && !i.IsStaple
                        && !(m.ShoppingItemId is { } item && ignoringItems?.Contains(item) == true))];

        DateOnly first = movements.Count > 0 && movements.Min(m => m.OccurredOn) is var earliest && earliest < from ? earliest : from;
        List<StockUse> uses = first <= through ? await UsesAsync(db, ingredients, first, through, cancellationToken) : [];

        StockLedger ledger = Stock.Run([
            .. movements.Select(m => m.Event()),
            .. uses.Select(u => new StockEvent(u.IngredientId, u.Day, StockChange.Used, u.Quantity, u.EntryId)),
        ]);
        return new PantryBook(ledger, ingredients, movements, uses);
    }

    private static async Task<List<StockUse>> UsesAsync(DbContext db, Dictionary<Guid, Ingredient> ingredients, DateOnly from, DateOnly to,
                                                        CancellationToken cancellationToken)
    {
        CalendarBook calendar = await CalendarBook.LoadAsync(db, from, to, cancellationToken);
        Dictionary<Guid, Meal> meals = await db.Set<Meal>().AsNoTracking().Where(m => !m.Deleted).ToDictionaryAsync(m => m.Id, cancellationToken);
        ILookup<Guid, MealIngredient> lines = (await db.Set<MealIngredient>().AsNoTracking().Where(l => !l.Deleted && l.Quantity != null)
            .ToListAsync(cancellationToken)).ToLookup(l => l.MealId);
        int household = await db.Set<Person>().AsNoTracking().CountAsync(p => !p.Deleted, cancellationToken);

        List<StockUse> uses = [];
        for (DateOnly day = from; day <= to; day = day.AddDays(1))
        {
            foreach (PlannedEntry entry in calendar.On(day))
            {
                if (entry.Skipped || entry.MealId is not { } mealId || meals.GetValueOrDefault(mealId) is not { } meal)
                    continue;
                int times = meal.Kind switch
                {
                    MealKind.Cooked => 1,
                    MealKind.Simple => Serves.EatersOf(entry.People, household),
                    _               => 0,
                };
                if (times == 0)
                    continue;
                foreach (MealIngredient line in lines[mealId])
                    if (ingredients.TryGetValue(line.IngredientId, out Ingredient? ingredient) && !ingredient.IsStaple && line.Quantity is { } quantity)
                        uses.Add(new StockUse(line.IngredientId, day, entry.Id, mealId, quantity * times));
            }
        }
        return uses;
    }
}

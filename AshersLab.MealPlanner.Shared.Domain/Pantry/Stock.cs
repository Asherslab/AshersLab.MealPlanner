namespace AshersLab.MealPlanner.Shared.Domain.Pantry;

/// <summary>What changed an ingredient's stock: a stored movement (<see cref="StockReason"/>), or a meal on the calendar.</summary>
public enum StockChange
{
    Bought,
    Adjusted,
    Counted,

    /// <summary>A cooked or simple meal on its day: never stored, read from the calendar.</summary>
    Used,
}

/// <summary>
/// One change to one ingredient's stock, in its base unit. For <see cref="StockChange.Used"/> the quantity is the amount
/// used (positive); for the others it is the movement's own quantity.
/// </summary>
/// <param name="Id">Orders two hand changes on one day: the movement's id (GUIDv7, minted when it was made).</param>
public sealed record StockEvent(Guid IngredientId, DateOnly Day, StockChange Change, decimal Quantity, Guid Id)
{
    public static StockChange From(StockReason reason) => reason switch
    {
        StockReason.Bought   => StockChange.Bought,
        StockReason.Adjusted => StockChange.Adjusted,
        _                    => StockChange.Counted,
    };
}

public enum StockState
{
    /// <summary>Nothing was ever bought or counted: no estimate at all - as good as none for the shopping list.</summary>
    NotTracked,

    InStock,

    /// <summary>Someone said so: counted at nothing, or adjusted down to it.</summary>
    Out,

    /// <summary>The meals drew it down to nothing, or past: the estimate says it is gone, and has probably drifted.</summary>
    ProbablyOut,
}

/// <summary>An ingredient's stock at a moment: never below zero.</summary>
public sealed record StockLevel(decimal Quantity, StockState State)
{
    public static readonly StockLevel NotTracked = new(0, StockState.NotTracked);

    public bool Tracked => State != StockState.NotTracked;
}

/// <summary>
/// One day of one ingredient's stock. <paramref name="Start"/> is after the day's hand changes and before its meals;
/// <paramref name="End"/> after its meals.
/// </summary>
/// <param name="Used">What the day's meals wanted.</param>
/// <param name="Short">What they wanted beyond what there was: how far the estimate fell short.</param>
public sealed record StockDay(DateOnly Day, StockLevel Start, StockLevel End, decimal Used, decimal Short);

/// <summary>
/// <b>Stock on hand is counted, never stored.</b> It is the ingredient's movements - bought, adjusted, counted - and what
/// the calendar's meals use, folded in order. The rules:
/// <list type="bullet">
/// <item><b>Never below zero.</b> A draw past zero leaves nothing and the state <see cref="StockState.ProbablyOut"/> - the
///   estimate drifted (recipes are approximate, nobody weighs the mayo) - and the next purchase starts from nothing, so the
///   drift never eats into what was bought.</item>
/// <item><b>A count sets the level</b>; bought and adjusted add to it (an adjustment down past zero is out).</item>
/// <item><b>On one day, the hand changes come before the meals</b>, in the order they were made. A count made in the
///   evening after cooking then has that dinner taken off again: stock reads low, never high - the safe way to be wrong
///   for a shopping list.</item>
/// <item>Meals do nothing to an ingredient before its first movement: there is no estimate to draw down.</item>
/// </list>
/// </summary>
public static class Stock
{
    public static StockLedger Run(IEnumerable<StockEvent> events)
    {
        Dictionary<Guid, List<StockDay>> days = [];
        foreach (IGrouping<Guid, StockEvent> ingredient in events.GroupBy(e => e.IngredientId))
        {
            List<StockDay> list = [];
            StockLevel level = StockLevel.NotTracked;
            foreach (IGrouping<DateOnly, StockEvent> day in ingredient.GroupBy(e => e.Day).OrderBy(g => g.Key))
            {
                foreach (StockEvent hand in day.Where(e => e.Change != StockChange.Used).OrderBy(e => e.Id))
                    level = Apply(level, hand);
                if (!level.Tracked)
                    continue;

                decimal used = day.Where(e => e.Change == StockChange.Used).Sum(e => e.Quantity);
                StockLevel start = level;
                decimal shortBy = 0;
                if (used > 0)
                {
                    if (used >= start.Quantity)
                    {
                        shortBy = used - start.Quantity;
                        // Already out by someone's word stays out; anything else drawn to nothing is the estimate's guess.
                        level = new StockLevel(0, start is { Quantity: 0, State: StockState.Out } ? StockState.Out : StockState.ProbablyOut);
                    }
                    else
                        level = start with { Quantity = start.Quantity - used };
                }
                list.Add(new StockDay(day.Key, start, level, used, shortBy));
            }
            if (list.Count > 0)
                days[ingredient.Key] = list;
        }
        return new StockLedger(days);
    }

    private static StockLevel Apply(StockLevel level, StockEvent change)
    {
        decimal quantity = change.Change switch
        {
            StockChange.Counted => change.Quantity,
            _                   => level.Quantity + change.Quantity,
        };
        return quantity > 0 ? new StockLevel(quantity, StockState.InStock) : new StockLevel(0, StockState.Out);
    }
}

/// <summary>Every tracked ingredient's stock, day by day: what the pantry shows and what the shopping list subtracts.</summary>
public sealed class StockLedger(IReadOnlyDictionary<Guid, List<StockDay>> days)
{
    /// <summary>Ingredients with at least one movement.</summary>
    public IEnumerable<Guid> Tracked => days.Keys;

    /// <summary>Every day something changed the ingredient's stock, in order; empty when it is not tracked.</summary>
    public IReadOnlyList<StockDay> DaysOf(Guid ingredient) => days.GetValueOrDefault(ingredient) ?? [];

    /// <summary>
    /// The stock at the start of <paramref name="day"/> - after that day's hand changes, before its meals: what a shopping
    /// list starting that day has to work with. A day in the future is a projection: the plan's meals drawn down.
    /// </summary>
    public StockLevel At(Guid ingredient, DateOnly day) =>
        Last(ingredient, day) is { } last ? last.Day == day ? last.Start : last.End : StockLevel.NotTracked;

    /// <summary>The stock at the end of <paramref name="day"/>, after its meals.</summary>
    public StockLevel After(Guid ingredient, DateOnly day) => Last(ingredient, day)?.End ?? StockLevel.NotTracked;

    /// <summary>The first day from <paramref name="from"/> to <paramref name="to"/> whose meals use up the last of it.</summary>
    public StockDay? RunsOut(Guid ingredient, DateOnly from, DateOnly to) =>
        DaysOf(ingredient).FirstOrDefault(d => d.Day >= from && d.Day <= to && d.Used > 0 && d.Start.Quantity > 0 && d.End.Quantity == 0);

    private StockDay? Last(Guid ingredient, DateOnly day) => DaysOf(ingredient).LastOrDefault(d => d.Day <= day);
}

using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Pantry;

namespace AshersLab.MealPlanner.WASM.Features.Pantry;

/// <summary>What the pantry says about stock, in the planner's words.</summary>
public static class PantryText
{
    /// <summary>"about 1.5 kg", "out", "probably out", "not counted yet".</summary>
    public static string Level(StockLevel level, BaseUnit unit) => level.State switch
    {
        StockState.InStock     => $"about {Quantity.Format(level.Quantity, unit)}",
        StockState.Out         => "out",
        StockState.ProbablyOut => "probably out",
        _                      => "not counted yet",
    };

    /// <summary>"Thu 29 Oct".</summary>
    public static string Day(DateOnly day) => day.ToString("ddd d MMM");

    /// <summary>A movement in its history: "counted 250 g", "bought 2 kg", "threw out 100 g", "added 100 g".</summary>
    public static string Movement(StockReason reason, decimal quantity, BaseUnit unit) => reason switch
    {
        StockReason.Bought                   => $"bought {Quantity.Format(quantity, unit)}",
        StockReason.Counted when quantity == 0 => "out",
        StockReason.Counted                  => $"counted {Quantity.Format(quantity, unit)}",
        _ when quantity < 0                  => $"threw out {Quantity.Format(-quantity, unit)}",
        _                                    => $"added {Quantity.Format(quantity, unit)}",
    };

    /// <summary>A fraction of a pack, kept to the places a quantity may have.</summary>
    public static decimal Of(decimal pack, decimal share) => decimal.Round(pack * share, Quantity.MaxDecimals);
}

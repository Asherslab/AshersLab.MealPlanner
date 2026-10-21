using AshersLab.MealPlanner.Shared.Data.Meals;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Prices;

namespace AshersLab.MealPlanner.WASM.Features.Meals;

/// <summary>How a meal is described in a line: what kind it is and how many it makes.</summary>
public static class MealText
{
    /// <summary>"makes 4 serves", "single serve", "leftovers", "no cost", "$45.00 · feeds 3".</summary>
    public static string Detail(Meal meal) => meal.Kind switch
    {
        MealKind.Cooked    => $"makes {Serves(meal.ServesPerBatch)}",
        MealKind.Simple    => "single serve",
        MealKind.Leftovers => "leftovers",
        MealKind.NoCost    => "no cost",
        MealKind.FixedCost => $"{(meal.FixedCostCents is { } cents ? Money.Format(cents) : "no price")} · feeds {meal.ServesPerBatch}",
        _                  => "",
    };

    public static string Serves(int count) => count == 1 ? "1 serve" : $"{count} serves";
}

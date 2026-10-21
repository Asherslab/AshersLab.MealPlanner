using AshersLab.MealPlanner.Shared.Data.Meals;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Prices;

namespace AshersLab.MealPlanner.WASM.Features.Meals;

/// <summary>How a meal is described in a line: what kind it is, how many it makes, and what a serve costs.</summary>
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

    /// <summary>
    /// <see cref="Detail"/> and what a serve costs: "makes 4 serves · $3.20 a serve", "makes 4 serves · from $3.20 a
    /// serve + 2 unpriced", "single serve · no ingredients yet". Leftovers and no-cost meals say only what they are.
    /// </summary>
    public static string Detail(Meal meal, MealCost cost) => meal.Kind switch
    {
        MealKind.Leftovers or MealKind.NoCost => Detail(meal),
        _ when cost.PerServe is { } serve      => $"{Detail(meal)} · {serve.Format(" a serve")}",
        _                                      => $"{Detail(meal)} · no ingredients yet",
    };

    public static string Serves(int count) => count == 1 ? "1 serve" : $"{count} serves";
}

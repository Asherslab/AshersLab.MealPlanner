using System.Globalization;
using AshersLab.MealPlanner.Shared.Data.Shopping;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Shopping;

namespace AshersLab.MealPlanner.WASM.Features.Shopping;

/// <summary>How the shopping list words a line, in the planner's terms.</summary>
public static class ShoppingText
{
    /// <summary>"Mon 26 Oct".</summary>
    public static string Day(DateOnly day) => day.ToString("ddd d MMM", CultureInfo.InvariantCulture);

    /// <summary>"Mon 26 Oct – Sun 1 Nov".</summary>
    public static string Range(DateOnly from, DateOnly to) => from == to ? Day(from) : $"{Day(from)} – {Day(to)}";

    /// <summary>
    /// What to put in the trolley: "1 pack (1.25 kg spare)", "2 packs", "750 g" when no product gives a pack size,
    /// "1 pack · running low" for a staple, "in the pantry · need 300 g, have about 500 g".
    /// </summary>
    public static string Detail(ShoppingLine line)
    {
        BaseUnit unit = line.Ingredient.BaseUnit;
        LinePlan plan = line.Plan;
        string? packs = plan.Pack is { } pack
            ? (pack.Packs == 1 ? "1 pack" : $"{pack.Packs} packs") + (pack.Spare > 0 ? $" ({Quantity.Format(pack.Spare, unit)} spare)" : "")
            : null;
        return plan.Need switch
        {
            LineNeed.Staple   => $"{packs ?? "some"} · {line.Ingredient.StapleStock.Label().ToLowerInvariant()}",
            _ when !line.Planned => packs is null ? "not on the plan now" : $"{packs} · not on the plan now",
            LineNeed.InPantry =>$"need {Quantity.Format(plan.Needed, unit)}, have about {Quantity.Format(plan.Have, unit)}",
            _                 => (packs ?? Quantity.Format(plan.Shortfall, unit))
                                 + (plan.Have > 0 ? $" · need {Quantity.Format(plan.Needed, unit)}, have about {Quantity.Format(plan.Have, unit)}" : ""),
        };
    }

    /// <summary>What the progress figure counts: "to get · 2 done", or "to get · all done" once nothing is left.</summary>
    public static string ProgressLabel(int left, int done) => left == 0 && done > 0 ? "to get · all done" : $"to get · {done} done";

    /// <summary>
    /// A cost as the week tiles print it - the known amount big, the rest small around it - in pieces that read as
    /// <see cref="Cost.Format"/> end to end: ("from ", "$3.12", " + 1 unpriced"). With nothing priced the figure is "$?",
    /// never "$0.00".
    /// </summary>
    public static (string Before, string Figure, string After) Figure(Cost cost) =>
        cost.Complete ? ("", Money.Format(cost.Cents), "")
        : cost.Cents == 0 ? ("", "$?", $" · {cost.Missing} unpriced")
        : ("from ", Money.Format(cost.Cents), $" + {cost.Missing} unpriced");

    /// <summary>"$24.99", "unpriced", or nothing for a line in the pantry.</summary>
    public static string Cost(ShoppingLine line) => line.Plan.Need switch
    {
        LineNeed.InPantry               => "",
        _ when line.Plan.Pack?.Cents is { } c => Money.Format(c),
        _                               => "unpriced",
    };
}

using System.Globalization;
using AshersLab.MealPlanner.Shared.Data.Meals;
using AshersLab.MealPlanner.Shared.Data.Planning;
using AshersLab.MealPlanner.Shared.Domain.Meals;

namespace AshersLab.MealPlanner.WASM.Features.Planning;

/// <summary>How the calendar words dates and costs.</summary>
public static class CalendarText
{
    /// <summary>"Tue 27 Oct".</summary>
    public static string Day(DateOnly date) => date.ToString("ddd d MMM", CultureInfo.InvariantCulture);

    /// <summary>"Tuesday 27 October".</summary>
    public static string LongDay(DateOnly date) => date.ToString("dddd d MMMM", CultureInfo.InvariantCulture);

    /// <summary>"Week of Mon 26 Oct".</summary>
    public static string Week(DateOnly monday) => $"Week of {Day(monday)}";

    /// <summary>
    /// "the calendar on Tue 27 Oct" when a coming entry (<c>CalendarBook.ComingAsync</c>) uses something - the reason it
    /// cannot be deleted, beside the plans that use it - or null. Past entries do not hold anything back: they show
    /// "meal removed" instead.
    /// </summary>
    public static string? Use(IEnumerable<CalendarEntry> coming, DateOnly today, Func<CalendarEntry, bool> uses) =>
        coming.Where(e => (e.MovedDate ?? e.Date) >= today && uses(e)).Select(e => e.MovedDate ?? e.Date).Order().FirstOrDefault() is var first
        && first != default ? $"the calendar on {Day(first)}" : null;

    /// <summary>
    /// What a serve of the meal costs: "$3.20 a serve", "from $3.20 a serve + 2 unpriced" - never a low number for an
    /// incomplete cost - or what it is instead: "leftovers" (costed by its batch), "no cost", "no ingredients yet".
    /// </summary>
    public static string Serve(Meal meal, MealCost cost) => meal.Kind switch
    {
        MealKind.Leftovers                => "leftovers",
        MealKind.NoCost                   => "no cost",
        _ when cost.PerServe is { } serve => serve.Format(" a serve"),
        _                                 => "no ingredients yet",
    };
}

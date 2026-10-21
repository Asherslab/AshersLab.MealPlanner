using System.Globalization;
using AshersLab.MealPlanner.Shared.Domain.Planning;

namespace AshersLab.MealPlanner.WASM.Features.Planning;

/// <summary>How the calendar words batches and serves: short under an entry, a sentence in the week's notes.</summary>
public static class ServesText
{
    private static string Dow(DateOnly day) => day.ToString("ddd", CultureInfo.InvariantCulture);

    private static string Weekday(DateOnly day) => day.ToString("dddd", CultureInfo.InvariantCulture);

    private static string Serves(int n) => n == 1 ? "1 serve" : $"{n} serves";

    /// <summary>Under a cooked entry: "makes 6 · 2 left after Thu", "makes 6 · eaten by Wed", "not cooked".</summary>
    public static string Batch(BatchServes batch) =>
        !batch.Cooked ? "not cooked"
        : batch.Short > 0 ? $"makes {batch.Made} · {batch.Short} short"
        : batch.Leftovers.LastOrDefault() is { } last
            ? batch.Left > 0 ? $"makes {batch.Made} · {batch.Left} left after {Dow(last.Day)}" : $"makes {batch.Made} · eaten by {Dow(last.Day)}"
        : batch.Left > 0 ? $"makes {batch.Made} · {batch.Left} left over"
        : $"makes {batch.Made}";

    /// <summary>Under a leftovers entry: "Stew from Mon · 2 left after", or what is wrong.</summary>
    /// <param name="batchMeal">The batch's meal name; null when there is no batch.</param>
    public static string Leftovers(LeftoversServes eats, string? batchMeal, DateOnly? batchDay) => eats.Problem switch
    {
        LeftoversProblem.None        => $"{batchMeal} from {Dow(batchDay!.Value)} · {eats.LeftAfter} left after",
        LeftoversProblem.NotEnough   => $"only {eats.Got} of {eats.Need} serves of {batchMeal}",
        LeftoversProblem.NotCooked   => $"{batchMeal} from {(batchDay is { } d ? Dow(d) : "?")} wasn't cooked - nothing to eat",
        LeftoversProblem.BatchGone   => "its batch is gone - nothing to eat",
        LeftoversProblem.CookedLater => $"{batchMeal} isn't cooked until {(batchDay is { } d ? Dow(d) : "later")} - nothing to eat",
        _                            => "nothing to eat",
    };

    /// <summary>A week's note about a leftovers entry with less than it needs: "Saturday's leftovers has nothing to eat".</summary>
    public static string LeftoversNote(DateOnly day, LeftoversServes eats, string? batchMeal, DateOnly? batchDay) => eats.Problem switch
    {
        LeftoversProblem.NotEnough   => $"{Weekday(day)}'s leftovers has only {eats.Got} of {eats.Need} serves",
        LeftoversProblem.NotCooked   => $"{Weekday(day)}'s leftovers has nothing to eat: {batchMeal} on {(batchDay is { } d ? Weekday(d) : "its day")} was skipped",
        LeftoversProblem.BatchGone   => $"{Weekday(day)}'s leftovers has nothing to eat: the batch it was set to eat is gone",
        LeftoversProblem.CookedLater => $"{Weekday(day)}'s leftovers has nothing to eat: {batchMeal} isn't cooked until {(batchDay is { } d ? Weekday(d) : "later")}",
        _                            => $"{Weekday(day)}'s leftovers has nothing to eat",
    };

    /// <summary>A week's note about a batch with serves nobody is planned to eat: "2 serves of Stew from Monday will go uneaten".</summary>
    public static string UneatenNote(BatchServes batch, string meal) =>
        batch.Leftovers.LastOrDefault() is { } last
            ? $"{Serves(batch.Left)} of {meal} from {Weekday(batch.Day)} left after {Weekday(last.Day)} will go uneaten"
            : $"{Serves(batch.Left)} of {meal} from {Weekday(batch.Day)} will go uneaten";

    /// <summary>A batch in the entry sheet's "Eats from": "Stew · Mon 26 Oct · 4 left".</summary>
    public static string Choice(string meal, DateOnly day, int left) => $"{meal} · {CalendarText.Day(day)} · {left} left";
}

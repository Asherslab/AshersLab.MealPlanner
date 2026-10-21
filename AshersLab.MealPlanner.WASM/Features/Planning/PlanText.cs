using AshersLab.MealPlanner.Shared.Domain.Planning;

namespace AshersLab.MealPlanner.WASM.Features.Planning;

/// <summary>How the plan pages word things.</summary>
public static class PlanText
{
    /// <summary>"1 week", "4 weeks".</summary>
    public static string Weeks(int weeks) => weeks == 1 ? "1 week" : $"{weeks} weeks";

    /// <summary>Null when <paramref name="text"/> is a week count a plan may have; otherwise what to say.</summary>
    public static string? ReadWeeks(string? text, out int weeks)
    {
        if (!int.TryParse(text?.Trim(), out weeks))
            return "Type how many weeks, like 4.";
        return PlanTemplateRules.WeekCountProblem(weeks);
    }

    /// <summary>"Leftovers / Noodles / Pizza" - the usual one first.</summary>
    public static string Choices(IEnumerable<string> meals) => string.Join(" / ", meals);
}

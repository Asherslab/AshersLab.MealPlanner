using AshersLab.MealPlanner.Shared.Data.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>
/// Someone in the household. Added by the planner in the app, never seeded. The same row in the server's database and
/// in every browser's; only ever written by applying an op.
/// </summary>
public class Person : ISyncedRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>"#rrggbb" (<c>PersonRules.Palette</c>): their magnet's colour.</summary>
    public string Colour { get; set; } = "";

    public bool Deleted { get; set; }

    /// <summary>The household in a fixed order: by name, then id so it is the same everywhere.</summary>
    public static IEnumerable<Person> InOrder(IEnumerable<Person> people) =>
        people.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Id);
}

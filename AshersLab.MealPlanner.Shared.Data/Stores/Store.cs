using AshersLab.MealPlanner.Shared.Data.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Stores;

/// <summary>
/// Where groceries come from - Woolworths and Costco (seeded), and any the household adds. The same row in the
/// server's database and in every browser's; only ever written by applying an op.
/// </summary>
public class Store : ISyncedRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public bool Deleted { get; set; }
}

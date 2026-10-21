using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Meals;

namespace AshersLab.MealPlanner.Shared.Data.Meals;

/// <summary>
/// A meal the household eats. Its ingredients are rows of their own. The same row in the server's database and in
/// every browser's; only ever written by applying an op.
/// </summary>
public class Meal : ISyncedRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Not a foreign key (docs/modules/catalogue/catalogue.md): a group gone elsewhere shows as "No group".</summary>
    public Guid? GroupId { get; set; }

    public MealKind Kind { get; set; }

    /// <summary>How many serves a batch makes. 1 for a kind with no batch.</summary>
    public int ServesPerBatch { get; set; } = 1;

    /// <summary>For <see cref="MealKind.FixedCost"/>: the whole thing's price, in whole cents. Kept when the kind changes.</summary>
    public long? FixedCostCents { get; set; }

    public string? Notes { get; set; }

    public bool Deleted { get; set; }

    public MealDraft Draft() => new(Name, GroupId, Kind, ServesPerBatch, FixedCostCents, Notes);
}

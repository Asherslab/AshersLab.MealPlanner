using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Data.Models.Sync;
using AshersLab.MealPlanner.Grpc.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain;
using AshersLab.MealPlanner.Shared.Domain.Categories;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Stores;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Grpc.Features.Catalogue;

/// <summary>
/// The stores, shopping categories and meal groups every household starts with, written into the op log as the server's own ops - so they reach
/// every browser like any other edit, and the household can rename or delete them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not migration seed data.</b> A row a migration inserts has no op and no field clocks: browsers would never
/// pull it, and a snapshot would leave its fields out (<see cref="OpApplier.SnapshotAsync"/> sends only fields an
/// op set).
/// </para>
/// <para>
/// Each seed is written once per database, when its log holds no op for the seed's id - so a renamed seed keeps
/// its new name and a deleted one stays deleted. The ids are fixed so that every database's Woolworths is the same row: a browser's
/// queued edit to it survives the server database being replaced.
/// </para>
/// <para>
/// The clock is <see cref="Clock"/>, wall time zero: a seed is a default, and any edit anyone ever makes beats
/// it - including one made offline before the seed was written (a restore from a backup older than the seed).
/// It also never moves a browser's clock forward when pulled.
/// </para>
/// </remarks>
public static class CatalogueSeed
{
    /// <summary>The device id on every seed op. Not any browser's.</summary>
    public static readonly Guid Device = new("00000000-5eed-7000-8000-000000000001");

    public static readonly Hlc Clock = new(0, 0, Device);

    public static readonly Guid Woolworths = new("00000000-5eed-7000-8000-000000000101");
    public static readonly Guid Costco     = new("00000000-5eed-7000-8000-000000000102");

    /// <summary>The shopping categories of the planner's spreadsheet, in its order.</summary>
    public static readonly IReadOnlyList<string> Categories =
        ["Meat", "Dairy", "Fruit", "Vegetables", "Carbs/sides", "Pantry", "Fridge", "Freezer", "Household"];

    /// <summary>The seeded category's id, by its position in <see cref="Categories"/> (0 is Meat).</summary>
    public static Guid Category(int index) => new($"00000000-5eed-7000-8000-000000000{201 + index}");

    /// <summary>The meal groups of the planner's spreadsheet - its "meal ideas" columns - in its order.</summary>
    public static readonly IReadOnlyList<string> MealGroups =
        ["Beef", "Chicken & egg", "Fish", "Other", "Individual", "Side", "Snack"];

    /// <summary>The seeded meal group's id, by its position in <see cref="MealGroups"/> (0 is Beef).</summary>
    public static Guid MealGroup(int index) => new($"00000000-5eed-7000-8000-000000000{301 + index}");

    public sealed record Seed(SyncedEntity Type, Guid Id, IReadOnlyList<OpField> Fields);

    public static readonly IReadOnlyList<Seed> All =
    [
        new(SyncedEntities.Store, Woolworths, [new OpField(StoreRules.NameField, "Woolworths")]),
        new(SyncedEntities.Store, Costco,     [new OpField(StoreRules.NameField, "Costco")]),
        .. Categories.Select((name, i) => new Seed(SyncedEntities.Category, Category(i),
        [
            new OpField(CategoryRules.NameField, name),
            new OpField(CategoryRules.PositionField, ListPosition.Format(i + 1)),
        ])),
        .. MealGroups.Select((name, i) => new Seed(SyncedEntities.MealGroup, MealGroup(i),
        [
            new OpField(MealGroupRules.NameField, name),
            new OpField(MealGroupRules.PositionField, ListPosition.Format(i + 1)),
        ])),
    ];

    /// <summary>Writes every seed this database has never held. The caller holds the write transaction.</summary>
    /// <returns>The newest seq written, or null when nothing was.</returns>
    public static async Task<long?> WriteMissingAsync(MealPlannerDbContext db, CancellationToken ct = default)
    {
        string chain = await db.Ops.OrderByDescending(o => o.Seq).Select(o => o.Chain).FirstOrDefaultAsync(ct)
                    ?? LogChain.Empty;
        DateTime now    = DateTime.UtcNow;
        long?    latest = null;

        foreach (Seed seed in All)
        {
            if (await db.Ops.AnyAsync(o => o.Entity == seed.Type.Entity && o.EntityId == seed.Id, ct))
                continue;

            if (seed.Type.Problem(OpKind.Create, seed.Id, seed.Fields) is { } problem)
                throw new InvalidOperationException($"Seed {seed.Type.Entity} {seed.Id} is invalid: {problem}");

            DbOp row = await SyncService.AcceptAsync(db, chain, Guid.CreateVersion7(), seed.Type, seed.Id, OpKind.Create, Clock, seed.Fields, now, ct);
            chain  = row.Chain;
            latest = row.Seq;
        }

        return latest;
    }
}

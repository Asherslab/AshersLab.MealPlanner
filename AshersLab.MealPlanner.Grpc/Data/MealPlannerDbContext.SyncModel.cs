using AshersLab.MealPlanner.Grpc.Data.Models.Sync;
using AshersLab.MealPlanner.Shared.Data.Categories;
using AshersLab.MealPlanner.Shared.Data.Ingredients;
using AshersLab.MealPlanner.Shared.Data.Prices;
using AshersLab.MealPlanner.Shared.Data.Products;
using AshersLab.MealPlanner.Shared.Data.Stores;
using AshersLab.MealPlanner.Shared.Data.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Grpc.Data;

public partial class MealPlannerDbContext
{
    /// <summary>Every accepted op, by seq. See <see cref="DbOp"/>.</summary>
    public DbSet<DbOp> Ops => Set<DbOp>();

    public DbSet<DbSyncEpoch> SyncEpochs => Set<DbSyncEpoch>();

    /// <summary>Materialised from the op log - what server-side reads use. Written only by <see cref="OpApplier"/>.</summary>
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();

    public DbSet<Store> Stores => Set<Store>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<PriceObservation> PriceObservations => Set<PriceObservation>();

    public DbSet<FieldClock> FieldClocks => Set<FieldClock>();

    private static void BuildSyncModel(ModelBuilder modelBuilder)
    {
        // The synced tables are shared with the browser's database, so they are defined once, there.
        SyncedEntities.BuildModel(modelBuilder);

        modelBuilder.Entity<DbOp>(op =>
        {
            op.ToTable("Op");
            op.HasKey(o => o.Seq);
            // INTEGER PRIMARY KEY: SQLite's rowid, ascending in commit order (see DbOp.Seq).
            op.Property(o => o.Seq).ValueGeneratedOnAdd();
            op.HasIndex(o => o.OpId).IsUnique();
        });

        modelBuilder.Entity<DbSyncEpoch>(epoch =>
        {
            epoch.ToTable("SyncEpoch");
            epoch.HasKey(e => e.Id);
            epoch.Property(e => e.Id).ValueGeneratedNever();
        });
    }
}

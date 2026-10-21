using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Data.Categories;
using AshersLab.MealPlanner.Shared.Data.Ingredients;
using AshersLab.MealPlanner.Shared.Data.Meals;
using AshersLab.MealPlanner.Shared.Data.Prices;
using AshersLab.MealPlanner.Shared.Data.Products;
using AshersLab.MealPlanner.Shared.Data.Stores;
using AshersLab.MealPlanner.Shared.Data.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.LocalData;

/// <summary>
/// The browser's copy of the data, in SQLite persisted to the Origin Private File System. The server's
/// <c>MealPlannerDbContext</c> is the master; this one is what the app reads and writes while offline.
/// </summary>
/// <remarks>
/// Migrations live in <c>Migrations/</c> and are applied in the browser by SqliteWasmBlazor's initializer,
/// not at startup - see <c>docs/modules/sync/browser-database.md</c>. Once the household has real data, a
/// change here needs a migration, exactly like the server's context.
/// </remarks>
public class LocalDbContext(
    DbContextOptions<LocalDbContext> options
) : DbContext(options)
{
    /// <summary>One row: this browser. See <see cref="Device"/>.</summary>
    public DbSet<Device> Devices => Set<Device>();

    /// <summary>Synced from the server. Read freely; write only through <see cref="LocalOps"/>.</summary>
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();

    public DbSet<Store> Stores => Set<Store>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<PriceObservation> PriceObservations => Set<PriceObservation>();

    public DbSet<MealGroup> MealGroups => Set<MealGroup>();

    public DbSet<Meal> Meals => Set<Meal>();

    public DbSet<MealIngredient> MealIngredients => Set<MealIngredient>();

    public DbSet<FieldClock> FieldClocks => Set<FieldClock>();

    /// <summary>This browser's edits the server has not confirmed. See <see cref="OutboxOp"/>.</summary>
    public DbSet<OutboxOp> Outbox => Set<OutboxOp>();

    public DbSet<SyncState> SyncStates => Set<SyncState>();

    private Action? _onDisposed;

    /// <summary>For <see cref="SerialLocalDbContextFactory"/>: lets the next context out once this one is gone.</summary>
    internal void ReleaseOnDispose(Action release) => _onDisposed = release;

    public override void Dispose()
    {
        base.Dispose();
        Interlocked.Exchange(ref _onDisposed, null)?.Invoke();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Interlocked.Exchange(ref _onDisposed, null)?.Invoke();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // The same synced tables as the server's database, defined once in Shared.Data.
        SyncedEntities.BuildModel(modelBuilder);

        modelBuilder.Entity<OutboxOp>(op =>
        {
            op.ToTable("Outbox");
            op.HasKey(o => o.OpId);
            op.Property(o => o.OpId).ValueGeneratedNever();
            op.HasIndex(o => o.Hlc);
        });

        modelBuilder.Entity<SyncState>(state =>
        {
            state.ToTable("SyncState");
            state.HasKey(s => s.Id);
            state.Property(s => s.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Device>(device =>
        {
            device.ToTable("Device");
            device.HasKey(d => d.Id);
            // Not database-generated: the id is minted in the browser, never by SQLite.
            device.Property(d => d.Id).ValueGeneratedNever();
        });
    }
}

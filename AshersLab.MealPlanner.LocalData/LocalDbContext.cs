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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Device>(device =>
        {
            device.ToTable("Device");
            device.HasKey(d => d.Id);
            // Not database-generated: the id is minted in the browser, never by SQLite.
            device.Property(d => d.Id).ValueGeneratedNever();
        });
    }
}

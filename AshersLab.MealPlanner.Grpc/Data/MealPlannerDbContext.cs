using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Grpc.Data;

/// <summary>
/// The server's master copy. SQLite, because it runs on a Raspberry Pi; kept to portable EF so that
/// Postgres stays a provider change rather than a rewrite.
/// </summary>
/// <remarks>
/// Split by area the GSBC way: <c>MealPlannerDbContext.&lt;Area&gt;Model.cs</c> partials, each with its own
/// <c>DbSet</c>s and a <c>Build&lt;Area&gt;Model</c> called from <see cref="OnModelCreating"/>.
/// </remarks>
public partial class MealPlannerDbContext(
    DbContextOptions<MealPlannerDbContext> options
) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        BuildSyncModel(modelBuilder);
    }
}

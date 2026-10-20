using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AshersLab.MealPlanner.LocalData;

/// <summary>
/// Lets <c>dotnet ef</c> build the context outside the browser. Migrations are generated from the model, so
/// plain SQLite stands in for the OPFS-backed provider the app uses; the file it names is only created if a
/// command actually opens it, and <c>*.db</c> is git-ignored.
/// </summary>
public class LocalDbContextFactory : IDesignTimeDbContextFactory<LocalDbContext>
{
    public LocalDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<LocalDbContext> options = new DbContextOptionsBuilder<LocalDbContext>()
            .UseSqlite("Data Source=local-design-time.db")
            .Options;

        return new LocalDbContext(options);
    }
}

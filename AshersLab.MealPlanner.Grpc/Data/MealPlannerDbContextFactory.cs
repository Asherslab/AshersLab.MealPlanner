using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AshersLab.MealPlanner.Grpc.Data;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting the host. It never connects to anything worth
/// protecting: migrations are generated from the model, and the database it names is a throwaway file
/// under <c>app-data/</c> (git-ignored), and only if a command actually opens it.
/// </summary>
public class MealPlannerDbContextFactory : IDesignTimeDbContextFactory<MealPlannerDbContext>
{
    public MealPlannerDbContext CreateDbContext(string[] args)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        DbContextOptions<MealPlannerDbContext> options = new DbContextOptionsBuilder<MealPlannerDbContext>()
            .UseSqlite(DataDirectory.ConnectionString(configuration))
            .Options;

        return new MealPlannerDbContext(options);
    }
}

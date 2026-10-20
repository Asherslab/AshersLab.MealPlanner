using System.Reflection;
using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Server;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Server;
using Microsoft.EntityFrameworkCore;
using ProtoBuf.Grpc;

namespace AshersLab.MealPlanner.Grpc.Features.Server;

public class ServerInfoService(
    MealPlannerDbContext db
) : IServerInfoService
{
    private static readonly string Version =
        typeof(ServerInfoService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public async Task<ServerInfo> Get(CallContext context = default)
    {
        // Read from __EFMigrationsHistory, not from the assembly's list of migrations: the point is to
        // prove this call reached the database, so the answer has to come out of it.
        IEnumerable<string> applied = await db.Database.GetAppliedMigrationsAsync(context.CancellationToken);

        return new ServerInfo
        {
            Version = Version,
            ServerTimeUtc = DateTime.UtcNow,
            LatestMigration = applied.LastOrDefault()
        };
    }
}

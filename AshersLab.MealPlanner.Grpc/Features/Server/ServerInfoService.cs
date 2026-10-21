using System.Reflection;
using System.Security.Claims;
using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Features.Ingress;
using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Server;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Server;
using Grpc.Core;
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

        // Set by UseHomeAssistantIngress from the Supervisor's X-Remote-User-* headers, or by UseTrustedHeaderAuth from a
        // sign-in proxy's - each only on a connection from that proxy.
        ClaimsPrincipal? user = context.ServerCallContext?.GetHttpContext().User;

        return new ServerInfo
        {
            Version = Version,
            ServerTimeUtc = DateTime.UtcNow,
            LatestMigration = applied.LastOrDefault(),
            UserName = user?.FindFirst(ClaimTypes.Name)?.Value,
            UserDisplayName = user?.FindFirst(IngressRequest.DisplayNameClaim)?.Value
        };
    }
}

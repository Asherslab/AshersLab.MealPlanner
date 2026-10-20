using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Server;

namespace AshersLab.MealPlanner.Shared.Contracts.Services.Features.Server;

[Service("gRPC/AshersLab.MealPlanner.ServerInfo")]
public interface IServerInfoService
{
    Task<ServerInfo> Get(CallContext context = default);
}

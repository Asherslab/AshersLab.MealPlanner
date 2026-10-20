using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Server;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Server;

namespace AshersLab.MealPlanner.Tests.Grpc;

public class ServerInfoServiceTests
{
    [Fact]
    public async Task Answers_over_grpc_web_from_a_migrated_database()
    {
        await using MealPlannerHost host = new();
        IServerInfoService service = host.CreateGrpcWebClient<IServerInfoService>();

        ServerInfo info = await service.Get(TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.NotNull(info.LatestMigration);
        Assert.EndsWith("_CreateDatabase", info.LatestMigration);
        Assert.InRange(info.ServerTimeUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Serves_the_wasm_app_shell_for_deep_links()
    {
        await using MealPlannerHost host = new();
        HttpClient client = host.CreateClient();

        string html = await client.GetStringAsync("/some/deep/link", TestContext.Current.CancellationToken);

        Assert.Contains("_framework/blazor.webassembly.js", html);
    }

    // No test for the _framework/ files themselves, deliberately. The one bug found there (see the
    // UseRouting comment in Program.cs) does not reproduce in this host - it has no static-assets
    // manifest - so such a test passed with the bug present and proved nothing.
}

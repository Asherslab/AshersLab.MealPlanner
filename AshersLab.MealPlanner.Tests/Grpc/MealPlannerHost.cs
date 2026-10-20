using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using ProtoBuf.Grpc.Client;

namespace AshersLab.MealPlanner.Tests.Grpc;

/// <summary>
/// The real gRPC host, in process, on a throwaway SQLite file. Startup migrates it exactly as production
/// does, so a test that gets an answer has proven the migration ran.
/// </summary>
public sealed class MealPlannerHost : WebApplicationFactory<Program>
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "mealplanner-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("DataDirectory", _dataDirectory);
    }

    /// <summary>
    /// A client that speaks gRPC-Web, as the browser does - not plain gRPC. A service mapped without
    /// <c>UseGrpcWeb</c> would pass a plain-gRPC test and fail in every browser.
    /// </summary>
    public T CreateGrpcWebClient<T>() where T : class
    {
        GrpcChannel channel = GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, Server.CreateHandler())
        });

        return channel.CreateGrpcService<T>();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // SQLite pools connections, which keeps the file open on some platforms; clear before deleting.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(_dataDirectory))
            Directory.Delete(_dataDirectory, recursive: true);
    }
}

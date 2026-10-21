using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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

    public MealPlannerHost()
    {
    }

    /// <summary>
    /// How many ops a new database's log starts with: the catalogue seeds, written at startup. A browser's first op
    /// is seq <c>SeedOps + 1</c>.
    /// </summary>
    public static int SeedOps => CatalogueSeed.All.Count;

    /// <summary>
    /// A host started on a copy of a backup taken with <see cref="BackupAsync"/> - the Pi after Home Assistant
    /// restores the add-on's <c>/data</c>.
    /// </summary>
    public static MealPlannerHost RestoredFrom(string backupFile)
    {
        MealPlannerHost host = new();
        Directory.CreateDirectory(host._dataDirectory);
        File.Copy(backupFile, Path.Combine(host._dataDirectory, DataDirectory.DatabaseFileName));
        return host;
    }

    /// <summary>A consistent copy of the server's database as it is now, as a backup would take it.</summary>
    public async Task<string> BackupAsync()
    {
        string file = Path.Combine(Path.GetTempPath(), "mealplanner-tests", $"backup-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        using IServiceScope scope = Services.CreateScope();
        MealPlannerDbContext db = scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>();
        await db.Database.ExecuteSqlRawAsync("VACUUM INTO '" + file + "'");
        return file;
    }

    /// <summary>
    /// The peer address a request claims, for the ingress tests. The test server has no socket, so it reports
    /// no remote address at all unless a request sets one with this header.
    /// </summary>
    public const string RemoteAddressHeader = "X-Test-Remote-Address";

    private readonly Dictionary<string, string?> _settings = new();

    /// <summary>As the add-on: only the Supervisor's address may connect, and its headers are believed.</summary>
    public MealPlannerHost BehindIngress(string trustedProxy = "172.30.32.2")
    {
        _settings["Ingress:TrustedProxy"] = trustedProxy;
        return this;
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("DataDirectory", _dataDirectory);
        // Every test starts a host; at Information each one prints its migrations and lifetime, burying failures.
        builder.UseSetting("Logging:LogLevel:Default", "Warning");

        foreach ((string key, string? value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, RemoteAddressFromHeader>());
    }

    /// <summary>
    /// A client that speaks gRPC-Web, as the browser does - not plain gRPC. A service mapped without
    /// <c>UseGrpcWeb</c> would pass a plain-gRPC test and fail in every browser.
    /// </summary>
    /// <param name="headers">Sent on every call, as a proxy in front would add them.</param>
    public T CreateGrpcWebClient<T>(IReadOnlyDictionary<string, string>? headers = null) where T : class
    {
        HttpMessageHandler inner = headers is null ? Server.CreateHandler() : new AddHeaders(headers, Server.CreateHandler());

        GrpcChannel channel = GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, inner)
        });

        return channel.CreateGrpcService<T>();
    }

    private sealed class AddHeaders(IReadOnlyDictionary<string, string> headers, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            foreach ((string name, string value) in headers)
                request.Headers.TryAddWithoutValidation(name, value);

            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class RemoteAddressFromHeader : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers[RemoteAddressHeader].ToString() is { Length: > 0 } address)
                    context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(address);

                return nextMiddleware(context);
            });

            next(app);
        };
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

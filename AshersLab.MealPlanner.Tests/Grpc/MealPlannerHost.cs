using Grpc.Net.Client;
using Grpc.Net.Client.Web;
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

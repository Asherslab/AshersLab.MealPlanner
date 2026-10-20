using System.Net;
using AshersLab.MealPlanner.Grpc.Features.Ingress;
using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Server;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Server;
using Grpc.Core;

namespace AshersLab.MealPlanner.Tests.Grpc;

/// <summary>
/// The host behind Home Assistant ingress: who may connect, the base path the shell is served with, and the
/// user it takes from the Supervisor. The real proxy is proven by .claude/tools/ingress-sim and on the Pi.
/// </summary>
public class IngressTests
{
    private const string Supervisor = "172.30.32.2";
    private const string Prefix     = "/api/hassio_ingress/Zx9-tok_en";

    [Theory]
    [InlineData(Prefix, Prefix + "/")]
    [InlineData(Prefix + "/", Prefix + "/")]
    [InlineData("/", "/")]
    public void Takes_the_ingress_path_as_a_base_href(string header, string expected)
    {
        Assert.True(IngressPath.TryParse(header, out string baseHref));
        Assert.Equal(expected, baseHref);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("api/hassio_ingress/abc")]                 // relative
    [InlineData("//evil.example/x")]                       // scheme-relative: another host
    [InlineData("/api//abc")]
    [InlineData("/api/../admin")]
    [InlineData("/api/hassio_ingress/a\"/><script>")]      // written into HTML
    [InlineData("/api/hassio_ingress/a b")]
    public void Refuses_a_malformed_ingress_path_and_falls_back_to_the_root(string? header)
    {
        Assert.False(IngressPath.TryParse(header, out string baseHref));
        Assert.Equal("/", baseHref);
    }

    [Fact]
    public void Rewrites_only_the_base_href()
    {
        const string html = """<head><base href="/"/><link href="css/app.css"/></head>""";

        Assert.Equal("""<head><base href="/api/x/"/><link href="css/app.css"/></head>""", AppShell.Rewrite(html, "/api/x/"));
        Assert.Equal(html, AppShell.Rewrite(html, "/"));
    }

    [Fact]
    public void Refuses_a_shell_that_no_longer_has_the_marker()
    {
        Assert.Throws<InvalidOperationException>(() => AppShell.Rewrite("""<base href="./">""", "/api/x/"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/ingredients")]
    [InlineData("/index.html")]
    public async Task Serves_the_shell_under_the_ingress_path(string path)
    {
        await using MealPlannerHost host = new MealPlannerHost().BehindIngress();

        HttpResponseMessage response = await Send(host, path, Supervisor, (IngressPath.Header, Prefix));
        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"""<base href="{Prefix}/"/>""", html);
        Assert.Contains("_framework/blazor.webassembly.js", html);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task The_real_index_html_still_carries_the_marker()
    {
        // Serving it outside ingress leaves it untouched, so this is the file as built.
        await using MealPlannerHost host = new();

        string html = await host.CreateClient().GetStringAsync("/", TestContext.Current.CancellationToken);

        Assert.Contains(AppShell.BaseHrefMarker, html);
    }

    [Theory]
    [InlineData("172.30.32.1")]     // the hassio network's gateway: a port published on the host
    [InlineData("172.30.33.5")]     // another add-on
    [InlineData(null)]
    public async Task Refuses_anything_but_the_supervisor(string? from)
    {
        await using MealPlannerHost host = new MealPlannerHost().BehindIngress();

        HttpResponseMessage shell = await Send(host, "/", from, (IngressRequest.UserIdHeader, "forged"));
        Assert.Equal(HttpStatusCode.Forbidden, shell.StatusCode);

        Dictionary<string, string> headers = new() { [IngressRequest.UserNameHeader] = "forged" };
        if (from is not null)
            headers[MealPlannerHost.RemoteAddressHeader] = from;

        RpcException refused = await Assert.ThrowsAsync<RpcException>(async () =>
            await host.CreateGrpcWebClient<IServerInfoService>(headers).Get(TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.PermissionDenied, refused.StatusCode);
    }

    [Fact]
    public async Task Accepts_the_supervisor_on_a_dual_stack_socket()
    {
        await using MealPlannerHost host = new MealPlannerHost().BehindIngress();

        HttpResponseMessage response = await Send(host, "/", "::ffff:" + Supervisor);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Knows_the_home_assistant_user_over_grpc_web()
    {
        await using MealPlannerHost host = new MealPlannerHost().BehindIngress();
        IServerInfoService service = host.CreateGrpcWebClient<IServerInfoService>(new Dictionary<string, string>
        {
            [MealPlannerHost.RemoteAddressHeader]  = Supervisor,
            [IngressRequest.UserIdHeader]          = "8c2f0e5a",
            [IngressRequest.UserNameHeader]        = "planner",
            [IngressRequest.UserDisplayNameHeader] = "The Planner"
        });

        ServerInfo info = await service.Get(TestContext.Current.CancellationToken);

        Assert.Equal("planner", info.UserName);
        Assert.Equal("The Planner", info.UserDisplayName);
    }

    [Fact]
    public async Task Believes_no_header_when_not_behind_ingress()
    {
        // Local development and the test host: nothing is refused, and nothing a request says about itself is
        // taken as fact.
        await using MealPlannerHost host = new();
        IServerInfoService service = host.CreateGrpcWebClient<IServerInfoService>(new Dictionary<string, string>
        {
            [IngressRequest.UserIdHeader]   = "8c2f0e5a",
            [IngressRequest.UserNameHeader] = "planner"
        });

        ServerInfo info = await service.Get(TestContext.Current.CancellationToken);
        string html = await (await Send(host, "/", null, (IngressPath.Header, Prefix)))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Null(info.UserName);
        Assert.Contains(AppShell.BaseHrefMarker, html);
    }

    private static Task<HttpResponseMessage> Send(MealPlannerHost host, string path, string? from, params (string Name, string Value)[] headers)
    {
        HttpRequestMessage request = new(HttpMethod.Get, path);
        if (from is not null)
            request.Headers.Add(MealPlannerHost.RemoteAddressHeader, from);
        foreach ((string name, string value) in headers)
            request.Headers.Add(name, value);

        return host.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }
}

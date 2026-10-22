using System.Net;
using AshersLab.MealPlanner.Grpc.Features.Auth;
using AshersLab.MealPlanner.Grpc.Features.Ingress;
using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Server;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Server;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace AshersLab.MealPlanner.Tests.Grpc;

/// <summary>
/// The image outside Home Assistant: refusing to start with no authentication, a sign-in proxy's user header believed only
/// from the proxy, the explicit opt-out, and the probes Kubernetes reaches without signing in. The real proxy is proven
/// by .claude/tools/standalone-sim and the chart in a kind cluster.
/// </summary>
public class AuthTests
{
    private const string Proxy = "10.42.0.7";
    private const string ProxyNetwork = "10.42.0.0/16";

    private static MealPlannerHost BehindProxy(string? displayNameHeader = null)
    {
        MealPlannerHost host = new MealPlannerHost().InProduction()
            .With("Auth:Mode", "TrustedHeader").With("Auth:TrustedProxies", $"192.168.1.5, {ProxyNetwork}");
        return displayNameHeader is null ? host : host.With("Auth:DisplayNameHeader", displayNameHeader);
    }

    private static Task<ServerInfo> Ask(MealPlannerHost host, string? from, params (string Name, string Value)[] headers)
    {
        Dictionary<string, string> sent = headers.ToDictionary(h => h.Name, h => h.Value);
        if (from is not null)
            sent[MealPlannerHost.RemoteAddressHeader] = from;
        return host.CreateGrpcWebClient<IServerInfoService>(sent).Get(TestContext.Current.CancellationToken);
    }

    private static string StartupFailure(MealPlannerHost host)
    {
        Exception? cause = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        while (cause is not null and not OptionsValidationException)
            cause = cause.InnerException;
        return Assert.IsType<OptionsValidationException>(cause).Message;
    }

    [Theory]
    // Home Assistant: the add-on sets only the Supervisor's address.
    [InlineData(null, "172.30.32.2", false, AuthMode.HomeAssistant)]
    // Local development and the test host: nothing set, nothing checked.
    [InlineData(null, null, true, AuthMode.None)]
    [InlineData("None", null, false, AuthMode.None)]
    [InlineData("TrustedHeader", null, false, AuthMode.TrustedHeader)]
    [InlineData("TrustedHeader", null, true, AuthMode.TrustedHeader)]
    public void The_mode_is_what_was_configured(string? mode, string? supervisor, bool development, AuthMode expected)
    {
        AuthOptions options = new() { Mode = mode, TrustedProxies = ProxyNetwork };
        Assert.Equal(expected, options.Resolve(new IngressOptions { TrustedProxy = supervisor }, development, out string? problem));
        Assert.Null(problem);
    }

    [Theory]
    [InlineData(null, null, ProxyNetwork, "No authentication is configured")]
    [InlineData("", null, ProxyNetwork, "No authentication is configured")]
    [InlineData("TrustedHeader", "172.30.32.2", ProxyNetwork, "cannot also be set")]
    [InlineData("None", "172.30.32.2", ProxyNetwork, "cannot also be set")]
    [InlineData("Password", null, ProxyNetwork, "is not Accounts, TrustedHeader or None")]
    [InlineData("TrustedHeader", null, "", "TrustedProxies is empty")]
    [InlineData("TrustedHeader", null, "10.42/16", "'10.42/16' is not an address")]
    public void A_setting_that_cannot_work_is_refused_and_says_what_to_set(string? mode, string? supervisor, string proxies, string expected)
    {
        AuthOptions options = new() { Mode = mode, TrustedProxies = proxies };
        Assert.Null(options.Resolve(new IngressOptions { TrustedProxy = supervisor }, development: false, out string? problem));
        Assert.Contains(expected, problem);
    }

    [Fact]
    public void A_user_header_must_be_named()
    {
        AuthOptions options = new() { Mode = "TrustedHeader", TrustedProxies = ProxyNetwork, UserHeader = " " };
        Assert.Null(options.Resolve(new IngressOptions(), false, out string? problem));
        Assert.Contains("UserHeader is empty", problem);
    }

    [Theory]
    [InlineData("10.42.0.0/16", "10.42.0.0/16")]
    [InlineData("192.168.1.5", "192.168.1.5/32")]
    [InlineData(" 10.0.0.0/8 ,fd00::/8 ", "10.0.0.0/8|fd00::/8")]
    [InlineData("fd00::7", "fd00::7/128")]
    public void Proxies_are_addresses_or_networks(string configured, string expected) =>
        Assert.Equal(expected, string.Join('|', AuthOptions.Networks(configured, out _)!.Select(n => n.ToString())));

    [Theory]
    [InlineData("10.42/16")]          // IPAddress.TryParse: 10.0.0.42
    [InlineData("10.42.1.0/16")]      // bits past the prefix: meant 10.42.0.0/16, or 10.42.1.0/24?
    [InlineData("10.42.0.0/33")]
    [InlineData("10.42.0.0/-1")]
    [InlineData("10.42.0.0/16/2")]
    [InlineData("0.0.0.0/0")]         // everyone: Auth:Mode=None without its warning
    [InlineData("::/0")]
    [InlineData("proxy.local")]
    [InlineData(",")]
    public void Anything_else_is_not_a_proxy(string configured)
    {
        Assert.Null(AuthOptions.Networks(configured, out string? bad));
        Assert.NotNull(bad);
    }

    [Fact]
    public async Task The_image_with_no_authentication_configured_will_not_start()
    {
        await using MealPlannerHost host = new MealPlannerHost().InProduction();
        Assert.Contains("No authentication is configured", StartupFailure(host));
    }

    [Fact]
    public async Task The_image_on_a_bad_setting_will_not_start()
    {
        await using MealPlannerHost host = new MealPlannerHost().InProduction().With("Auth:Mode", "TrustedHeader").With("Auth:TrustedProxies", "10.42/16");
        Assert.Contains("10.42/16", StartupFailure(host));
    }

    [Fact]
    public async Task The_add_on_starts_on_the_supervisors_address_alone()
    {
        await using MealPlannerHost host = new MealPlannerHost().InProduction().BehindIngress();
        ServerInfo info = await Ask(host, "172.30.32.2", (IngressRequest.UserIdHeader, "8c2f0e5a"), (IngressRequest.UserNameHeader, "planner"));
        Assert.Equal("planner", info.UserName);
    }

    [Fact]
    public async Task Told_none_it_serves_anyone_and_believes_no_header()
    {
        await using MealPlannerHost host = new MealPlannerHost().InProduction().With("Auth:Mode", "None");
        ServerInfo info = await Ask(host, "203.0.113.9", ("X-Forwarded-User", "forged"));
        Assert.Null(info.UserName);
    }

    [Fact]
    public async Task Knows_the_user_the_proxy_names()
    {
        await using MealPlannerHost host = BehindProxy("X-Forwarded-Preferred-Username");
        ServerInfo info = await Ask(host, Proxy, ("X-Forwarded-User", "planner@example.com"), ("X-Forwarded-Preferred-Username", "The Planner"));
        Assert.Equal(("planner@example.com", "The Planner"), (info.UserName, info.UserDisplayName));

        // A single address in the list, and an IPv4 peer on a dual-stack socket.
        Assert.Equal("planner", (await Ask(host, "::ffff:192.168.1.5", ("X-Forwarded-User", "planner"))).UserName);
    }

    [Theory]
    [InlineData("10.43.0.7")]        // another pod network: not the proxy
    [InlineData("192.168.1.6")]
    [InlineData(null)]
    public async Task Refuses_anything_but_the_proxy_whatever_it_claims(string? from)
    {
        await using MealPlannerHost host = BehindProxy();
        RpcException refused = await Assert.ThrowsAsync<RpcException>(() => Ask(host, from, ("X-Forwarded-User", "forged")));
        Assert.Equal(StatusCode.PermissionDenied, refused.StatusCode);

        HttpRequestMessage request = new(HttpMethod.Get, "/");
        if (from is not null)
            request.Headers.Add(MealPlannerHost.RemoteAddressHeader, from);
        HttpResponseMessage shell = await host.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, shell.StatusCode);
    }

    [Fact]
    public async Task The_proxy_letting_through_nobody_is_not_signed_in()
    {
        await using MealPlannerHost host = BehindProxy();
        RpcException missing = await Assert.ThrowsAsync<RpcException>(() => Ask(host, Proxy));
        Assert.Equal(StatusCode.Unauthenticated, missing.StatusCode);
        RpcException empty = await Assert.ThrowsAsync<RpcException>(() => Ask(host, Proxy, ("X-Forwarded-User", " ")));
        Assert.Equal(StatusCode.Unauthenticated, empty.StatusCode);
    }

    [Theory]
    [InlineData("X-Forwarded-User")]
    [InlineData("X-Name")]
    public void Believes_nobody_when_a_user_header_arrives_twice(string repeated)
    {
        // A proxy that passed the browser's own copy on beside its own: neither can be trusted.
        HeaderDictionary headers = new() { ["X-Forwarded-User"] = "planner", ["X-Name"] = "The Planner" };
        Assert.NotNull(TrustedHeaderRequest.UserFrom(headers, "X-Forwarded-User", "X-Name"));
        headers[repeated] = new StringValues(["forged", headers[repeated].ToString()]);
        Assert.Null(TrustedHeaderRequest.UserFrom(headers, "X-Forwarded-User", "X-Name"));
    }

    [Theory]
    [InlineData(HealthProbes.Live)]
    [InlineData(HealthProbes.Ready)]
    public async Task Kubernetes_probes_answer_without_signing_in(string path)
    {
        // The kubelet probes the pod's own address, from the node: not the proxy, no user.
        await using MealPlannerHost host = BehindProxy();
        HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Add(MealPlannerHost.RemoteAddressHeader, "10.0.0.1");
        HttpResponseMessage response = await host.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Only_the_probes_are_open()
    {
        await using MealPlannerHost host = BehindProxy();
        HttpRequestMessage request = new(HttpMethod.Get, "/healthz/../index.html");
        request.Headers.Add(MealPlannerHost.RemoteAddressHeader, "10.0.0.1");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.CreateClient().SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);

        HttpRequestMessage below = new(HttpMethod.Get, "/healthz/ready/x");
        below.Headers.Add(MealPlannerHost.RemoteAddressHeader, "10.0.0.1");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.CreateClient().SendAsync(below, TestContext.Current.CancellationToken)).StatusCode);
    }
}

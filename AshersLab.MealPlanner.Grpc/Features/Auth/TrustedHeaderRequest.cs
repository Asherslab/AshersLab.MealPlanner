using System.Net;
using System.Security.Claims;
using AshersLab.MealPlanner.Grpc.Features.Ingress;
using Microsoft.Extensions.Options;

namespace AshersLab.MealPlanner.Grpc.Features.Auth;

/// <summary>
/// The standalone image behind a reverse proxy that signs people in (oauth2-proxy, Authentik, an nginx <c>auth_request</c>)
/// and names the user in a header: who may connect, and who they are.
/// </summary>
/// <remarks>
/// The same rule as Home Assistant's (<see cref="IngressRequest"/>): <b>a header is believed only on a connection that
/// could only have come from the proxy</b>. A request from any other address is a 403 before it reaches a file or a
/// service; one from the proxy that names nobody is a 401 - the proxy let through someone it had not signed in.
/// <para>
/// The proxy must <b>replace</b> the browser's own copy of the user header, not pass it on: everything here rests on that.
/// oauth2-proxy and Authentik's outposts set it from the session; an nginx <c>auth_request</c> needs
/// <c>proxy_set_header</c>, not <c>add_header</c>. A header that still arrives twice is believed as nobody.
/// </para>
/// </remarks>
public static class TrustedHeaderRequest
{
    public const string AuthenticationType = "TrustedHeader";

    public static IApplicationBuilder UseTrustedHeaderAuth(this IApplicationBuilder app)
    {
        AuthOptions options = app.ApplicationServices.GetRequiredService<IOptions<AuthOptions>>().Value;
        IngressOptions ingress = app.ApplicationServices.GetRequiredService<IOptions<IngressOptions>>().Value;
        bool development = app.ApplicationServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
        AuthMode? mode = options.Resolve(ingress, development, out _);

        if (mode == AuthMode.None && !development)
            app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(TrustedHeaderRequest))
                .LogWarning("Auth:Mode is None: anyone who can reach this port can read and change everything.");
        if (mode != AuthMode.TrustedHeader)
            return app;

        IReadOnlyList<IPNetwork> proxies = AuthOptions.Networks(options.TrustedProxies, out _)!;
        return app.Use(async (context, next) =>
        {
            IPAddress? remote = context.Connection.RemoteIpAddress;
            if (remote is null || !proxies.Any(p => p.Contains(IngressRequest.Normalise(remote))))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Only reachable through the sign-in proxy.", context.RequestAborted);
                return;
            }

            if (UserFrom(context.Request.Headers, options.UserHeader, options.DisplayNameHeader) is not { } user)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Not signed in.", context.RequestAborted);
                return;
            }

            context.User = user;
            await next(context);
        });
    }

    /// <summary>
    /// The user the proxy named, as the same claims Home Assistant's gives (Name, and <see cref="IngressRequest.DisplayNameClaim"/>),
    /// so the rest of the app cannot tell the two apart. Null when the header is missing, empty, or arrived more than once.
    /// </summary>
    public static ClaimsPrincipal? UserFrom(IHeaderDictionary headers, string userHeader, string? displayNameHeader)
    {
        if (headers[userHeader].Count != 1 || (displayNameHeader is { Length: > 0 } && headers[displayNameHeader].Count > 1))
            return null;

        string user = headers[userHeader].ToString().Trim();
        if (user.Length == 0)
            return null;

        List<Claim> claims = [new(ClaimTypes.NameIdentifier, user), new(ClaimTypes.Name, user)];
        if (displayNameHeader is { Length: > 0 } && headers[displayNameHeader].ToString().Trim() is { Length: > 0 } displayName)
            claims.Add(new Claim(IngressRequest.DisplayNameClaim, displayName));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType));
    }
}

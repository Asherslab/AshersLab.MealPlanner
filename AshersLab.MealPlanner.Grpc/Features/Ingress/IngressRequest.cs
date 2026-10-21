using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace AshersLab.MealPlanner.Grpc.Features.Ingress;

/// <summary>
/// Everything this host takes from the Home Assistant Supervisor: who may connect, the base path, and the user.
/// </summary>
/// <remarks>
/// Ingress only forwards users logged in to Home Assistant, and only the Supervisor (<c>172.30.32.2</c> on the
/// hassio network) can reach an add-on's ingress port. So the check here is the address, nothing else: a
/// request from it carries a real HA user in <c>X-Remote-User-*</c>; a request from anywhere else - another
/// add-on, a port someone published by hand - is refused before it reaches a page or a gRPC service, because
/// it could carry any headers it liked.
/// <para>
/// First in the pipeline, so the refusal covers <c>_framework/</c> and the gRPC services alike.
/// </para>
/// </remarks>
public static class IngressRequest
{
    public const string UserIdHeader          = "X-Remote-User-Id";
    public const string UserNameHeader        = "X-Remote-User-Name";
    public const string UserDisplayNameHeader = "X-Remote-User-Display-Name";

    /// <summary>The claim carrying the HA display name ("Sam Smith"), next to the login name in Name.</summary>
    public const string DisplayNameClaim = "display_name";

    public const string AuthenticationType = "HomeAssistant";

    private const string BaseHrefKey = "Ingress.BaseHref";

    public static IApplicationBuilder UseHomeAssistantIngress(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            IPAddress? proxy = context.RequestServices.GetRequiredService<IOptions<IngressOptions>>().Value.TrustedProxyAddress;
            if (proxy is null)
            {
                await next(context);
                return;
            }

            IPAddress? remote = context.Connection.RemoteIpAddress;
            if (remote is null || !Normalise(remote).Equals(Normalise(proxy)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Only reachable through Home Assistant.", context.RequestAborted);
                return;
            }

            if (IngressPath.TryParse(context.Request.Headers[IngressPath.Header], out string baseHref))
                context.Items[BaseHrefKey] = baseHref;

            if (UserFrom(context.Request.Headers) is { } user)
                context.User = user;

            await next(context);
        });

    /// <summary>The base the app shell is served with: the ingress prefix, or <c>/</c> outside ingress.</summary>
    public static string BaseHref(HttpContext context) =>
        context.Items[BaseHrefKey] as string ?? "/";

    /// <summary>
    /// Null when the Supervisor sent no user id - a request it forwarded without one is anonymous - or when any of
    /// the user headers arrived more than once.
    /// </summary>
    /// <remarks>
    /// The Supervisor drops a browser's own <c>X-Remote-User-*</c> headers before setting its own
    /// (<c>supervisor/api/ingress.py</c>, <c>_REQUEST_HEADERS_FILTER</c>). A second value would mean a proxy in
    /// between that does not - and ASP.NET would join the two with a comma, the browser's first - so it is
    /// believed as nobody rather than as either.
    /// </remarks>
    public static ClaimsPrincipal? UserFrom(IHeaderDictionary headers)
    {
        if (headers[UserIdHeader].Count > 1 || headers[UserNameHeader].Count > 1 || headers[UserDisplayNameHeader].Count > 1)
            return null;

        string? id = headers[UserIdHeader];
        if (string.IsNullOrWhiteSpace(id))
            return null;

        List<Claim> claims = [new(ClaimTypes.NameIdentifier, id)];

        if (headers[UserNameHeader].ToString() is { Length: > 0 } name)
            claims.Add(new Claim(ClaimTypes.Name, name));

        if (headers[UserDisplayNameHeader].ToString() is { Length: > 0 } displayName)
            claims.Add(new Claim(DisplayNameClaim, displayName));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType));
    }

    // Kestrel reports an IPv4 peer on a dual-stack socket as ::ffff:172.30.32.2.
    private static IPAddress Normalise(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}

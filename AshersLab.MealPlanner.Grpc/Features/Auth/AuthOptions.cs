using System.Net;
using AshersLab.MealPlanner.Grpc.Features.Ingress;
using Microsoft.Extensions.Options;

namespace AshersLab.MealPlanner.Grpc.Features.Auth;

/// <summary>Who the host believes a request comes from. One per host, from configuration (<see cref="AuthOptions"/>).</summary>
public enum AuthMode
{
    /// <summary>Nobody is checked: local development, the test host, or an owner who said so (<c>Auth:Mode=None</c>).</summary>
    None,

    /// <summary>The Home Assistant add-on: <c>Ingress:TrustedProxy</c> is the Supervisor (<see cref="IngressRequest"/>).</summary>
    HomeAssistant,

    /// <summary>Standalone behind a reverse proxy that signs people in and names them in a header (<see cref="TrustedHeaderRequest"/>).</summary>
    TrustedHeader,

    /// <summary>
    /// The app signs people in itself, with passkeys (<c>Features/Accounts</c>): the Home Assistant add-on behind a proxy
    /// that only does HTTPS. Nobody's headers are believed.
    /// </summary>
    Accounts,
}

/// <summary>
/// How the standalone image (Docker, Kubernetes) knows who is asking - docs/modules/infrastructure/deployment.md.
/// </summary>
/// <remarks>
/// <b>Outside Development, a host with no authentication configured does not start.</b> The image is the same one Home
/// Assistant runs, and there the Supervisor is the gate; run anywhere else with nothing set it would serve the household's
/// data to whoever found the port. So it has to be told one of: the Supervisor's address (<c>Ingress__TrustedProxy</c>, the
/// add-on's <c>config.yaml</c> sets it), a proxy that signs people in (<c>Auth__Mode=TrustedHeader</c>), or that nobody is
/// checked (<c>Auth__Mode=None</c>) - a deliberate choice, for an owner whose network already is the gate.
/// </remarks>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary><c>Accounts</c>, <c>TrustedHeader</c> or <c>None</c>; unset is Home Assistant when <c>Ingress:TrustedProxy</c> is set.</summary>
    public string? Mode { get; set; }

    /// <summary>
    /// <c>Accounts</c>: the address people open, <c>https://meals.example.com</c>. Passkeys are bound to its host, and a
    /// passkey ceremony from any other origin is refused (<see cref="Accounts.AccountRules.PublicUrl"/>).
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary><c>Accounts</c>: print a new setup link at start even though members exist - for when nobody can sign in.</summary>
    public bool ResetAccess { get; set; }

    /// <summary>
    /// The proxy's addresses, comma-separated: <c>10.42.0.0/16, 192.168.1.5</c>. In Kubernetes the ingress controller's pod
    /// address changes, so this is usually the pod network - and a NetworkPolicy keeps every other pod off the port.
    /// </summary>
    public string? TrustedProxies { get; set; }

    /// <summary>The header the proxy names the signed-in user in: oauth2-proxy's by default; Authentik's is <c>X-authentik-username</c>.</summary>
    public string UserHeader { get; set; } = "X-Forwarded-User";

    /// <summary>Optional: the header with the name to show (<c>X-authentik-name</c>). Unset, the user's login is shown.</summary>
    public string? DisplayNameHeader { get; set; }

    /// <summary>The mode, or null with the reason in <paramref name="problem"/> - which stops the host at startup.</summary>
    public AuthMode? Resolve(IngressOptions ingress, bool development, out string? problem)
    {
        bool homeAssistant = ingress.TrustedProxy is { Length: > 0 };
        problem = Mode switch
        {
            null or "" when homeAssistant => null,
            null or "" when development => null,
            null or "" => "No authentication is configured, so the app will not serve anyone. For the app's own passkey sign-in, set " +
                          "Auth__Mode=Accounts and Auth__PublicUrl; behind a proxy that signs people in, set Auth__Mode=TrustedHeader " +
                          "and Auth__TrustedProxies to its addresses; to serve without any, set Auth__Mode=None.",
            _ when homeAssistant => $"{IngressOptions.Section}:TrustedProxy is the Home Assistant add-on's; {Section}:Mode cannot also be set.",
            "None" => null,
            "TrustedHeader" => TrustedHeaderProblem(),
            "Accounts" => Accounts.AccountRules.PublicUrl(PublicUrl, out string? bad) is null ? $"{Section}:PublicUrl {bad}." : null,
            _ => $"{Section}:Mode '{Mode}' is not Accounts, TrustedHeader or None.",
        };
        if (problem is not null)
            return null;
        return homeAssistant ? AuthMode.HomeAssistant
            : Mode switch { "TrustedHeader" => AuthMode.TrustedHeader, "Accounts" => AuthMode.Accounts, _ => AuthMode.None };
    }

    private string? TrustedHeaderProblem()
    {
        if (string.IsNullOrWhiteSpace(UserHeader))
            return $"{Section}:UserHeader is empty: the proxy's user header has to be named.";
        if (string.IsNullOrWhiteSpace(TrustedProxies))
            return $"{Section}:Mode is TrustedHeader but {Section}:TrustedProxies is empty: name the proxy's addresses.";
        return Networks(TrustedProxies, out string? bad) is null
            ? $"{Section}:TrustedProxies '{bad}' is not an address or a network like 10.42.0.0/16 (IPv4 written out in full, no bits past the prefix)."
            : null;
    }

    /// <summary>The proxy's networks - an address alone is a network of one - or null and the first that is not one.</summary>
    public static IReadOnlyList<IPNetwork>? Networks(string? configured, out string? bad)
    {
        bad = null;
        List<IPNetwork> networks = [];
        foreach (string entry in (configured ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = entry.Split('/');
            // The address half held to IngressOptions' rule: written out in full, so "10.42/16" is not 10.0.0.42.
            if (IngressOptions.Address(parts[0]) is not { } address || parts.Length > 2)
            {
                bad = entry;
                return null;
            }
            int full = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            if (parts.Length == 1)
            {
                networks.Add(new IPNetwork(address, full));
                continue;
            }
            // Bits past the prefix (10.42.1.0/16) are refused: meant /16 or /24? .NET's IPNetwork.TryParse quietly masks them off.
            // A /0 is every address: Auth:Mode=None without its warning, so it is refused - say None if that is meant.
            if (!int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int prefix)
                || prefix == 0 || prefix > full || !IPNetwork.TryParse(entry, out IPNetwork network) || !network.BaseAddress.Equals(address))
            {
                bad = entry;
                return null;
            }
            networks.Add(network);
        }
        if (networks.Count == 0)
            bad = configured;
        return networks.Count == 0 ? null : networks;
    }
}

/// <summary>Stops the host at startup on an authentication setting that cannot work, with what to set instead.</summary>
public sealed class AuthOptionsCheck(IOptions<IngressOptions> ingress, IHostEnvironment environment) : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options) =>
        options.Resolve(ingress.Value, environment.IsDevelopment(), out string? problem) is null
            ? ValidateOptionsResult.Fail(problem!)
            : ValidateOptionsResult.Success;
}

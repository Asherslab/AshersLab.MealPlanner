using System.Net;
using System.Net.Sockets;

namespace AshersLab.MealPlanner.Grpc.Features.Ingress;

/// <summary>
/// The proxy this host sits behind - the Home Assistant Supervisor, as an add-on.
/// </summary>
/// <remarks>
/// <b>Unset, the host trusts nobody's headers and refuses nobody.</b> That is local development and the test
/// host. The add-on sets <c>Ingress__TrustedProxy=172.30.32.2</c> in its <c>config.yaml</c> environment, which
/// turns on all three of: refusing every request from any other address, taking the base path from
/// <c>X-Ingress-Path</c>, and taking the user from <c>X-Remote-User-*</c>. They are one switch on purpose -
/// a header is only worth believing when the connection it arrived on could only have come from the proxy.
/// <para>
/// Behind any other proxy (Docker, Kubernetes) it is <see cref="Auth.AuthOptions"/> instead; the two are never both set.
/// </para>
/// </remarks>
public sealed class IngressOptions
{
    public const string Section = "Ingress";

    /// <summary>The Supervisor's address on the hassio network. Null: not behind ingress.</summary>
    public string? TrustedProxy { get; set; }

    public IPAddress? TrustedProxyAddress =>
        TrustedProxy is { Length: > 0 } configured ? Address(configured) ?? throw new FormatException($"Not an address: '{configured}'.") : null;

    /// <summary>
    /// At startup (<c>ValidateOnStart</c>): a typo in the add-on's environment stops the host with this message,
    /// instead of answering every request with a 500 from a parse inside the middleware.
    /// </summary>
    public bool IsValid => TrustedProxy is not { Length: > 0 } configured || Address(configured) is not null;

    /// <summary>
    /// An IPv6 address, or an IPv4 one written out in full. <see cref="IPAddress.TryParse(string, out IPAddress)"/>
    /// also takes the old shorthands - "172.30.32" is 172.30.0.32, and padded octets ("172.30.32.010") and bare
    /// numbers are accepted too - so a dropped or padded octet would quietly trust some other machine and refuse the
    /// Supervisor, instead of stopping the host. Written out in full, an IPv4 address reads back as itself.
    /// </summary>
    internal static IPAddress? Address(string configured) =>
        IPAddress.TryParse(configured, out IPAddress? address)
        && (address.AddressFamily == AddressFamily.InterNetworkV6 || address.ToString() == configured)
            ? address
            : null;
}

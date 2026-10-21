using System.Net;

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
/// Standalone (Kubernetes) auth behind some other proxy is slice 14; until then the image run outside Home
/// Assistant has no auth at all.
/// </para>
/// </remarks>
public sealed class IngressOptions
{
    public const string Section = "Ingress";

    /// <summary>The Supervisor's address on the hassio network. Null: not behind ingress.</summary>
    public string? TrustedProxy { get; set; }

    public IPAddress? TrustedProxyAddress =>
        TrustedProxy is { Length: > 0 } configured ? IPAddress.Parse(configured) : null;

    /// <summary>
    /// At startup (<c>ValidateOnStart</c>): a typo in the add-on's environment stops the host with this message,
    /// instead of answering every request with a 500 from a parse inside the middleware.
    /// </summary>
    public bool IsValid => TrustedProxy is not { Length: > 0 } configured || IPAddress.TryParse(configured, out _);
}

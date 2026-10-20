namespace AshersLab.MealPlanner.Grpc.Features.Ingress;

/// <summary>
/// The base path the browser sees the app under, from the Supervisor's <c>X-Ingress-Path</c> header.
/// </summary>
/// <remarks>
/// The Supervisor strips <c>/api/hassio_ingress/&lt;token&gt;</c> before forwarding, so the host only ever sees
/// <c>/</c>, <c>/_framework/…</c>, <c>/gRPC/…</c> - but the browser has to be told the prefix, or every relative
/// URL resolves against the Home Assistant root and leaves the add-on.
/// <para>
/// The value is written into HTML, so it is held to the characters a path segment from the Supervisor ever
/// contains rather than escaped and hoped for: anything else is refused and the shell falls back to <c>/</c>.
/// </para>
/// </remarks>
public static class IngressPath
{
    public const string Header = "X-Ingress-Path";

    /// <summary>
    /// <c>/api/hassio_ingress/abc</c> or <c>/api/hassio_ingress/abc/</c> → <c>/api/hassio_ingress/abc/</c>.
    /// False for a missing, relative or malformed value.
    /// </summary>
    public static bool TryParse(string? header, out string baseHref)
    {
        baseHref = "/";

        if (string.IsNullOrWhiteSpace(header) || header[0] != '/')
            return false;

        string trimmed = header.TrimEnd('/');
        if (trimmed.Length == 0)
            return true;

        foreach (string segment in trimmed[1..].Split('/'))
        {
            // Empty: "//", which a browser reads as a scheme-relative URL to another host. Dots: a path that
            // climbs out of where the Supervisor put us.
            if (segment.Length == 0 || segment is "." or ".." || !segment.All(IsSafe))
                return false;
        }

        baseHref = trimmed + "/";
        return true;
    }

    private static bool IsSafe(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~';
}

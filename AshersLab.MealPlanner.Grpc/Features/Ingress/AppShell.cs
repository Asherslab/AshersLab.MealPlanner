using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace AshersLab.MealPlanner.Grpc.Features.Ingress;

/// <summary>
/// Serves <c>index.html</c> with its <c>&lt;base href&gt;</c> set for this request, instead of as a static file.
/// </summary>
/// <remarks>
/// Under Home Assistant ingress the same image answers at <c>/api/hassio_ingress/&lt;token&gt;/</c>, and the
/// token differs per install (and changes when the add-on is reinstalled), so the base cannot be baked in at
/// build time. Every URL the WASM app makes - <c>_framework/</c>, <c>_content/</c>, the gRPC-Web channel, the
/// SQLite worker - is relative to it.
/// <para>
/// Read uncompressed from the web root rather than through <c>MapStaticAssets</c>, which would hand out its
/// precompressed <c>index.html.br</c> with the static <c>/</c> still in it.
/// </para>
/// </remarks>
public static class AppShell
{
    /// <summary>The exact line in <c>WASM/wwwroot/index.html</c> this replaces. A test pins it.</summary>
    public const string BaseHrefMarker = """<base href="/"/>""";

    public static string Rewrite(string html, string baseHref)
    {
        if (!html.Contains(BaseHrefMarker, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"index.html no longer contains {BaseHrefMarker}; the ingress base path cannot be set.");

        return baseHref == "/" ? html : html.Replace(BaseHrefMarker, $"""<base href="{baseHref}"/>""", StringComparison.Ordinal);
    }

    public static async Task Serve(HttpContext context)
    {
        IFileInfo file = context.RequestServices.GetRequiredService<IWebHostEnvironment>()
            .WebRootFileProvider.GetFileInfo("index.html");

        string html;
        await using (Stream stream = file.CreateReadStream())
        using (StreamReader reader = new(stream))
            html = await reader.ReadToEndAsync(context.RequestAborted);

        string body = Rewrite(html, IngressRequest.BaseHref(context));

        // no-cache: its body depends on a header, so a cached copy from another base path would break the app
        // outright. The installed app's service worker caches its own copy, fetched from here with its scope's base
        // (WASM/wwwroot/service-worker.published.js); a browser without one reads this every load.
        context.Response.ContentType                 = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl        = "no-cache";
        context.Response.Headers[HeaderNames.Vary]   = IngressPath.Header;
        await context.Response.WriteAsync(body, context.RequestAborted);
    }
}

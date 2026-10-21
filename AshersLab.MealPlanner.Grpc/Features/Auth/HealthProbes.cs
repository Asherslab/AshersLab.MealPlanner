using AshersLab.MealPlanner.Grpc.Data;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Grpc.Features.Auth;

/// <summary>
/// <c>/healthz</c> (the process answers) and <c>/healthz/ready</c> (and its database opens) for Kubernetes' probes.
/// </summary>
/// <remarks>
/// <b>Ahead of every authentication gate</b>: the kubelet probes the pod's own address, not through the sign-in proxy, so
/// behind the gate every probe would be a 403 and the pod restarted forever. They say "ok" or 503 and nothing else - no
/// version, no data - so answering anyone costs nothing. Aspire's <c>/health</c> stays Development-only.
/// </remarks>
public static class HealthProbes
{
    public const string Live = "/healthz";
    public const string Ready = "/healthz/ready";

    public static IApplicationBuilder UseHealthProbes(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                await next(context);
                return;
            }
            if (context.Request.Path.Equals(Live, StringComparison.Ordinal))
            {
                await Answer(context, true);
                return;
            }
            if (context.Request.Path.Equals(Ready, StringComparison.Ordinal))
            {
                await using AsyncServiceScope scope = context.RequestServices.CreateAsyncScope();
                MealPlannerDbContext db = scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>();
                await Answer(context, await db.Database.CanConnectAsync(context.RequestAborted));
                return;
            }
            await next(context);
        });

    private static Task Answer(HttpContext context, bool ok)
    {
        context.Response.StatusCode = ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/plain";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(ok ? "ok" : "unavailable", context.RequestAborted);
    }
}

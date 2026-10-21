using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Features.Ingress;
using AshersLab.MealPlanner.Grpc.Features.Server;
using AshersLab.MealPlanner.Grpc.Features.Sync;
using AshersLab.MealPlanner.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using ProtoBuf.Grpc.Server;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddCodeFirstGrpc();
builder.Services.AddGrpc();

// The connection string is resolved when the first context is built, not here, so a test host's
// configuration overrides (WebApplicationFactory.UseSetting) are already applied by the time it is read.
builder.Services.AddDbContext<MealPlannerDbContext>((sp, o) =>
    o.UseSqlite(DataDirectory.ConnectionString(sp.GetRequiredService<IConfiguration>())));

builder.Services.AddSingleton<SyncSignal>();

builder.Services.AddOptions<IngressOptions>()
    .Bind(builder.Configuration.GetSection(IngressOptions.Section))
    .Validate(o => o.IsValid, $"{IngressOptions.Section}:TrustedProxy is not an IP address.")
    .ValidateOnStart();

var app = builder.Build();

// Migrate before serving. One process and a single-writer database, so there is no migrations Job to
// wait on as in the GSBC repos: a migration that fails stops the host here, before it answers anything.
using (IServiceScope scope = app.Services.CreateScope())
{
    MealPlannerDbContext db = scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>();
    await db.Database.MigrateAsync();
    await SyncStartup.InitialiseAsync(db, app.Services.GetRequiredService<SyncSignal>());
}

// First, so a request from anywhere but the Supervisor is refused before it reaches a file or a service.
// Does nothing unless Ingress:TrustedProxy is set, which only the Home Assistant add-on does.
app.UseHomeAssistantIngress();

if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();

// The WASM app, from this same origin. UseBlazorFrameworkFiles serves _framework/ in a branch of its own
// and adds the Blazor-Environment header the client reads its environment from.
app.UseBlazorFrameworkFiles();

// /index.html by name is the shell too, never the static file with its static "/" base. A middleware, not an
// endpoint: MapGet("/index.html").WithOrder(-1) still lost to the static file in the test host.
app.Use((context, next) =>
    HttpMethods.IsGet(context.Request.Method) && context.Request.Path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)
        ? AppShell.Serve(context)
        : next(context));

// Explicit, and AFTER UseBlazorFrameworkFiles - load-bearing. Left implicit, WebApplication routes at the
// very start of the pipeline, so a _framework/ request has already been matched to a MapStaticAssets
// endpoint when it enters that branch, the branch never executes it, and every runtime file is a 500:
// "The request reached the end of the pipeline without executing the endpoint". The page then sits on
// "Loading…" forever. The test host cannot catch this - it has no static-assets manifest, so the endpoint
// that causes it never exists there. Check a _framework/ file against the running app instead.
app.UseRouting();

// grpc-web, because the caller is Blazor WebAssembly in a browser and a browser cannot speak HTTP/2 gRPC
// framing from fetch. DefaultEnabled = true is what lets it talk to these services at all.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapDefaultEndpoints();

// A service that compiles and is not mapped fails at the client as an unimplemented method, not at build.
app.MapGrpcService<ServerInfoService>();
app.MapGrpcService<SyncService>();

app.MapStaticAssets();

// The app shell, with <base href> set per request from the ingress path (Features/Ingress/AppShell.cs) - never
// the static index.html. Deep links (a reload on /meals/123) are the WASM router's to answer, so anything no
// endpoint claimed is the shell; the fallback is lowest priority, so it never shadows a gRPC or static route.
// /index.html is claimed by a middleware before the static files (after UseBlazorFrameworkFiles above).
app.MapFallback(AppShell.Serve);

app.Run();

// Visible to WebApplicationFactory in the test project.
public partial class Program;

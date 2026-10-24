using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Features.Accounts;
using AshersLab.MealPlanner.Grpc.Features.Auth;
using AshersLab.MealPlanner.Grpc.Features.Ingress;
using AshersLab.MealPlanner.Grpc.Features.Server;
using AshersLab.MealPlanner.Grpc.Features.Sync;
using AshersLab.MealPlanner.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProtoBuf.Grpc.Server;

// A host that fails to start must EXIT. Unhandled, an exception makes .NET abort(), and as a container's PID 1 - plain
// Docker, Kubernetes; not Home Assistant, which starts add-ons under an init - the kernel ignores that SIGABRT: the
// error is printed and the container sits "running", serving nothing, forever. Exiting is an ordinary exit. The test
// host never gets here: WebApplicationFactory catches what Program throws.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Console.Error.WriteLine($"Unhandled exception. {e.ExceptionObject}");
    Environment.Exit(1);
};

var builder = WebApplication.CreateBuilder(args);

// `dotnet AshersLab.MealPlanner.Grpc.dll snapshot`: the Home Assistant add-on's backup_pre. Writes a consistent copy
// of the database beside it and exits - it never starts the host, so it runs beside the one already serving.
if (args is [DatabaseSnapshot.Verb])
{
    string snapshot = await DatabaseSnapshot.TakeAsync(DataDirectory.DatabasePath(builder.Configuration));
    Console.WriteLine($"Wrote {snapshot}");
    return;
}

// The Home Assistant add-on's Configuration tab (/data/options.json): the public address, and reset_access.
AddOnOptions.AddTo(builder.Configuration);

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

// Who is asking, outside Home Assistant (Features/Auth/AuthOptions.cs). Outside Development, nothing configured is a
// host that will not start - the same image with no setting would otherwise serve everything to anyone.
builder.Services.AddOptions<AuthOptions>().Bind(builder.Configuration.GetSection(AuthOptions.Section)).ValidateOnStart();

// The app's own passkey sign-in (Features/Accounts), when that is the mode. A mode it cannot use stops the host at the
// check below, with the reason; so only a usable one is set up here.
AuthOptions auth = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
bool accounts = auth.Mode == "Accounts" && AccountRules.PublicUrl(auth.PublicUrl, out _) is not null;
if (accounts)
    builder.AddAccounts(auth);
builder.Services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsCheck>();

var app = builder.Build();

// Checked here, before the database is touched: a host that will not serve anyone has no business migrating it.
_ = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;

// After a Home Assistant restore /data holds only the backup snapshot (the live file is excluded from backups).
DatabaseSnapshot.RestoreIfMissing(DataDirectory.DatabasePath(app.Configuration), app.Logger);

// Migrate before serving. One process and a single-writer database, so there is no migrations Job to
// wait on as in the GSBC repos: a migration that fails stops the host here, before it answers anything.
using (IServiceScope scope = app.Services.CreateScope())
{
    MealPlannerDbContext db = scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>();
    await db.Database.MigrateAsync();
    if (accounts)
        await Accounts.StartAsync(app, auth);
    await SyncStartup.InitialiseAsync(db, app.Services.GetRequiredService<SyncSignal>());
}

// Kubernetes' probes, ahead of the gates below: the kubelet does not come through the proxy. "ok" and nothing else.
app.UseHealthProbes();

// First, so a request from anywhere but the Supervisor is refused before it reaches a file or a service.
// Does nothing unless Ingress:TrustedProxy is set, which only the Home Assistant add-on does.
app.UseHomeAssistantIngress();

// The same for a standalone image behind a sign-in proxy: does nothing unless Auth:Mode is TrustedHeader.
app.UseTrustedHeaderAuth();

// Or the app's own sign-in: a POST from another site refused, then who the session cookie says.
if (accounts)
    app.UseAccounts(auth);

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
// the splash forever. The test host cannot catch this - it has no static-assets manifest, so the endpoint
// that causes it never exists there. Check a _framework/ file against the running app instead.
app.UseRouting();

// grpc-web, because the caller is Blazor WebAssembly in a browser and a browser cannot speak HTTP/2 gRPC
// framing from fetch. DefaultEnabled = true is what lets it talk to these services at all.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapDefaultEndpoints();

// A service that compiles and is not mapped fails at the client as an unimplemented method, not at build.
if (accounts)
{
    app.UseAccountsAfterRouting();
    app.MapAccounts();
    // Every call needs a signed-in member; without one it is a 401, which the app shows as "Signed out".
    app.MapGrpcService<ServerInfoService>().RequireAuthorization();
    app.MapGrpcService<SyncService>().RequireAuthorization();
}
else
{
    app.MapGrpcService<ServerInfoService>();
    app.MapGrpcService<SyncService>();
    // No sign-in of the app's own: say so with a 404. Left unmapped, the shell's fallback would answer 200 with HTML.
    app.MapGet("/account/{**rest}", () => Results.NotFound());
}

app.MapStaticAssets();

// The app shell, with <base href> set per request from the ingress path (Features/Ingress/AppShell.cs) - never
// the static index.html. Deep links (a reload on /meals/123) are the WASM router's to answer, so anything no
// endpoint claimed is the shell; the fallback is lowest priority, so it never shadows a gRPC or static route.
// /index.html is claimed by a middleware before the static files (after UseBlazorFrameworkFiles above).
app.MapFallback(AppShell.Serve);

app.Run();

// Visible to WebApplicationFactory in the test project.
public partial class Program;

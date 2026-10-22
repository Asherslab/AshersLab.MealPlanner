using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Server;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.WASM;
using AshersLab.MealPlanner.WASM.Account;
using AshersLab.MealPlanner.WASM.Data;
using AshersLab.MealPlanner.WASM.Design;
using AshersLab.MealPlanner.WASM.Extensions;
using AshersLab.MealPlanner.WASM.Sync;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using SqliteWasmBlazor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

Uri api = new(builder.HostEnvironment.BaseAddress);

// A page injecting an unregistered service fails at RUNTIME, not at build, so a new service contract needs
// a line here as well as a MapGrpcService on the server.
builder.Services.AddCodeFirstClient<IServerInfoService>(api);
builder.Services.AddCodeFirstClient<ISyncService>(api);

// The browser's own SQLite, in OPFS. Registration only: nothing opens it until after the first render, when
// LocalDatabase.ReadyAsync starts the worker and migrates (docs/modules/sync/browser-database.md). BaseHref
// is set because under Home Assistant ingress the app is not at "/", and the worker script is fetched
// relative to it.
builder.Services.AddDbContextFactory<LocalDbContext>(options =>
    options.UseSqliteWasm(new SqliteWasmConnection("Data Source=mealplanner.db")));
builder.Services.AddSqliteWasm(options => options.BaseHref = api.AbsolutePath);
builder.Services.AddSqliteWasmDbContext<LocalDbContext>();
builder.Services.AddScoped<LocalDatabase>();

// One per window, started by the app bar's SyncStatusChip once the local database is ready.
builder.Services.AddScoped<SyncLoop>();

// The app's own sign-in (Account/): plain JSON to the server's /account endpoints, from this same origin - so the
// session cookie goes with every call, and with every gRPC-Web one.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = api });
builder.Services.AddScoped<AccountClient>();

// Light or dark (Design/AppearanceState.cs): this device's copy read before the first render, so MudBlazor's palette
// agrees with what js/appearance.js already put on the page.
builder.Services.AddScoped<AppearanceState>();

WebAssemblyHost host = builder.Build();
await host.Services.GetRequiredService<AppearanceState>().LoadAsync();
await host.RunAsync();

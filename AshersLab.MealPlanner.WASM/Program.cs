using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Server;
using AshersLab.MealPlanner.WASM;
using AshersLab.MealPlanner.WASM.Extensions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

Uri api = new(builder.HostEnvironment.BaseAddress);

// A page injecting an unregistered service fails at RUNTIME, not at build, so a new service contract needs
// a line here as well as a MapGrpcService on the server.
builder.Services.AddCodeFirstClient<IServerInfoService>(api);

await builder.Build().RunAsync();

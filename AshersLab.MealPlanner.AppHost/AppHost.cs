var builder = DistributedApplication.CreateBuilder(args);

// One resource, because production is one process: the gRPC host serves both the API and the WASM app
// (docs/work/2026-10-deployment.md). No YARP, no separate WASM dev server, no migrations worker, no
// database container - SQLite is a file the host opens itself.
//
// The database lives outside the repo, under the user's Application Support folder, so a `git clean` never
// takes it and nothing personal can be staged by accident. The path is recorded in .claude/app-local.md.
string dataDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AshersLab.MealPlanner");

// Its fixed ports (https 7283, http 5280) come from the project's launchSettings.json and must differ from
// both GSBC stacks, which run at the same time. They are recorded in .claude/app-local.md.
builder.AddProject<Projects.AshersLab_MealPlanner_Grpc>("mealplanner")
    .WithEnvironment("DataDirectory", dataDirectory)
    .WithExternalHttpEndpoints();

builder.Build().Run();

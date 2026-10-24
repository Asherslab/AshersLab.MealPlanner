---
title: How the app is hosted
kind: reference
status: current
module: infrastructure
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.AppHost/AppHost.cs
  - AshersLab.MealPlanner.Grpc/Program.cs
  - AshersLab.MealPlanner.Grpc/Data/DataDirectory.cs
  - AshersLab.MealPlanner.Grpc/Data/DatabaseSnapshot.cs
  - AshersLab.MealPlanner.Grpc/AshersLab.MealPlanner.Grpc.csproj
  - AshersLab.MealPlanner.WASM/Extensions/GrpcServiceExtensions.cs
  - AshersLab.MealPlanner.WASM/wwwroot/index.html
  - AshersLab.MealPlanner.Grpc/Features/Ingress
  - AshersLab.MealPlanner.Grpc/Features/Auth
---

# How the app is hosted

One process serves everything: the gRPC-Web API, the Blazor WebAssembly app, and the SQLite database it
owns. Read this before adding a project, a route, a client registration or anything that touches a URL.
How to *run* it locally is in `.claude/app-local.md`; why it is one image is in the
[deployment design](../../archive/2026-10-deployment.md).

## Traps first

- **`app.UseRouting()` must stay, after `UseBlazorFrameworkFiles()`** (`Grpc/Program.cs`). Left
  implicit, routing runs at the very start of the pipeline, matches every `_framework/` file to a
  `MapStaticAssets` endpoint, and the framework-files branch then ends without executing it: every
  runtime file is a 500 ("The request reached the end of the pipeline without executing the
  endpoint"), `index.html` is still a 200, and the phone shows the splash forever. The in-process test
  host has no static-assets manifest and **cannot catch this**; check a `_framework/` file against the
  running app.
- **gRPC clients use the app's base URI, never `/` and never a host name**
  (`WASM/Extensions/GrpcServiceExtensions.cs`, fed from `HostEnvironment.BaseAddress`). Under Home
  Assistant ingress the app lives below `/api/hassio_ingress/<token>/`; a root-relative call leaves the
  add-on. The base comes from `<base href>`, which the host rewrites per request (below).
- **`index.html` is never served as a static file.** Its `<base href="/"/>` line is replaced per request
  with the ingress prefix (`Grpc/Features/Ingress/AppShell.cs`), for `/`, `/index.html` and every deep link.
  Change that exact line in `WASM/wwwroot/index.html` and the rewrite throws — `IngressTests` pins it. A
  route or middleware that hands out the static file instead serves a shell whose every URL leaves the
  add-on: a blank panel in Home Assistant, and fine everywhere else.
- **A service that compiles and is not mapped answers "unimplemented"** at the client, and a contract
  with no `AddCodeFirstClient<T>` line in `WASM/Program.cs` fails when a page injects it — both at
  runtime, neither at build.

## The projects

| Project | What it is |
|---|---|
| `AshersLab.MealPlanner.AppHost` | Aspire, **local development only**. One resource: the gRPC host. |
| `AshersLab.MealPlanner.ServiceDefaults` | OpenTelemetry, health checks, service discovery. Inert in production: the OTLP exporter needs `OTEL_EXPORTER_OTLP_ENDPOINT` and `/health` exists only in Development. |
| `AshersLab.MealPlanner.Shared.Contracts` | protobuf-net records and code-first service interfaces. Rules in its `AGENTS.md`. |
| `AshersLab.MealPlanner.Shared.Domain` | Pure rules both ends run — the sync clock and merge rules, entity validation; later costing and units. No packages, no I/O. |
| `AshersLab.MealPlanner.Shared.Data` | The synced entities' EF model and the code that applies an op to them, referenced by both databases so they cannot disagree — see [offline sync](../sync/offline-sync.md). EF Core only, no provider. |
| `AshersLab.MealPlanner.Grpc` | The one production process: gRPC-Web, the WASM files, EF Core over SQLite. References the WASM project so its files are this project's static web assets. |
| `AshersLab.MealPlanner.WASM` | Blazor WebAssembly + MudBlazor. References the two `Shared` projects and `LocalData` — never the server. |
| `AshersLab.MealPlanner.LocalData` | The browser database's EF model and migrations — see [the browser database](../sync/browser-database.md) — and the browser's side of sync (outbox, one sync round), here rather than in the WASM app so tests can run it. Its own project so `dotnet ef` can load it. |
| `AshersLab.MealPlanner.Tests` | xunit v3. Hosts the real `Program` in process (`WebApplicationFactory`) and calls it over **gRPC-Web**, the protocol the browser uses — a plain-gRPC test would pass for a service the browser cannot reach. |

Compared with the GSBC repos this drops the YARP BFF, the nginx WASM image, the separate WASM dev-server
resource and the migrations worker. Same-origin is what YARP gave them; here the host serving the
files gives it for free, so there is no CORS and no service-discovery address in the client.

## Requests, in pipeline order

`Grpc/Program.cs`:

0. **Who to believe is checked first**, before the database is touched: outside Development, no authentication
   configured stops the host (`Features/Auth/AuthOptions.cs`). Any startup failure exits 1 rather than hanging as
   PID 1 (the `UnhandledException` handler at the top) — see [deployment](deployment.md).
1. **Migrate**, before the host listens. SQLite and one process, so there is no job to wait for; a
   migration that fails stops startup rather than serving a half-migrated database. Then the sync epoch is
   minted on a new database and the newest op seq read (`Features/Sync/SyncStartup.cs`).
2. `UseHealthProbes` (`Features/Auth/HealthProbes.cs`) — `/healthz` and `/healthz/ready`, ahead of the gates: the
   kubelet does not come through the proxy. Then `UseHomeAssistantIngress` (`Features/Ingress/IngressRequest.cs`), so it
   covers files and services alike. Inert unless `Ingress:TrustedProxy` is set; the add-on sets it. Then it refuses any
   other peer with 403, and takes the base path and the user from the Supervisor's headers. Then `UseTrustedHeaderAuth`
   (`Features/Auth/TrustedHeaderRequest.cs`), the same for a standalone sign-in proxy, inert unless `Auth:Mode` is
   `TrustedHeader` — both in [deployment](deployment.md).
3. `UseBlazorFrameworkFiles` — `_framework/` in its own branch, plus the `Blazor-Environment` header the
   client reads its environment from (`Development` locally).
4. `/index.html` by name → the app shell. A middleware: an endpoint ordered ahead of `MapStaticAssets`'
   own one still lost to the static file in the test host.
5. `UseRouting` — see the trap above.
6. `UseGrpcWeb(DefaultEnabled = true)` — browsers cannot speak HTTP/2 gRPC framing.
7. Endpoints: health (Development only), gRPC services under `/gRPC/AshersLab.MealPlanner.<Service>/`
   (the `[Service]` attribute's string), static assets, then `MapFallback(AppShell.Serve)` for `/` and deep
   links — lowest priority, so it never shadows a gRPC or file route. The shell goes out `no-cache`.

## Where the data lives

One setting, `DataDirectory` (`Grpc/Data/DataDirectory.cs`); the database is
`<DataDirectory>/mealplanner.db`. A full `ConnectionStrings:mealplanner` overrides it.

| Where | `DataDirectory` |
|---|---|
| Local, via the AppHost | `~/Library/Application Support/AshersLab.MealPlanner` — outside the repo |
| The image (Home Assistant add-on) | `/data` — the image's `ENV`; in HA backups |
| Kubernetes | `/data` too, a PVC (`Charts/mealplanner`); optionally replicated to S3 by Litestream — see [deployment](deployment.md) |
| Unset (bare `dotnet ef`, a project-only launch) | `app-data/` under the working directory, git-ignored |

The fallback is `app-data`, not `data`: macOS is case-insensitive, and `data` is the host project's
`Data/` source folder.

Beside it, `mealplanner.snapshot.db` (`Grpc/Data/DatabaseSnapshot.cs`): a self-contained copy, written only by
`dotnet AshersLab.MealPlanner.Grpc.dll snapshot` - the Home Assistant add-on's backup command, which is what goes into
a backup in place of the live file. **On start, a missing `mealplanner.db` beside a snapshot is restored from it**
(`DatabaseSnapshot.RestoreIfMissing`, before migrating; a stray `-wal`/`-shm` is deleted first, since it cannot belong
to the snapshot). A live database is never replaced. Why and how the backup works: [deployment](deployment.md). What
a restore does to the browsers is in [offline sync](../sync/offline-sync.md) - they give back what it lost.

## Package versions that are not the GSBC ones

- `Microsoft.AspNetCore.Components.WebAssembly.Server` is **10.0.13 or later**: 10.0.0–10.0.12 carry two
  remote-code-execution advisories (CVE-2026-102870, CVE-2026-96230), and this package runs in production.
- OpenTelemetry is 1.20, not the GSBC repos' 1.15.0, which has published advisories.
- The AppHost's Aspire 13.2.2 pulls in a `MessagePack` with advisories. It is development-only and never
  in the image, so it is left as a restore warning rather than pinned over.

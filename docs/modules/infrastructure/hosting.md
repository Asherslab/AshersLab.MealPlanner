---
title: How the app is hosted
kind: reference
status: current
module: infrastructure
verified: 2026-10-20
code:
  - AshersLab.MealPlanner.AppHost/AppHost.cs
  - AshersLab.MealPlanner.Grpc/Program.cs
  - AshersLab.MealPlanner.Grpc/Data/DataDirectory.cs
  - AshersLab.MealPlanner.Grpc/AshersLab.MealPlanner.Grpc.csproj
  - AshersLab.MealPlanner.WASM/Extensions/GrpcServiceExtensions.cs
  - AshersLab.MealPlanner.WASM/wwwroot/index.html
---

# How the app is hosted

One process serves everything: the gRPC-Web API, the Blazor WebAssembly app, and the SQLite database it
owns. Read this before adding a project, a route, a client registration or anything that touches a URL.
How to *run* it locally is in `.claude/app-local.md`; why it is one image is in the
[deployment design](../../work/2026-10-deployment.md).

## Traps first

- **`app.UseRouting()` must stay, after `UseBlazorFrameworkFiles()`** (`Grpc/Program.cs`). Left
  implicit, routing runs at the very start of the pipeline, matches every `_framework/` file to a
  `MapStaticAssets` endpoint, and the framework-files branch then ends without executing it: every
  runtime file is a 500 ("The request reached the end of the pipeline without executing the
  endpoint"), `index.html` is still a 200, and the phone shows "Loading…" forever. The in-process test
  host has no static-assets manifest and **cannot catch this**; check a `_framework/` file against the
  running app.
- **gRPC clients use the app's base URI, never `/` and never a host name**
  (`WASM/Extensions/GrpcServiceExtensions.cs`, fed from `HostEnvironment.BaseAddress`). Under Home
  Assistant ingress the app lives below `/api/hassio_ingress/<token>/`; a root-relative call leaves the
  add-on. `index.html` still has a static `<base href="/">` — rewriting it from `X-Ingress-Path` is
  slice 3, tracked in [home-assistant-ingress](../../open-questions/home-assistant-ingress.md).
- **A service that compiles and is not mapped answers "unimplemented"** at the client, and a contract
  with no `AddCodeFirstClient<T>` line in `WASM/Program.cs` fails when a page injects it — both at
  runtime, neither at build.

## The projects

| Project | What it is |
|---|---|
| `AshersLab.MealPlanner.AppHost` | Aspire, **local development only**. One resource: the gRPC host. |
| `AshersLab.MealPlanner.ServiceDefaults` | OpenTelemetry, health checks, service discovery. Inert in production: the OTLP exporter needs `OTEL_EXPORTER_OTLP_ENDPOINT` and `/health` exists only in Development. |
| `AshersLab.MealPlanner.Shared.Contracts` | protobuf-net records and code-first service interfaces. Rules in its `AGENTS.md`. |
| `AshersLab.MealPlanner.Shared.Domain` | Pure rules both ends run — costing, units, merge rules. No packages, no I/O. Empty until slice 2. |
| `AshersLab.MealPlanner.Grpc` | The one production process: gRPC-Web, the WASM files, EF Core over SQLite. References the WASM project so its files are this project's static web assets. |
| `AshersLab.MealPlanner.WASM` | Blazor WebAssembly + MudBlazor. References the two `Shared` projects and `LocalData` — never the server. |
| `AshersLab.MealPlanner.LocalData` | The browser database's EF model and migrations — see [the browser database](../sync/browser-database.md). Its own project only so `dotnet ef` can load it. |
| `AshersLab.MealPlanner.Tests` | xunit v3. Hosts the real `Program` in process (`WebApplicationFactory`) and calls it over **gRPC-Web**, the protocol the browser uses — a plain-gRPC test would pass for a service the browser cannot reach. |

Compared with the GSBC repos this drops the YARP BFF, the nginx WASM image, the separate WASM dev-server
resource and the migrations worker. Same-origin is what YARP gave them; here the host serving the
files gives it for free, so there is no CORS and no service-discovery address in the client.

## Requests, in pipeline order

`Grpc/Program.cs`:

1. **Migrate**, before the host listens. SQLite and one process, so there is no job to wait for; a
   migration that fails stops startup rather than serving a half-migrated database.
2. `UseBlazorFrameworkFiles` — `_framework/` in its own branch, plus the `Blazor-Environment` header the
   client reads its environment from (`Development` locally).
3. `UseRouting` — see the trap above.
4. `UseGrpcWeb(DefaultEnabled = true)` — browsers cannot speak HTTP/2 gRPC framing.
5. Endpoints: health (Development only), gRPC services under `/gRPC/AshersLab.MealPlanner.<Service>/`
   (the `[Service]` attribute's string), static assets, then `MapFallbackToFile("index.html")` for deep
   links — lowest priority, so it never shadows a gRPC or file route.

## Where the data lives

One setting, `DataDirectory` (`Grpc/Data/DataDirectory.cs`); the database is
`<DataDirectory>/mealplanner.db`. A full `ConnectionStrings:mealplanner` overrides it.

| Where | `DataDirectory` |
|---|---|
| Local, via the AppHost | `~/Library/Application Support/AshersLab.MealPlanner` — outside the repo |
| Home Assistant add-on | `/data` (in HA backups) — set in slice 3 |
| Kubernetes | a PVC mount — slice 14 |
| Unset (bare `dotnet ef`, a project-only launch) | `app-data/` under the working directory, git-ignored |

The fallback is `app-data`, not `data`: macOS is case-insensitive, and `data` is the host project's
`Data/` source folder.

## Package versions that are not the GSBC ones

- `Microsoft.AspNetCore.Components.WebAssembly.Server` is **10.0.13 or later**: 10.0.0–10.0.12 carry two
  remote-code-execution advisories (CVE-2026-102870, CVE-2026-96230), and this package runs in production.
- OpenTelemetry is 1.20, not the GSBC repos' 1.15.0, which has published advisories.
- The AppHost's Aspire 13.2.2 pulls in a `MessagePack` with advisories. It is development-only and never
  in the image, so it is left as a restore warning rather than pinned over.

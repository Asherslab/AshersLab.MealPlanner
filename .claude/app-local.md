# AshersLab.MealPlanner — local run specifics

Resolves the `{{PLACEHOLDER}}` values in the global `run-and-inspect-app` skill
(`~/.claude/skills/run-and-inspect-app/SKILL.md`). Read the skill for the procedure; read this for the
values. Where the two disagree, **this file wins** — it is written against the running app.

**This stack is smaller than the one the skill describes.** One Aspire resource, no YARP, no WASM dev
server, no containers, no Postgres. Several sections of the skill therefore do not apply — see "What
differs from the skill" below before following it.

| Placeholder | Value |
|---|---|
| `{{REPO_ROOT}}` | `/Users/asherp/Personal-Projects/Git/AshersLab.MealPlanner` |
| `{{SOLUTION_PREFIX}}` | `AshersLab.MealPlanner` |
| `{{APPHOST_PROJECT}}` | `AshersLab.MealPlanner.AppHost` |
| `{{API_PROJECT}}` | `AshersLab.MealPlanner.Grpc` — also serves the WASM app |
| `{{WASM_PROJECT}}` | `AshersLab.MealPlanner.WASM` — never run on its own |
| `{{YARP_PROJECT}}` | none — there is no YARP (see `docs/work/2026-10-deployment.md`) |
| `{{TESTS_PROJECT}}` | `AshersLab.MealPlanner.Tests` |
| `{{RUN_CONFIG}}` | `AshersLab.MealPlanner.AppHost: https` |
| `{{APP_PORT}}` | `7283` (https) / `5280` (http) — the gRPC host, and the only port to open in a browser |
| `{{DASHBOARD_MCP_PORT}}` | `16056` — but see below, the dashboard MCP is not wired up |
| `{{ASPIRE_MCP_NAME}}` | none yet — no `.mcp.json` entry exists for this stack |
| `{{POSTGRES_PORT}}` | none — SQLite, see Database |
| `{{DB_NAME}}` | none — the database is one file, see Database |
| `{{AUTHED_PAGE}}` | none — no auth yet; every page is open locally |

The run configurations are **shared files** in `.run/` (committed), so Rider lists them as soon as the
solution is open: `AshersLab.MealPlanner.AppHost: https` and `: http`. Rider also offers per-project
launch-profile configurations (`AshersLab.MealPlanner.Grpc: https`); do not use those — they start the
host without the AppHost's `DataDirectory`, so it writes its database under `AshersLab.MealPlanner.Grpc/app-data/`.

## Running the tests

The `AshersLab.MealPlanner.Tests` run configuration (Rider's project configuration — xunit v3 test
projects are executables) runs the whole suite and prints a summary. Run it with
`execute_run_configuration` and `waitForExit: true`. Rider's test-runner gutter points are not exposed
through the MCP for this project, so this is the way.

## Ports

Both GSBC stacks run at the same time as this one. Every fixed port here differs from theirs —
ImpactKids holds 7263, 5250, 17259/15014, 21191, 22297, 16036, 19276, 20239 and 60535–60537;
GSBC.Accounting holds 7273/5242, 5260, 7068/5279, 17269/15024, 21201, 22307, 16046, 19286, 20249,
60546–60547. All of the below were verified free and then listening on 2026-10-20.

| Port | What | Pinned in |
|---|---|---|
| 7283 / 5280 | gRPC host — **the app's address**, API and WASM on one origin | `AshersLab.MealPlanner.Grpc/Properties/launchSettings.json` |
| 17279 / 15034 | Aspire dashboard | AppHost `launchSettings.json`, `.run/*.run.xml` |
| 21211 / 19296 | Aspire OTLP (https / http profile) | same |
| 22317 / 20259 | Aspire resource service (https / http profile) | same |
| 16056 | Aspire dashboard MCP | same |

As with the GSBC stacks, 7283 is Aspire's DCP **proxy**; the host process itself binds a random port.
Opening that random port works for the page but nothing else should be pointed at it.

## What differs from the skill

- **No YARP, no separate WASM resource.** The gRPC host serves `_framework/`, `_content/` and
  `index.html` itself. The skill's "WASM changes need a full restart" still holds — the host snapshots
  the WASM's static-web-assets manifest at startup.
- **Restarting.** Kill by built binary path, AppHost first, then wait for the host to go:

  ```bash
  ps ax -o pid=,command= | awk '$2 ~ /AshersLab.MealPlanner\.[A-Za-z.]*\/bin\/Debug/ {print $1, $2}'
  kill <apphost pid>    # the Grpc host follows within a few seconds; kill it too if it does not
  ```

  Never `pkill -f AshersLab.MealPlanner` — agent sessions carry the name in their command lines.
- **No service worker yet**, so the skill's stale-service-worker section does not apply until the PWA
  slice adds one.
- **The dashboard MCP is not configured.** There is no `AppHost:McpApiKey` in this AppHost's user
  secrets and no `.mcp.json`. Until someone adds both (per the skill), read the dashboard at
  `https://localhost:17279` — the login URL with its token is printed in the run configuration's
  output — or read the host's own answers with `curl`.
- **A `_framework/` file answering 500** means `app.UseRouting()` has been moved or removed from
  `AshersLab.MealPlanner.Grpc/Program.cs`; see the comment there. The page shows "Loading…" forever.
  `curl -sk -o /dev/null -w '%{http_code}\n' https://localhost:7283/_framework/blazor.webassembly.js`
  should be 200. The test host cannot catch this.

## Driving the app

Chrome (`mcp__claude-in-chrome__*`) works against `https://localhost:7283` with no sign-in. The home
page is the scaffold's wiring check: it calls `IServerInfoService.Get` over gRPC-Web and shows the
server version, server time and the newest applied migration. The refresh icon (aria-label "Check the
server again") repeats the call — the server time changing is the evidence that it went to the server.

In the network panel the call is `POST https://localhost:7283/gRPC/AshersLab.MealPlanner.ServerInfo/Get`.

## Database

**SQLite, one file, outside the repo:**

```
~/Library/Application Support/AshersLab.MealPlanner/mealplanner.db
```

The AppHost sets `DataDirectory` to that folder (`AppHost.cs`). Read it directly:

```bash
sqlite3 "$HOME/Library/Application Support/AshersLab.MealPlanner/mealplanner.db" \
  'select * from "__EFMigrationsHistory";'
```

The host migrates at startup, so a missing table means the host has not started, not that a
migration job is pending. Nothing in it is worth keeping until the household uses the app — delete
the folder to start over (stop the app first; SQLite holds `-wal`/`-shm` files beside it).

The browser's own SQLite arrives in slice 1; how to read it will be written here then.

## Migrations

`dotnet ef` from `AshersLab.MealPlanner.Grpc`, which has an `IDesignTimeDbContextFactory`:

```bash
cd AshersLab.MealPlanner.Grpc && dotnet ef migrations add <WhatItDoes> -o Data/Migrations
```

Name a migration for what it does, never a timestamp.

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
through the MCP for this project, so this is the way. It takes no arguments (no `-method` filter through the MCP).

**`build_solution` can build from Rider's stale view of a file edited outside it** - "success", and the old code.
Seen repeatedly on 2026-10-21 while mutation-testing: an edit made with the Edit tool, then a build seconds later
that compiled nothing. Call `get_file_problems` on each edited file first (it makes Rider re-read it), then build,
and when it matters check that `AshersLab.MealPlanner.Tests/bin/Debug/net10.0/<Project>.dll` is newer than the
source (`[ dll -nt source ]`). A test run on a stale build reports the old code's result.

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

**Ingredients** (`/ingredients`, "Ingredients" in the app bar) is the op log's page: the "New ingredient" box
(`InputId` `ingredient-new-name`, Enter or `data-testid` `ingredient-add`), and per row (`ingredient-row`, its
`data-id` the ingredient id) a pencil (`ingredient-rename`) opening `ingredient-rename-input` and
`ingredient-rename-save`. The sync chip at the right of the app bar is `data-testid` `sync-status`, with
`data-state` (`connecting|online|offline|failed|refused`) and `data-pending` (outbox count, including ops accepted
but not yet pulled back). Sync calls are
`/gRPC/AshersLab.MealPlanner.Sync/Push|Pull|Changes`; `Changes` stays open.

Typing into a MudTextField from a script needs the native `value` setter and an `input` event (see
`typeInto` in `.claude/tools/chrome-sync-check.mjs`); assigning `.value` alone is not seen by Blazor.

### Two profiles, offline, concurrent edits

```bash
node .claude/tools/chrome-sync-check.mjs <scratch>/sync     # about 40 s, prints PASS/FAIL per check
```

Two separate headless Chromes on fresh profiles under `<scratch>/sync` (ports 19341/19342 - if taken, a
previous run's Chrome is alive: `pkill -f "user-data-dir=<scratch>/sync"`). Offline is the DevTools protocol's
`Network.emulateNetworkConditions`, the same switch as DevTools' Offline. It checks create → other profile,
an offline rename syncing on reconnect, and two concurrent offline renames ending on the later one, then copies
each profile's SQLite out of OPFS (directly over the DevTools protocol - no receiver needed) and prints its rows
beside the server's. Ingredients are named with the run id, so runs pile up in the database; wipe it when that
matters. The Chrome extension cannot do this: it has no offline switch and drives only one profile.

### A server restore, with both profiles open

```bash
node .claude/tools/chrome-restore-check.mjs <scratch>/restore     # run_in_background; same ports as above
```

Halfway it backs up the server's SQLite (`sqlite3 .backup`), makes the edits the restore will lose, takes profile
A offline and prints `RESTORE NOW` with a backup path and a signal file. Then, by hand: stop the app (the kill
commands above), delete `mealplanner.db-wal` and `-shm`, copy the backup over `mealplanner.db`, start the AppHost
run configuration, wait for `_framework/blazor.webassembly.js` to answer 200, and `touch` the signal file. About
two minutes in all; prints PASS/FAIL per check. Profile B usually pushes back before you touch the file, which
is why the script compares chains rather than the head seq. Read the backup with plain `sqlite3`, not
`-readonly`: it is in WAL mode with no `-shm`, which a read-only open cannot create ("unable to open database
file").

## The image, behind a simulated Home Assistant ingress

The AppHost serves the app at `/`, which hides every base-path bug. To see what the add-on sees, build the
image and run it behind nginx playing the Supervisor (why and what it proves: `docs/modules/infrastructure/deployment.md`):

```bash
docker buildx build --platform linux/arm64 -t mealplanner:local --load .     # ~80 s cold
DATA_DIR=<scratch>/sim-data sh .claude/tools/ingress-sim/run.sh               # --down to stop
```

| Port | What |
|---|---|
| 18299 | the fake Supervisor — open `http://localhost:18299/api/hassio_ingress/sImUlAtEdToKeN/` |
| 18298 | the app's own port — must answer 403 |

The server database is `<scratch>/sim-data/mealplanner.db`. The two-profile check runs against it with
`APP_URL=http://localhost:18299/api/hassio_ingress/sImUlAtEdToKeN/ SERVER_DB=<scratch>/sim-data/mealplanner.db`.
After a rebuild, `--down` and start again; a page left open on the old container logs 503s/404s until reloaded.

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
migration job is pending. Sync's tables: `Op` (the log, by `Seq`, each with its `Chain`), `Ingredient`,
`FieldClock`, `SyncEpoch` (one row - a new value after a wipe means every browser forgets what it synced and takes a
snapshot on its next pull). Nothing in it is worth keeping until the household uses the app — delete
the folder to start over (stop the app first; SQLite holds `-wal`/`-shm` files beside it).

## The browser database

How it works is in `docs/modules/sync/browser-database.md`. How to look at it:

- **In the page** — the home page's "This device" card (`data-testid` `device-id`, `device-open-count`,
  `device-storage`, `device-role`, or `local-db-state` when it could not open) reads the `Device` row
  through EF. In the console, `mealPlannerDatabase.role()` and `mealPlannerDatabase.events` show what the
  cross-window shim did.
- **Independently, with `sqlite3`** — copy the file out of OPFS. The Chrome extension's output filter
  refuses base64 *and* hex, so the bytes cannot come back through `javascript_tool`'s result; the page
  POSTs them to a receiver on 127.0.0.1 instead:

  ```bash
  python3 -I .claude/tools/opfs-receive.py <scratch>/opfs      # run_in_background
  ```

  then run `.claude/tools/opfs-dump.js` in the page with `javascript_tool` (set its `label`), and:

  ```bash
  mkdir -p <scratch>/read && cp <scratch>/opfs/<label>-mealplanner.db <scratch>/read/m.db \
    && cp <scratch>/opfs/<label>-mealplanner.db-wal <scratch>/read/m.db-wal \
    && sqlite3 <scratch>/read/m.db 'select * from Device;'
  ```

  Copy the `-wal` too: committed rows usually live there until a checkpoint.
- **Several windows** — `node .claude/tools/chrome-multiwindow-check.mjs <scratch>/profile` drives a
  separate headless Chrome through sharing, leader hand-overs, reloads, simultaneous starts and dropped
  downloads, and prints PASS/FAIL per check (about a minute; give each run a fresh profile directory). A
  failure is classified `boot-download`, `boot-unknown` or `shim-stall`; only `shim-stall` is a bug in
  `shared-database.js`, and it prints that window's shim events and Web Locks. A brand-new headless profile
  drops requests (`ERR_CERT_VERIFIER_CHANGED`) for about its first second, so the script waits 3 s first
  (`FAST_FIRST=1 WARMUP_MS=0` reproduces it). If it says the port is taken, a headless Chrome from an earlier
  run is still alive: `pkill -f "user-data-dir=<scratch>"`.
  The Chrome extension cannot do this: closing the first tab of its tab group drops the whole group, and
  the other tabs can no longer be driven.
- **Across a browser restart** — never quit the user's Chrome. `.claude/tools/chrome-restart-check.mjs`
  launches a separate headless Chrome on a throwaway profile, reads the card, and ends the browser with
  `Browser.close` or `SIGKILL`; run it repeatedly on the same profile directory.
- **Wiping it** — DevTools → Application → Storage → "Clear site data", or in the page
  `await (await navigator.storage.getDirectory()).removeEntry('databases', {recursive: true})` with every
  app tab closed but one, then reload.

Browser-database migrations are generated in `AshersLab.MealPlanner.LocalData`, not the host:

```bash
cd AshersLab.MealPlanner.LocalData && dotnet ef migrations add <WhatItDoes> -o Migrations
```

## The `wasm-tools` workload

The WASM project relinks the .NET runtime (`WasmBuildNative`), which needs `wasm-tools`. This machine's
SDK is Homebrew's 10.0.401 (runtime 10.0.12), which lags the newest workload set, so the workload is pinned
to the SDK's own set:

```bash
dotnet workload install wasm-tools
dotnet workload update --version 10.0.401    # after any SDK upgrade, use the new SDK's version
```

The symptom of a mismatch is a successful build and a page stuck on "Loading…" with
`RuntimeError: function signature mismatch` in the console.

## Migrations

`dotnet ef` from `AshersLab.MealPlanner.Grpc`, which has an `IDesignTimeDbContextFactory`:

```bash
cd AshersLab.MealPlanner.Grpc && dotnet ef migrations add <WhatItDoes> -o Data/Migrations
```

Name a migration for what it does, never a timestamp.

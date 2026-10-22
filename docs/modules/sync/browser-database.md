---
title: The browser database
kind: reference
status: current
module: sync
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.LocalData
  - AshersLab.MealPlanner.WASM/Data/LocalDatabase.cs
  - AshersLab.MealPlanner.WASM/Data/LocalDataGate.razor
  - AshersLab.MealPlanner.WASM/Data/ResetDeviceDialog.razor
  - AshersLab.MealPlanner.WASM/Components/ThisDeviceCard.razor
  - AshersLab.MealPlanner.WASM/Program.cs
  - AshersLab.MealPlanner.WASM/AshersLab.MealPlanner.WASM.csproj
  - AshersLab.MealPlanner.WASM/wwwroot/js/storage.js
  - AshersLab.MealPlanner.WASM/wwwroot/js/shared-database.js
  - AshersLab.MealPlanner.Tests/Js
  - .claude/tools/chrome-device-reset-check.mjs
---

# The browser database

Each browser keeps its own SQLite database, which the app reads and writes while offline; the server's
copy is the master (see [offline sync](offline-sync.md)). Read this before
adding a local table, a local migration, or anything that waits on the local database.

## Traps first

- **Building needs the `wasm-tools` workload, at the workload set that matches the SDK's runtime.**
  `WasmBuildNative` relinks `dotnet.native.wasm` on every build. If the workload's runtime packs are a
  newer patch than the managed runtime the SDK targets, the build succeeds and the app dies at boot with
  `RuntimeError: function signature mismatch` in `mono_wasm_load_runtime`, page stuck on "Loading…". On a
  machine whose SDK lags the newest workload set (Homebrew's `dotnet`), pin it:
  `dotnet workload update --version <sdk version>` — 10.0.401 ships runtime 10.0.12 and workload manifests
  10.0.112. `dotnet workload list` shows the pinned version.
- **Nothing may touch the database before `LocalDatabase.StartAsync()` has answered `READY`**
  (`WASM/Data/LocalDatabase.cs`). The worker starts and migrates *after the first render*, so a context
  used from `OnInitializedAsync` or `Program.cs` hits a worker that does not exist yet. Call it from
  `OnAfterRenderAsync`.
- **Never add `<SqliteWasmDatabaseInitializer/>`, and never start SqliteWasmBlazor any other way.**
  `StartAsync` first loads `wwwroot/js/shared-database.js`, which replaces the worker SqliteWasmBlazor
  creates; anything that starts SqliteWasmBlazor earlier gets an unshared worker, and every other window
  of the app then fails with `TAB_LOCKED`. The script is not a `<script>` tag on purpose: a script tag whose
  download is dropped fails silently, and that exact failure was seen (see "Several windows").
- **One context at a time in a window - get them only from `LocalDatabase.Contexts`.** Every context in a
  window shares SqliteWasmBlazor's one connection object, so two alive at once break each other: overlapping
  transactions fail with `cannot start a transaction within a transaction`, and a plain read in a second
  context can close the connection under the first (`Connection must be Open`) or join its transaction. Seen in
  Chrome with a sync round and two quick adds. `Contexts` is a `SerialLocalDbContextFactory`
  (`LocalData/SerialLocalDbContextFactory.cs`): the next `CreateDbContextAsync` waits until the last context is
  disposed. So never hold a context across a network call or a wait on the user, and never open a second while
  holding one - that waits forever. The sync-free `CreateDbContext` throws; there is no blocking wait on the
  browser's one thread. `SyncEndToEndTests.Overlapping_writes_and_a_round_in_one_window_do_not_collide` fails
  without it - its test browser yields before every statement, because in-memory SQLite never interleaves.
- **Every window of the app shares one SQLite connection.** Two `BEGIN`s cannot overlap: the shim holds
  other windows' statements while one window's transaction is open. A transaction that waits on another
  window, or on the user, holds up every window — keep them short, and never await UI inside one. (The shim
  queues *other* windows; the trap above is about a window's own contexts.)
- **`shared-database.js` is coupled to SqliteWasmBlazor's bridge ↔ worker messages** (`{type:'init'}`,
  `{id, data:{type:'execute', sql}}`, `{type:'ready'|'error'}`, `streamId`). Re-run
  `.claude/tools/chrome-multiwindow-check.mjs` after upgrading the package.
- **SqliteWasmBlazor's reader cannot read a `DateOnly` column** - `InvalidCastException` from
  `SqliteWasmDataReader.GetFieldValue<DateOnly>`, which kills the page and the sync round. The server's provider and
  the tests' plain SQLite read it fine, so only the browser shows it. Give a date column an explicit string converter
  in `SyncedEntities.BuildModel` (as `PriceObservation.ObservedOn` has). `decimal`, `long`, `bool`, enums by name and
  `Guid` read fine. Seen in Chrome on 2026-10-21; any new column type needs a look in a real browser.
- **A model change needs a migration.** The browser applies migrations, never `EnsureCreated`, and a model
  change without one builds and then fails in every browser that already has the database.
  `LocalDbContextTests.Every_model_change_has_a_migration` catches it.
- **Never edit or regenerate a local migration a browser may have applied.** The browser records the id it
  applied; a regenerated one has a new id, so EF applies it again, its `CREATE TABLE` meets the table already
  there, and that browser cannot open its copy (`SCHEMA_INCOMPATIBLE`). Seen 2026-10-22 in a profile last opened
  during development. The page's reset (below) gets the device going again, but it loses any unsent edits; once
  real data exists, write a new migration instead.

## What it is

| Piece | Where |
|---|---|
| The model and its migrations | `AshersLab.MealPlanner.LocalData` — `LocalDbContext`, plain EF Core |
| The provider | [SqliteWasmBlazor](https://github.com/b-straub/SqliteWasmBlazor) 0.9.4-pre (MIT): EF Core's ADO.NET calls are sent to the official sqlite-wasm in a Web Worker |
| The storage | The Origin Private File System, through sqlite-wasm's **SAHPool** VFS |
| The file | `mealplanner.db` (`Program.cs`), journal mode WAL, `synchronous = FULL` |
| Its tables | `Device` (this browser, below); the synced tables `Ingredient` and `FieldClock`, defined once in `Shared.Data` and identical to the server's; `Outbox` and `SyncState`, this browser's own - see [offline sync](offline-sync.md) |

Chosen because it is plain EF Core over a real file-backed SQLite: no copy of the whole database to
IndexedDB or the Cache API after each write (Bit.Besql's approach), and so no window in which a closed tab
loses the last write. Every EF Core option in the browser needs the native relink, so that cost is not
specific to this library. It is pre-1.0 — pin the version, and read its release notes before moving it.
On its own it allows one tab per profile (its author declined multi-tab support, issues #7 and #17); the
sharing below is this repo's.

The model lives in its own project because `dotnet ef` cannot load a browser-wasm assembly. Migrations:

```bash
cd AshersLab.MealPlanner.LocalData && dotnet ef migrations add <WhatItDoes> -o Migrations
```

`LocalDbContextFactory` points the design-time context at plain SQLite; the dialect and the migrations are
the same.

## Starting up

`LocalDatabase.StartAsync()`, the first time anything calls it after the first render:

1. Imports `js/shared-database.js`, retrying up to four times with a fresh URL. If it never loads, the start
   is `FAILED` and the card asks for a reload — never an unshared database.
2. Calls SqliteWasmBlazor's `ISqliteWasmInitializer.InitializeAsync()`, which creates the worker (now the
   shim's) and applies migrations - holding the Web Lock `mealplanner-db-start`, so one window at a time does.
   Two windows migrating the one shared database together both read the same history and both apply the next
   migration; the second fails `SCHEMA_INCOMPATIBLE`. Seen when a reset reloaded two windows onto an empty
   database at once, about one run in four before the lock.
3. Records this start against the `Device` row, in a `BEGIN IMMEDIATE` transaction so two windows starting
   together cannot both insert one.
4. Asks the browser to keep the storage.

The result is cached, so a start is counted once however many components ask.

`Device` (`LocalData/Device.cs`) is one row per browser profile: its `Id` is the device id every op
carries, and what starts the sync loop (`WASM/Sync/SyncStatusChip.razor`, once the start is `READY`). It is also the visible proof that the database persists — the home page's "This device" card shows
the id, when it was first opened, how many starts it has seen, and which window holds the database. A new
id, or the count back at 1, means the browser lost the storage.

## Several windows

The owner keeps two windows of the app side by side, so every window in a browser profile works on the one
database at once. OPFS access handles are exclusive to one worker, so `wwwroot/js/shared-database.js`
stands in for the worker SqliteWasmBlazor creates:

| Part | How |
|---|---|
| Who runs the worker | The window holding the Web Lock `mealplanner-db-leader`. Every other window queues on the same lock. |
| How the others reach it | `BroadcastChannel('mealplanner-db')`: requests out, answers back by window id. Request ids are renumbered at the leader so two windows' ids cannot collide. |
| Transactions | `TransactionGate`: from the moment a window's `BEGIN` is sent until its `COMMIT`/`ROLLBACK` answers, other windows' statements wait in arrival order. `ROLLBACK TO` a savepoint does not end it. |
| A window closes mid-transaction | Each window holds its own Web Lock; when the leader is granted it, that window has gone, and the leader rolls its transaction back. |
| The leading window closes | The next window in the lock queue starts a worker and announces itself. Requests that were in flight to the old leader fail ("try again") — nobody can know whether they ran. A transaction open on the old leader fails until its `ROLLBACK`, so its later statements cannot commit one by one. |
| The new worker cannot start yet | The lock and the access handles are let go by different parts of a closing window. A worker that fails before it is ready is retried 5 times, 0.25 s doubling to 4 s, then reported. |
| Other windows' changes | After a commit, or a write outside a transaction, the leader broadcasts; `LocalDatabase.ChangedElsewhere` fires in every other window, and the card re-reads. |
| A reset | Every window waits on the Web Lock `mealplanner-db-reset` (shared) before it opens anything; a reset holds it exclusively. See "When this browser's copy cannot be opened". |
| Diagnostics | `mealPlannerDatabase.events` in the console: the last 50 things this window's shim did. `mealPlannerDatabase.role()` is `leader` or `follower`. |

Exports and imports (SqliteWasmBlazor's `streamId` messages) stage through OPFS and work only in the leading
window; anywhere else they fail with a message saying so. Nothing uses them yet.

Measured 2026-10-21, `.claude/tools/chrome-multiwindow-check.mjs` in headless Chrome, 25 clean runs with no
retries (22 checks each), each: a second window sharing and counted, the first window refreshing on its own, five leader
hand-overs, three reloads of the leading window beside a follower, three reloads of a sole window, three
windows starting at once (each counted once, one `Device` row), and a window whose first request for the
shim *and* first request for the worker script were dropped (both retried; it opened leading and shared).
Also checked by hand in Chrome: a follower that sent `BEGIN IMMEDIATE` and was closed had its transaction
rolled back, and the leader's queued statement then ran.

Two failures were seen while building this, both the same cause: a request dropped at the network level
(`net::ERR_CERT_VERIFIER_CHANGED` from a brand-new headless profile in its first second). When the dropped
request was the shim's own `<script>`, Blazor booted *without* the shim, opened the database unshared, and
crashed on `mealPlannerDatabase.subscribe` — which is why the script is now imported with retries and the
start fails visibly instead. The pure parts — statement classification and the gate — have unit tests in
`AshersLab.MealPlanner.Tests/Js/shared-database.test.mjs`, run by the xunit suite through Node (skipped,
not passed, where Node is missing).

## When this browser's copy cannot be opened

A start that ends in anything but `READY` shows the reason in every page's `LocalDataGate` (`local-db-state`);
Home's "This device" card points at it rather than repeating it. When the failure is one a reset can cure
(`LocalDatabaseStart.CanReset`: any failure to open or migrate - `SCHEMA_INCOMPATIBLE`, `FAILED`, `TIMEOUT` - but
not `TAB_LOCKED`, where a window outside the shim holds the files, nor the cross-window script failing to
download, where a reload is the cure) the gate offers **Reset this device's copy** (`local-db-reset`). Never
automatic:

1. `LocalDatabase.CountUnsentAsync` reads the outbox through EF, which works while the migrations are what failed.
   It is an upper bound (the outbox also holds edits the server accepted but this browser has not pulled back)
   and `null` - "there's no telling" - when the table or the worker cannot answer within 5 s.
2. `ResetDeviceDialog` asks in two steps, as the server-reset question does: what resetting means and what would
   be lost (`device-reset-unsent`), then "This can't be undone" (`device-reset-confirm`).
3. `mealPlannerDatabase.reset()` in `shared-database.js` takes the reset lock, stops this window's worker if it
   leads, and broadcasts `reset`: every other started window stops its worker and reloads - and, reloaded, waits
   on the reset lock. Once this window holds the leader lock, no worker is running anywhere. It deletes the OPFS
   root entries `databases` (SqliteWasmBlazor's SAHPool - every file) and `export-staging`, retrying for about
   10 s while a just-terminated worker's access handles let go, and reloads. The other windows then start one at a
   time (the start lock) on an empty database.
4. If another window keeps the leader lock for 20 s - an old build that does not know `reset` - or the files will
   not delete, nothing is deleted, the lock is given back, and the gate says to close the other windows.

What goes with the files: everything this browser holds is in that one database - the `Device` row, the outbox,
`SyncState` (cursor, epoch, an unanswered server-replaced question) and every synced row. So the browser comes
back as a **new device**: a new id, a first-load pull of the server's snapshot. Keeping the old id is deliberately
not done - its clock state would be gone, and a new edit's HLC could repeat one the server already holds. The server keeps
no device list, so nothing there needs cleaning up. The one other piece of state, `SyncLoop`'s sessionStorage note
that it already reloaded once for an out-of-date build, clears itself on the next successful round.

Seen working with `node .claude/tools/chrome-device-reset-check.mjs <scratch>` (port 19351): it plants the
2026-10-22 failure (deletes the newest migration's history row), holds one unsent edit, opens a second window,
resets from the first at 390 px, and reads back both windows, the browser's SQLite and the server's. `UNREADABLE=1`
also drops the outbox, for the "no telling" wording. Six clean runs plus that variant on 2026-10-22; the
multi-window check still passes all 22.

## Persistence, as measured

Chrome desktop, 2026-10-20 — all with the same device id afterwards and the count going up by exactly one
per start:

| Event | Result |
|---|---|
| Hard reload (Cmd+Shift+R) | Kept |
| Reload fired the instant a start's write landed, five times running | Kept, no lost write, no lock error |
| Closing the tab that held the database, then opening a new one | Kept |
| Navigating to another site and straight back (before the shim) | Once reported `TAB_LOCKED` — most likely the old page in the back/forward cache still held the handles. A page holding a Web Lock is not kept in that cache, so the shim's lock should prevent it; not re-tested. |
| Browser quit (`Browser.close`) and relaunched on the same profile, twice | Kept |
| Browser killed (`SIGKILL`) and relaunched | Kept |

A write is committed in the worker before `SaveChangesAsync` returns (`synchronous = FULL` on an OPFS
access handle), so a tab closed after an edit has been acknowledged cannot lose it; a tab closed during the
write rolls the transaction back. That is the property the op log's outbox relies on.

**Safari on iOS is not yet confirmed** — see [sqlite-in-the-browser](../../open-questions/sqlite-in-the-browser.md).

## Eviction

`wwwroot/js/storage.js` calls `navigator.storage.persist()` once per start, and the card says whether the
browser agreed. Chrome grants it from site engagement, so a new site usually starts at "May be cleared";
Safari grants it to a site added to the home screen. Either way the server copy makes eviction a re-sync,
losing only unsynced edits.

## What it costs

Release build, brotli, as a phone downloads it on first load:

| Part | Size |
|---|---|
| EF Core, Relational, Sqlite provider, Microsoft.Data.Sqlite | 1.42 MB |
| `sqlite3.wasm` + worker + bridge | 0.44 MB |
| SqliteWasmBlazor and SQLitePCLRaw managed code | 0.11 MB |
| `dotnet.native.wasm`, relinked | 0.95 MB (it is downloaded anyway; relinking swaps the SQLite inside it for a 10 KB stub) |

Time, Chrome desktop, Release, reload: the page renders at about 0.4 s and the database is ready at about
2.0 s — worker start, the migrations check and EF Core's first model build. The UI does not wait for it.
A mid-range phone is slower; measure it in the iOS check.

## Reading it

The app's own card is one reader. To read the rows independently, copy the file out of OPFS — see "The
browser database" in `.claude/app-local.md`.

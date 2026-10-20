---
title: The browser database
kind: reference
status: current
module: sync
verified: 2026-10-20
code:
  - AshersLab.MealPlanner.LocalData
  - AshersLab.MealPlanner.WASM/Data/LocalDatabase.cs
  - AshersLab.MealPlanner.WASM/Components/ThisDeviceCard.razor
  - AshersLab.MealPlanner.WASM/Program.cs
  - AshersLab.MealPlanner.WASM/AshersLab.MealPlanner.WASM.csproj
  - AshersLab.MealPlanner.WASM/wwwroot/js/storage.js
---

# The browser database

Each browser keeps its own SQLite database, which the app reads and writes while offline; the server's
copy is the master (see the [offline sync design](../../work/2026-10-offline-sync.md)). Read this before
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
  (`WASM/Data/LocalDatabase.cs`). SqliteWasmBlazor starts its worker and migrates *after the first render*,
  so a context used from `OnInitializedAsync` or `Program.cs` hits a worker that does not exist yet. Call
  it from `OnAfterRenderAsync`.
- **Only one tab per browser profile can open the database.** OPFS synchronous access handles are
  exclusive; a second tab gets `DbInitState.TAB_LOCKED` and `ThisDeviceCard` says the app is open in another
  tab. A second browser *profile* (or browser) is a separate origin storage and is unaffected. Seen once
  with only one tab: navigate the tab to another site and then straight back, and the new page reported
  `TAB_LOCKED` — most likely the old page, kept in the back/forward cache, still held the handles. The
  next load opened normally.
- **A model change needs a migration.** The browser applies migrations, never `EnsureCreated`, and a model
  change without one builds and then fails in every browser that already has the database.
  `LocalDbContextTests.Every_model_change_has_a_migration` catches it.

## What it is

| Piece | Where |
|---|---|
| The model and its migrations | `AshersLab.MealPlanner.LocalData` — `LocalDbContext`, plain EF Core |
| The provider | [SqliteWasmBlazor](https://github.com/b-straub/SqliteWasmBlazor) 0.9.4-pre (MIT): EF Core's ADO.NET calls are sent to the official sqlite-wasm in a Web Worker |
| The storage | The Origin Private File System, through sqlite-wasm's **SAHPool** VFS |
| The file | `mealplanner.db` (`Program.cs`), journal mode WAL, `synchronous = FULL` |

Chosen because it is plain EF Core over a real file-backed SQLite: no copy of the whole database to
IndexedDB or the Cache API after each write (Bit.Besql's approach), and so no window in which a closed tab
loses the last write. Every EF Core option in the browser needs the native relink, so that cost is not
specific to this library. It is
pre-1.0 — pin the version, and read its release notes before moving it.

The model lives in its own project because `dotnet ef` cannot load a browser-wasm assembly. Migrations:

```bash
cd AshersLab.MealPlanner.LocalData && dotnet ef migrations add <WhatItDoes> -o Migrations
```

`LocalDbContextFactory` points the design-time context at plain SQLite; the dialect and the migrations are
the same.

## Starting up

`<SqliteWasmDatabaseInitializer/>` in `MainLayout.razor` starts the worker after the first render;
`LocalDatabase.StartAsync()` awaits that (it is idempotent), then records this start against the `Device`
row and asks the browser to keep the storage. The result is cached, so a start is counted once however many
components ask.

`Device` (`LocalData/Device.cs`) is one row per browser profile: its `Id` is the device id every op will
carry. It is also the visible proof that the database persists — the home page's "This device" card shows
the id, when it was first opened and how many starts it has seen. A new id, or the count back at 1, means
the browser lost the storage.

## Persistence, as measured

Chrome desktop, 2026-10-20 — all with the same device id afterwards and the count going up by exactly one
per start:

| Event | Result |
|---|---|
| Hard reload (Cmd+Shift+R) | Kept |
| Reload fired the instant a start's write landed, five times running | Kept, no lost write, no lock error |
| Closing the tab that held the database, then opening a new one | Kept |
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

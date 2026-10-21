---
title: Offline sync
kind: reference
status: current
module: sync
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.Shared.Domain/Sync
  - AshersLab.MealPlanner.Shared.Domain/Ingredients/IngredientRules.cs
  - AshersLab.MealPlanner.Shared.Data
  - AshersLab.MealPlanner.Shared.Contracts/Entities/Features/Sync
  - AshersLab.MealPlanner.Shared.Contracts/Messages/Requests/Features/Sync/SyncRequests.cs
  - AshersLab.MealPlanner.Shared.Contracts/Messages/Responses/Features/Sync/SyncResponses.cs
  - AshersLab.MealPlanner.Shared.Contracts/Services/Features/Sync/ISyncService.cs
  - AshersLab.MealPlanner.Grpc/Features/Sync
  - AshersLab.MealPlanner.Grpc/Data/MealPlannerDbContext.SyncModel.cs
  - AshersLab.MealPlanner.Grpc/Data/Models/Sync
  - AshersLab.MealPlanner.LocalData/Sync
  - AshersLab.MealPlanner.WASM/Sync
  - AshersLab.MealPlanner.WASM/wwwroot/js/connectivity.js
  - AshersLab.MealPlanner.WASM/Features/Ingredients/IngredientsPage.razor
  - AshersLab.MealPlanner.WASM/Data/LocalDataPage.cs
  - AshersLab.MealPlanner.Grpc/Features/Catalogue/CatalogueSeed.cs
  - AshersLab.MealPlanner.Tests/Sync
  - AshersLab.MealPlanner.Tests/Domain
  - .claude/tools/chrome-sync-check.mjs
  - .claude/tools/chrome-restore-check.mjs
  - .claude/tools/chrome-server-reset-check.mjs
---

# Offline sync

How every browser and the server end up with the same data when any of them changes it, including while a
browser is offline. Read before adding a synced entity or any write path: **every edit in this app is an op**
in the browser's outbox; there is no "call the API to save".

The browser's database itself - OPFS, migrations, sharing it across windows - is
[the browser database](browser-database.md).

## Traps first

- **A page never sets an entity's properties and saves.** It calls `LocalOps.WriteAsync`
  (`LocalData/Sync/LocalOps.cs`), which applies the edit *and* queues it in one transaction. An edit saved any
  other way never reaches the server and is overwritten by the next pull that touches the field.
- **One local context at a time per window** - see the trap in [the browser database](browser-database.md).
  A sync round and an edit overlapping is the normal case, not a corner case.
- **An op that changes nothing still wins.** Its clock is newer than whatever another device did meanwhile, so
  a "rename" to the same name silently undoes a real rename made elsewhere. The pages send no op when the name
  is unchanged, and an edit carries only the fields that changed (`NameList.SaveRename`,
  `LocalDataPage.WriteAsync`); every future editor must do the same.
- **A new synced entity is four places and two migrations**: its row class in `Shared.Data`, its
  `SyncedEntity<TRow>`, a line in `SyncedEntities.All`, its table in `SyncedEntities.BuildModel` - then a
  migration in *both* `Grpc` and `LocalData`. An entity missing from `All` is rejected by the server. Once real
  data exists it also needs a `SyncProtocol` bump (below). Its rules go in `Shared.Domain`, built on `OpRules`.
- **Seed data is ops the server writes at startup, never a migration's rows** - see
  [the catalogue](../catalogue/catalogue.md). So a new server's log is not empty: it starts with the seeds.
- **Never `Update` a counter.** Two devices adding one each would both write "previous + 1" and one would be lost.
  Count rows instead: serves eaten are the calendar's leftovers entries, counted when read
  ([batches and serves](../planning/batches-and-serves.md)); stock on hand is a ledger of movements, one row each
  ([the pantry](../pantry/pantry.md)).
  Two devices adding one each add two rows, and nothing merges. There is no `Increment` op kind and none is planned. A
  number the planner states ("we made 8") is not a counter - it is an ordinary field, and the later edit wins.
- **`Seq` order relies on SQLite having one writer** (`Grpc/Data/Models/Sync/DbOp.cs`). A push numbers and
  commits its ops before the next push can number any, so a pull never sees seq N+1 without N. On Postgres a
  later seq could commit first and a pull would skip the earlier op forever. Revisit before moving.
- **A device whose clock is far ahead wins every conflict** until the others' clocks pass it. The clock
  (below) protects against a few minutes of skew, which is what phones have; nothing guards against a clock set
  a year ahead.
- **The server cannot tell it was restored from a backup** - the backup holds the epoch, the log and
  `sqlite_sequence` alike, and nothing outside `/data` survives. The browsers can: each pull carries a fingerprint
  of the log at its cursor (below), and what a restore lost comes back from the browsers that still hold it. A
  browser that held an edit and is never opened again takes it with it.
- **A rejected op stays applied in the browser that made it**, and nothing corrects it until someone edits that
  field again. That is safe only because the browser checks every edit with the server's own check before
  applying it (`SyncedEntity.Problem`, called by both `LocalOps.WriteAsync` and `SyncService.Push`). **Never add a
  server-only check on an op's content**; tightening a rule is a `SyncProtocol` bump, so an older build with the
  looser rule is refused instead of rejected op by op. `SyncEndToEndTests.The_browser_refuses_exactly_what_the_server_would`
  is the guard.

## Shape

```
Browser (each window)                                    Server (Grpc)
┌───────────────────────────────────────┐                ┌───────────────────────────────────┐
│ page ─edit─> LocalOps.WriteAsync      │                │ SyncService                       │
│               ├ OpApplier → Ingredient│                │  Push ─> OpApplier → Ingredient,  │
│               │   + FieldClock        │                │          FieldClock; log in Op    │
│               └ Outbox    (one txn)   │                │          (seq = rowid), dedupe id │
│ SyncLoop ─> SyncRound:                │ ──Push──────>  │  Pull(since seq) ─> ops > seq,    │
│   push Outbox, then pull past cursor  │ <─Pull──────── │          or a snapshot            │
│   OpApplier on each pulled op         │ <─Changes───── │  Changes ─> "newest seq is N"     │
└───────────────────────────────────────┘                └───────────────────────────────────┘
```

| Piece | Where | Runs on |
|---|---|---|
| Clock, merge rules, entity validation | `Shared.Domain/Sync`, `Shared.Domain/Ingredients` | both - pure functions |
| Synced tables and applying an op to them | `Shared.Data` (`SyncedEntities`, `OpApplier`) | both - one applier, so they cannot disagree |
| Op log, epoch, push/pull/stream | `Grpc/Features/Sync` | server |
| Outbox, cursor, one sync round | `LocalData/Sync` (`LocalOps`, `SyncRound`) | browser, and the tests |
| When to run a round, status, protocol refusal | `WASM/Sync/SyncLoop.cs` | browser |

`SyncRound` is in `LocalData`, not the WASM app, so the test suite runs real rounds against the real host.

## An op

`{ OpId, DeviceId, Hlc, Entity, EntityId, Kind, Fields }` (`Contracts/.../SyncOp.cs`). `Kind` is `Create`,
`Update` or `Delete` (`Shared.Domain/Sync/OpKind.cs`, on the wire as its number - never renumber). `Fields` is
only the fields that changed, each a name and invariant-culture text; each entity's rules parse their own.

- **Ids are GUIDv7, minted by the device**, entity ids and op ids alike - with one exception: a calendar entry a plan
  makes has an id derived from its template entry and date, so two devices applying the same plan make the same row
  ([the calendar](../planning/calendar.md)). Nothing waits on the server for an id,
  so something created offline can be referred to offline.
- **The clock is a hybrid logical clock** (`Hlc.cs`): wall milliseconds, a counter, and the device id as the tie
  break. `Hlc.Observe` moves a device's clock past every op it pulls, so an edit made *after seeing* another
  always orders after it, even from a phone whose clock is minutes slow. The string form
  (`000001792508583639.00000.<device>`) is fixed width, so SQL `ORDER BY` and the merge agree.
- **Every field remembers the clock that set it** - `FieldClock (Entity, EntityId, Field, Hlc)`, one table for
  every entity type, the same in both databases.

## Merge rules

`OpMerge.Apply` (`Shared.Domain/Sync/OpMerge.cs`), run identically by the server when it accepts a push and by
every browser when it pulls. Truth table and permutation tests: `Tests/Domain/OpMergeTests.cs`.

| Case | Rule |
|---|---|
| Two updates to **different** fields | Both apply. This is why ops carry fields, not rows. |
| Two updates to the **same** field | The higher clock wins. Two renames in the same millisecond: the higher device id. |
| The same op again (a retry, a device pulling its own op back) | Equal clock: changes nothing. |
| Delete vs anything | Delete wins. The row stays as a tombstone (`Deleted = true`) and ignores every later op, so a late update from an offline device cannot bring it back. |
| Create of an id that exists | An update of the fields it carries. |
| Update or delete of an id never seen | Creates it (a tombstone, for a delete), so the result does not depend on arrival order. |
| Reference to a deleted entity (later: a calendar entry → a deleted meal) | The referencing entity survives and shows the reference is gone. Nothing cascades on sync. |

For any set of ops, every arrival order gives the same result; the tests check every permutation of four
scenarios. **What the planner sees** in a conflict: both people rename "eggs" offline, and once both are back
online both phones show whichever rename was made later. Nothing asks; the earlier rename is in the server's
log but shown nowhere.

Delete is in the rules and tested end to end (applier, server, pull, snapshot). What the pages do about deleting
something another row refers to is [the catalogue](../catalogue/catalogue.md)'s.

## The server

`Grpc/Features/Sync/SyncService.cs`, mapped at `/gRPC/AshersLab.MealPlanner.Sync/`.

- **Push** applies a batch (at most 500) in one `BEGIN IMMEDIATE` transaction. Per op: an op id already logged
  answers `Duplicate` with its original seq; one that fails validation (unknown entity, bad clock, a clock naming
  another device, a field the entity does not have, an invalid name) answers `Rejected` with a reason and is
  **not** logged; anything else is applied and logged with a new seq, **whether or not it won its merge** -
  every browser must see the same ops to reach the same answer. The answer carries the server's epoch.
- **The log is append-only** (`Op` table): seq from SQLite's rowid, the op, its fields as JSON
  (`Shared.Data/Sync/OpFieldsJson.cs`, source-generated so trimming cannot drop it), and its **chain**
  (`Shared.Domain/Sync/LogChain.cs`): a hash of the previous op's chain and this op's id, so two logs with the same
  chain at a seq hold the same ops up to it. The materialised tables (one per entity in `SyncedEntities.All`, and
  `FieldClock`) are what server-side reads will use. `SyncService.AcceptAsync` applies and logs one op; push and the
  startup seeding both go through it.
- **Pull** returns ops after the browser's seq, ascending, at most 500 (200 by default), with `HasMore`, each with
  its chain. It returns a **snapshot** instead - every synced row with each field's clock, as of the newest seq and
  its chain, read in one transaction - when the cursor means nothing here, and says why (`SnapshotReason`). **A
  tombstone travels with its fields** (`OpApplier.SnapshotAsync`), applied before its delete: a phone that only ever
  saw the snapshot still knows a deleted meal by name, so the import does not make it again. Which values a tombstone
  holds can differ between copies - an update arriving after the delete is ignored - so read them as "what was
  deleted", never as data:

  | Reason | When | What the browser does |
  |---|---|---|
  | `FirstLoad` | no cursor yet | merges it |
  | `NewDatabase` | the cursor names another epoch: the server was reinstalled without a backup | holding nothing the new database lacks: forgets the old rows and merges. Otherwise **applies nothing and asks the planner** (below) |
  | `HistoryLost` | same epoch, but the op at the cursor's seq is missing or has a different chain: **restored from an older backup** | queues everything it holds that the snapshot lacks, merges, pushes |

  Seq alone cannot detect a restore: the restored log numbers new ops from the backup's head again, so a
  browser's "seq 41" may name a different op on the restored server (AUTOINCREMENT would not help - the backup
  restores `sqlite_sequence` too). The chain at the cursor is what the server compares.
- **The epoch** (`SyncEpoch` table) is a GUID minted on the database's first start (`SyncStartup.cs`), not in a
  migration - seed data would give every database the same one. A restore keeps it, which is right: it is the
  same database, missing its end.
- **Changes** is a server stream that only ever says "newest seq is N": on connect, after every push, and as a
  heartbeat every 25 s so a proxy (Home Assistant ingress) does not close an idle stream. gRPC-Web carries server
  streaming; it has no client or bidirectional streaming, which is why push and pull are unary.

## The browser

- **The outbox** (`Outbox` table) holds this browser's ops until **its cursor has passed them**: a pull brings the
  op back, or a push answers "already have it" at a seq the cursor has reached (`LocalOps.AcknowledgeAsync`). A
  push's "accepted" alone does not remove it - if the server is then restored from a backup older than that push,
  the op is still here and simply goes again. A rejected op leaves at once; it would be rejected forever.
- **The cursor** (`SyncState`: epoch, pulled-through seq and that op's chain, this device's clock) is one row
  shared by every window.
- **A round** (`SyncRound.RunAsync`) pushes the whole outbox in batches of 200, then pulls until caught up. After
  a snapshot it pushes and pulls once more: a snapshot carries rows, not op ids, so only a second push can tell
  which queued ops the server already has, and a `HistoryLost` snapshot has just queued ops to give back. Every
  step commits on its own and repeating one is harmless, so a round that dies halfway loses nothing.
- **A snapshot is merged, not copied** (`LocalOps.ApplySnapshotAsync`): each field at its own clock, so an
  unsynced local edit newer than the server's value survives.
- **What a restored server lost comes back from the browsers** (`LocalOps.WhatTheServerLacksAsync`, on a
  `HistoryLost` snapshot). Every field newer here than in the snapshot is queued as an update **with the clock
  that set it, as an op from the device that made the edit** (`OutboxOp.DeviceId`), so it merges on the server
  exactly as the original did. **The device matters**: the server refuses an op whose clock names another device,
  so another phone's edit sent as this one's is refused and lost for good. Its op id is derived from the edit, so
  two browsers giving back the same edit send one op id and the server logs it once. An edit still in the outbox is
  not queued twice - it goes back by itself. A tombstone the snapshot lacks goes back as a new delete from this
  device (a delete needs no clock - it wins). Seen: a restore that lost a create, a rename and another browser's
  create got all three back from the first browser to reconnect; the second found nothing missing.
- **A server that came back empty is the planner's call, never the browser's** - see "The server came back empty"
  below. Until they answer, that browser pushes and pulls nothing.
- **Several edits as one** (`LocalOps.WriteAllAsync`, which `WriteAsync` is the one-edit case of): every edit is
  checked with the server's rules before anything is written, then all are applied and queued in one transaction,
  each with its own clock tick in the order given - all of them or none. For something written as a whole, where
  half would be worse than nothing: the [spreadsheet import](../import/spreadsheet-import.md) - about 900 ops with its
  plan - and a plan entry with its choices. They push in batches like any other queued ops. **Each edit is saved and
  the change tracker cleared before the next**, inside the one transaction: kept tracked, every save scanned every row
  the batch had touched, and the import with its plan took 54 s in desktop Chrome; cleared, 12 s.
- **Pulled pages and snapshots are forgotten op by op, row by row, too** (`LocalOps.ApplyPulledAsync`,
  `ApplySnapshotAsync`), for the same reason: `OpApplier` saves per op. A fresh browser's first load after that import
  (a snapshot of about 900 rows) took 147 s to show anything; cleared, 22 s, most of it the WASM app starting. The
  server's push path is native .NET and was not the bottleneck; it still saves op by op without clearing.
- **When a round runs** (`WASM/Sync/SyncLoop.cs`, its decisions in `LocalData/Sync/SyncSchedule.cs` and tested in
  `Tests/Sync/SyncScheduleTests.cs`): on start; after every edit; when the change stream reports a seq other than
  the cursor's (a restored server reports one *below* it), or reconnects after a drop; on the browser's `online`
  event (`wwwroot/js/connectivity.js`); every 5 s while the server cannot be reached; and every 30 s regardless. The
  5 s retry is not optional: an idle open stream can survive the network going away, so it cannot be relied on to
  notice the network coming back.
- **Offline is not the same as broken** (`LocalData/Sync/SyncFailure.cs`, `SyncSchedule.AfterFailure`). Not reaching
  the server - a failed fetch, `Unavailable` (also what the Supervisor's 502/503 become while the add-on restarts), a
  cut-off response - is "Offline", logged at Information, retried every 5 s. Anything else - a pulled op that will
  not apply, a server error, `Unimplemented` - is "Failed": logged as an error and shown. It does not fix itself: a
  pull that fails rolls back, the cursor stays put, and this browser receives nothing after that op until the cause
  is fixed. **While Failed, change notices do not start rounds** - the stream's 25 s heartbeat and every other
  device's edit would each re-run a round that cannot succeed - so it retries every 30 s, on the planner's own edits,
  and when they tap the chip. A dropped stream never turns "Sync problem", "Update needed" or "Paused · needs you"
  into "Offline".
- **Every window runs its own loop.** Safe because every step is idempotent; two windows pushing the same outbox
  only do the work twice (seen: the server logged a second window's create exactly once).

**What the planner sees** is the chip at the right of the app bar (`WASM/Sync/SyncStatusChip.razor`), on every
page: "Synced", "Saving 2", "Offline · 3 waiting", "Sync problem · 3 waiting" (red; the tooltip says what failed
and that changes are kept), "Update needed", or "Paused · needs you" (amber, while the question below is open). Tapping
it syncs now.

## The server came back empty

The Pi reinstalled without a backup, or the add-on's data deleted: a new database with a new epoch, under browsers
that still hold the old one's rows. **Those rows may be the only copy left**, so no browser decides alone what
happens to them.

- **When it asks.** A `NewDatabase` snapshot is compared with what the browser holds, exactly as after a restore
  (`LocalOps.WhatTheServerLacksAsync`): a row the snapshot lacks, a field newer here, a tombstone of a row it has, or
  anything in the outbox. Any of those - the question is recorded in `SyncState.ReplacedByEpoch` and nothing is
  applied. None (a browser that only ever saw the seeds, which are the same rows at clock zero in every database) -
  it takes the new database without asking. A tombstone of a row the new database never had does not ask on its own.
- **Nothing reaches the new database first.** A round pushes before it pulls, so the push carries the epoch the
  browser's cursor belongs to (`PushRequest.ServerEpoch`); the server logs nothing from a push aimed at another
  database and answers with no results (`SyncService.Push`). Every later round sees the open question and returns
  before touching the network (`SyncRound.RunAsync`, `SyncRoundResult.Replaced`).
- **Every window shows it.** The question lives in the shared database, so every window's next round finds it; the
  chip says "Paused · needs you" and `ServerReplacedPrompt` (in the layout) opens `ServerReplacedDialog` - no close
  button, no backdrop click, no Escape. The first answer in any window applies (`LocalOps.DecideAsync` checks the
  question is still open, in one transaction); the other windows hear the commit (`LocalDatabase.ChangedElsewhere`),
  run a round, find nothing open and close theirs.
- **"Send my copy"** resets the cursor and sets `SendCopyOnNextSnapshot`. The next pull is a first load from the new
  database, compared with every row held here, and everything it lacks is queued with the clock that set it and the
  device that made it - the restore mechanism above, so several browsers sending their copies merge by the ordinary
  rules: the same edit from two of them is one op id, logged once; different edits to a field go to the later one;
  a delete wins; a renamed seed beats the new database's seed. The outbox goes as it is.
- **"Clear my copy"** asks a second time, in red, how many items will be gone for good, then deletes every synced
  row and every queued edit here and takes the server as it is. "Go back" changes nothing.

What the planner sees: the dialog "The server has lost its copy" - "This device still has 14 items the server
doesn't", **Send my copy** (one tap) or **Clear my copy** (two taps, the second "Delete them for good"). Each phone or
computer asks once, the first time it reaches the new server. `Tests/Sync/ServerReplacedTests.cs` covers the
decision and the merge; `.claude/tools/chrome-server-reset-check.mjs` the running app. An edit made offline shows in the list at
once and waits in the outbox; nothing is lost by closing the tab. "Saving" includes edits the server has accepted
but this browser has not yet pulled back - normally for well under a second.

## Protocol version

Every push, pull and stream carries `SyncProtocol.Current` as the browser was built
(`Contracts/.../SyncProtocol.cs`). The server refuses anything outside
[`OldestSupported`, `Current`] with `FailedPrecondition` and the detail `sync-protocol-unsupported`, before
reading the ops. The browser's answer is to reload once (guarded by `sessionStorage`) to fetch the new build -
its outbox is in SQLite, survives the reload, is migrated by the new build and then pushed. A second refusal in
the same session shows "Update needed" instead of reloading in a loop.

Bump `Current` when an entity's fields or their meaning change in a way an older browser would misread; raise
`OldestSupported` when the server can no longer accept the older shape. A change to *how* sync works counts, not
only an entity's shape: version 2 is the restore check - a pull carries the chain at its cursor and may be answered
with a `HistoryLost` snapshot - which a version-1 browser can neither send nor read, so both `Current` and
`OldestSupported` are 2. **While nothing is in production, wipe both databases instead** of keeping an old version
alive.

The refusal is tested over gRPC-Web for all three calls (`SyncEndToEndTests.Refuses_a_client_on_a_protocol_version_it_cannot_read`).
The browser's reload-on-refusal has not been seen in a browser: it needs a server built with a higher
`OldestSupported` than the page.

## What is not synced through ops

- **Server-produced data** (budget figures, live prices, version 2) will be ops the server writes with its own
  device id. Browsers never write those entity types.
- **Computed values** - costs, shopping-list lines, serves remaining - are never stored or synced. Both ends
  compute them from synced data with the same `Shared.Domain` code.
- **Receipt files** are never stored anywhere. Parsed lines are ops like everything else.

## Where offline works

| Situation | Works offline? |
|---|---|
| App already open, signal drops (the shop) | Yes - edits queue and go on reconnect. The main case, and the one tested. |
| Standalone deployment, installed as a PWA, opened with no signal | Will, once a service worker caches the app. There is none yet. |
| Inside Home Assistant (ingress), opened with no signal | **No.** Home Assistant's own frontend must load first, and the companion app's web view is unlikely to support service workers. Accepted. |
| iOS Safari evicts site storage after weeks unused | Data re-syncs from the server (a snapshot); only unsynced edits are lost. Accepted. |
| The Pi restored from a Home Assistant backup | Edits made since the backup come back from whichever browsers hold them, on their next round. Nothing to do on any phone. |
| The add-on reinstalled with no backup | Each phone or computer that still has data asks once; "Send my copy" on each rebuilds the server from all of them. |

## As measured

2026-10-21, Chrome desktop, `.claude/tools/chrome-sync-check.mjs` - two headless Chromes on separate fresh
profiles, offline through the DevTools protocol's network emulation (what DevTools' Offline switch does) - two
clean runs on the final build, plus the user's own Chrome as a third profile. (The first runs failed only on
reconnect taking longer than the script waits, which is what added the 5 s retry and the `online` event.)

| Check | Result |
|---|---|
| A create on profile A, seen on B | By the time A had rendered it - the stream, not the 30 s round |
| A rename made on A while offline | Shown on A at once; chip "Offline · 1 waiting"; server and B unchanged for 3 s |
| A back online | B showed the rename within about 0.2 s; A's outbox empty |
| Both offline, A renames, B renames 1.5 s later; B reconnects first, then A | A, B and the server all end on B's rename; A's is in the log (4 ops for the row) and shown nowhere |
| Rows read from each profile's SQLite (copied out of OPFS) and the server's | Same name and the same `FieldClock` reading in all three; outboxes empty; both cursors at the server's newest seq |
| Four ingredients added as fast as Enter can be pressed, in the user's Chrome | All four saved and synced; no errors |

2026-10-21, a restore from backup in the running app, `.claude/tools/chrome-restore-check.mjs` - two headless
profiles kept open while the app was stopped, its SQLite replaced by a backup taken mid-run (`sqlite3 .backup`),
and the app started again:

| Check | Result |
|---|---|
| The backup at seq 14; then A adds an ingredient and renames one, B adds one (seqs 15-17) | Both converge |
| Restored; B was online throughout, A offline | B's next round got `HistoryLost`, pushed the three lost edits back - A's two with A's device id and clocks - and the server held all of them |
| B adds one more (seq 18); A comes back with cursor 17, which now names B's pushed-back op (different chain) | A got `HistoryLost`, queued nothing (the server had it all), showed B's addition |
| Rows in both profiles' SQLite and the server's | Same five ingredients; outboxes empty; both cursors at 18, the server's head |

2026-10-21, the server wiped twice in the running app, `.claude/tools/chrome-server-reset-check.mjs` - profile A in two
windows, profile B in one, every check passing:

| Check | Result |
|---|---|
| A holds two ingredients and B's, B holds a rename of A's still queued; wipe | Both A windows and B asked ("3 items"); chip "Paused · needs you"; the new server held only its seeds |
| A's second window sends | A's first window's dialog closed by itself; the server had A's three ingredients |
| B sends | B's later rename won on the server and in all three windows; both profiles' SQLite matched the server - same rows, outbox empty, cursor at the head, no question open |
| B adds one offline; wipe; A sends; B chooses clear | B asked "1 item" (the rest the server already had from A); the red second step; "Go back" changed nothing; confirmed, B's extra was gone from B and never reached the server, and both profiles' SQLite matched it |

A dialog on a phone-sized viewport: the text and both buttons fit without scrolling. A first run with A's second
window as a *tab* failed: Chrome throttled the hidden tab, whose rounds then ran minutes apart and whose chip
flapped to "Offline" each time its idle change stream dropped. Windows side by side, as the owner uses them, are
not throttled.

2026-10-21, after protocol 2 and the `SyncSchedule` split: `chrome-sync-check.mjs`, `chrome-restore-check.mjs` (a real
backup restored under both profiles) and `chrome-server-reset-check.mjs` (two wipes) all passed again unchanged.

The chip in the user's Chrome with a page's `fetch` patched: a `Pull` answered with gRPC status 13 showed red "Sync
problem" and logged `fail: ... Sync round failed` with the exception; a rejected `fetch` showed "Offline"; restored,
"Synced".

The in-process tests cover the same cases against real hosts over gRPC-Web: `Tests/Sync/SyncEndToEndTests.cs`
(convergence, the clock, a lost push answer and an accepted op both confirmed by the pull, deletes through the
applier, the server, a snapshot and a pull, more than one pull page, the change stream, overlapping writes in one
window, rejected ops and the browser refusing exactly what the server would) and `Tests/Sync/SyncRecoveryTests.cs`
(a second host started on the first's backup: cursor past the end, and seqs renumbered by another browser; an
accepted op lost to a restore; another device's edit given back as that device's; two browsers giving back the same
edit, logged once; an edit still queued when history is lost, sent once) and `Tests/Sync/ServerReplacedTests.cs` (a
new database: asking, not asking about a tombstone it never had, sending from two browsers in either order, clearing,
one answer only). `Tests/Sync/HookedServer.cs` lets a test edit between a round's push
and its pull, lose an answer, or cut the network mid-round - without it the outbox is always empty when a snapshot
arrives and the snapshot paths go untested. Each of these was checked by breaking the code it covers, and every break
failed at least one test: no outbox replay, no chain check, dropping accepted ops, no tombstone, `HasMore` always
false, no push-back; a given-back edit sent as this device's, a minted op id for it, queuing an edit already in the
outbox; and for a new database, the server logging a push aimed at another epoch, taking the snapshot without
asking, asking about tombstones of rows it never had, a second answer applying, a round that pushes while the
question is open, "clear" keeping the outbox, "send" forgetting to compare the next snapshot. The SyncLoop decisions
were broken the same way (a failing round kicked by notices, a dropped stream hiding "Sync problem", the refusal
classified as a failure).

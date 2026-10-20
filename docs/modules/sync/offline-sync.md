---
title: Offline sync
kind: reference
status: current
module: sync
verified: 2026-10-21
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
  - AshersLab.MealPlanner.Tests/Sync
  - AshersLab.MealPlanner.Tests/Domain
  - .claude/tools/chrome-sync-check.mjs
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
  a "rename" to the same name silently undoes a real rename made elsewhere. The page sends no op when the name
  is unchanged (`IngredientsPage.SaveRename`); every future editor must do the same.
- **A new synced entity is four places and two migrations**: its row class in `Shared.Data`, its
  `SyncedEntity<TRow>`, a line in `SyncedEntities.All`, its table in `SyncedEntities.BuildModel` - then a
  migration in *both* `Grpc` and `LocalData`. An entity missing from `All` is rejected by the server. Once real
  data exists it also needs a `SyncProtocol` bump (below).
- **Never `Update` a counter** (serves eaten, stock counts). Two devices adding one each would both write
  "previous + 1" and one would be lost. Counters get an `Increment` op kind, added with the first counter
  (slice 10).
- **`Seq` order relies on SQLite having one writer** (`Grpc/Data/Models/Sync/DbOp.cs`). A push numbers and
  commits its ops before the next push can number any, so a pull never sees seq N+1 without N. On Postgres a
  later seq could commit first and a pull would skip the earlier op forever. Revisit before moving.
- **A device whose clock is far ahead wins every conflict** until the others' clocks pass it. The clock
  (below) protects against a few minutes of skew, which is what phones have; nothing guards against a clock set
  a year ahead.

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

- **Ids are GUIDv7, minted by the device**, entity ids and op ids alike. Nothing waits on the server for an id,
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

Delete is in the rules and tested; nothing in the UI deletes yet (ingredient CRUD, slice 4).

## The server

`Grpc/Features/Sync/SyncService.cs`, mapped at `/gRPC/AshersLab.MealPlanner.Sync/`.

- **Push** applies a batch (at most 500) in one `BEGIN IMMEDIATE` transaction. Per op: an op id already logged
  answers `Duplicate` with its original seq; one that fails validation (unknown entity, bad clock, a clock naming
  another device, a field the entity does not have, an invalid name) answers `Rejected` with a reason and is
  **not** logged; anything else is applied and logged with a new seq, **whether or not it won its merge** -
  every browser must see the same ops to reach the same answer.
- **The log is append-only** (`Op` table): seq from SQLite's rowid, the op, its fields as JSON
  (`Shared.Data/Sync/OpFieldsJson.cs`, source-generated so trimming cannot drop it). The materialised tables
  (`Ingredient`, `FieldClock`) are what server-side reads will use.
- **Pull** returns ops after the browser's seq, ascending, at most 500 (200 by default), with `HasMore`. It
  returns a **snapshot** instead - every synced row with each field's clock, as of the newest seq, read in one
  transaction - when the browser has no cursor yet (a first load), when its cursor names a different
  **epoch**, or when its cursor is past the end of the log.
- **The epoch** (`SyncEpoch` table) is a GUID minted on the database's first start (`SyncStartup.cs`), not in a
  migration - seed data would give every database the same one. A server database that is wiped and started
  again has a new epoch, and every browser's cursor is recognised as meaningless. A restore from an older
  backup keeps its epoch; the "cursor past the end" check catches that.
- **Changes** is a server stream that only ever says "newest seq is N": on connect, after every push, and as a
  heartbeat every 25 s so a proxy (Home Assistant ingress) does not close an idle stream. gRPC-Web carries server
  streaming; it has no client or bidirectional streaming, which is why push and pull are unary.

## The browser

- **The outbox** (`Outbox` table) holds this browser's ops until the server answers for them. An op leaves it
  when a push answers for it - accepted, duplicate *or rejected*; a rejected op would be rejected forever - or
  when a pull brings it back, which covers a push whose answer was lost.
- **The cursor** (`SyncState`: epoch, pulled-through seq, this device's clock) is one row shared by every window.
- **A round** (`SyncRound.RunAsync`) pushes the whole outbox in batches of 200, then pulls until caught up.
  Every step commits on its own and repeating one is harmless, so a round that dies halfway loses nothing.
- **A snapshot is merged, not copied** (`LocalOps.ApplySnapshotAsync`): each field at its own clock, so an
  unsynced local edit newer than the server's value survives. If the snapshot comes from a different epoch, the
  browser first forgets every synced row (they are not in the new server database, and their ops left the
  outbox long ago), then replays its outbox on top and pushes it.
- **When a round runs** (`WASM/Sync/SyncLoop.cs`): on start; after every edit; when the change stream reports a
  seq past the cursor, or reconnects after a drop; on the browser's `online` event (`wwwroot/js/connectivity.js`);
  every 5 s while rounds are failing; and every 30 s regardless. The 5 s retry is not optional: an idle open
  stream can survive the network going away, so it cannot be relied on to notice the network coming back.
- **Every window runs its own loop.** Safe because every step is idempotent; two windows pushing the same outbox
  only do the work twice (seen: the server logged a second window's create exactly once).

**What the planner sees** is the chip at the right of the app bar (`WASM/Sync/SyncStatusChip.razor`), on every
page: "Synced", "Saving 2", "Offline · 3 waiting", or "Update needed". Tapping it syncs now. An edit made offline
shows in the list at once and waits in the outbox; nothing is lost by closing the tab.

## Protocol version

Every push, pull and stream carries `SyncProtocol.Current` as the browser was built
(`Contracts/.../SyncProtocol.cs`). The server refuses anything outside
[`OldestSupported`, `Current`] with `FailedPrecondition` and the detail `sync-protocol-unsupported`, before
reading the ops. The browser's answer is to reload once (guarded by `sessionStorage`) to fetch the new build -
its outbox is in SQLite, survives the reload, is migrated by the new build and then pushed. A second refusal in
the same session shows "Update needed" instead of reloading in a loop.

Bump `Current` when an entity's fields or their meaning change in a way an older browser would misread; raise
`OldestSupported` when the server can no longer accept the older shape. **While nothing is in production, wipe
both databases instead** - the version is 1 and nothing older exists.

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

The in-process tests (`Tests/Sync/SyncEndToEndTests.cs`) cover the same cases against the real host over
gRPC-Web, plus a lost push answer, rejected ops, a first load from a snapshot that keeps unsynced edits, a new
epoch, the change stream, and overlapping writes in one window.

---
title: Offline sync design
kind: plan
status: accepted
module: sync
opened: 2026-10-20
verified: 2026-10-20
code: []
---

# Offline sync design

How the browser and the server keep the same data when either side changes it, including while the
browser is offline. Expands the "Data" row of [the v1 scope](2026-10-v1-scope.md). Read before
touching any write path: **every user edit in this app goes through the op log**, there is no
direct "call the API to save" path.

## Why op-based and not row sync

Two people edit the same small dataset, occasionally at the same time, one of them often on a phone
in a shop with poor reception. Whole-row last-write-wins loses edits (she renames a meal, he changes
its serves, one change vanishes). CRDTs are more machinery than two users need. A log of field-level
operations with a server-assigned order is the smallest thing that gets both right.

## Shape

```
Browser                                              Server
┌──────────────────────────────┐                     ┌─────────────────────────────┐
│ UI ──edit──> OpWriter        │                     │ SyncService (gRPC)          │
│               ├ apply to local SQLite (same txn)   │  Push(ops) ─> apply in txn, │
│               └ append to Outbox                   │               assign Seq,   │
│ SyncLoop:                    │ ──Push(outbox)────> │               dedupe OpId   │
│   push outbox, then pull  <──┼── Pull(since Seq) ──│  Pull(since) ─> ops > Seq   │
│   apply remote ops locally   │ <─ Changes stream ──│  Changes() ─> "new Seq: N"  │
└──────────────────────────────┘                     └─────────────────────────────┘
```

- **Ids are client-generated GUIDv7.** Nothing waits on the server to assign an id, so an entity
  created offline can be referenced offline.
- **An op** is `{ OpId, DeviceId, Hlc, Entity, EntityId, Kind, Fields }` where `Kind` is
  `Create | Update | Delete | Increment`, and `Fields` is only the fields that changed.
- **Hybrid logical clock** (`Hlc`): wall time + counter + device id. Orders ops correctly when a
  phone's clock is a few minutes off, which a plain timestamp does not.
- **The local write and the outbox append are one SQLite transaction.** An edit is never applied
  without being queued, or queued without being applied.

## Server

- `Push` applies a batch in one transaction. Each op gets a monotonically increasing `Seq`. An
  `OpId` already applied is acknowledged and skipped — retries after a dropped connection are safe.
- The server stores the op log (`Seq`, op) **and** the materialised tables. The tables are what
  reads, costing and server jobs use; the log is what clients pull.
- `Pull(sinceSeq)` returns ops after `sinceSeq`, including the device's own (so a client can confirm
  its outbox landed and clear it).
- `Changes()` is a server stream that emits only "latest Seq is N". Server streaming works over
  gRPC-Web; client and bidirectional streaming do not, which is why push and pull are unary.
- A client that is too far behind (log compacted past its `sinceSeq`) gets a full snapshot instead.
  Log compaction is not needed in v1 — two users will take years to make a log worth compacting — but
  the snapshot path is also the first-load path, so it exists from slice 2.

## Merge rules

Decided per field type, applied identically on server and client (shared code, unit-tested with
truth tables).

| Case | Rule |
|---|---|
| Two updates to **different** fields of one entity | Both apply. This is the reason ops carry fields, not rows. |
| Two updates to the **same** field | Higher `Hlc` wins. |
| Counters (serves consumed, stock "running low" counts) | `Increment` ops — commutative, both apply. Never `Update` a counter. |
| Collections (a meal's ingredients, shopping list items, template choices) | Each member is its own entity with its own id. Adding on two devices merges. |
| Delete vs any update | Delete wins. Tombstone kept (`Deleted = true`), so a late update from an offline device cannot resurrect it. |
| Create of an id that exists | Treated as update of the provided fields. Only happens on retry. |
| Reference to a deleted entity (calendar entry → deleted meal) | The referencing entity survives and shows "meal deleted" — the planner fixes it. Do not cascade-delete user data on sync. |

Edit-wins-over-delete was considered for meals and rejected for v1: it needs undelete semantics in
the UI for a case two people will rarely hit.

## What is not synced through ops

- **Server-produced data** — budget figures, live prices — *is* synced, as ops the server writes
  with its own device id. The client never writes those entity types.
- **Computed values** — costs, shopping list lines, serves remaining — are never stored or synced.
  Both sides compute them from synced data with the same shared code.
- **Receipt files** — never stored anywhere. Parsed lines are ops like everything else.

## Schema versions

Once real data exists, the browser's SQLite and the server's SQLite migrate independently, and a
browser can be offline across a deploy. Every `Push`/`Pull` carries the client's schema version; the
server refuses a client it cannot read with a typed error, and the client responds by reloading to
the new app version (its outbox survives in local SQLite and is pushed after its own migration).
Before real data exists, wipe both instead.

## Where offline actually works

| Situation | Works offline? |
|---|---|
| App already open, signal drops (the shop) | Yes — edits queue, sync on reconnect. The main case. |
| Standalone deployment, installed as a PWA, opened with no signal | Yes, with a service worker caching the app. |
| Inside Home Assistant (ingress), opened with no signal | **No.** Home Assistant's own frontend must load first, and the HA companion app's web view is unlikely to support service workers. Accepted. |
| iOS Safari evicts site storage after weeks unused | Data re-syncs from the server; only unsynced edits are lost. Accepted. |

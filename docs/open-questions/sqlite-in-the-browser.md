---
title: SQLite in the browser
kind: investigation
status: open
module: sync
opened: 2026-10-20
verified: 2026-10-20
code: []
---

# SQLite in the browser

Can the Blazor WASM app keep an EF Core SQLite database that survives a reload, on Chrome desktop
**and** Safari on iOS, without hurting load time on a phone? The offline design
([offline sync](../work/2026-10-offline-sync.md)) assumes yes. Settled by slice 1 of
[the v1 scope](../work/2026-10-v1-scope.md).

## What is known

- EF Core's SQLite provider runs in Blazor WASM against a native `e_sqlite3` compiled to WASM. That
  needs native linking in the build (`<WasmBuildNative>true</WasmBuildNative>`, the `wasm-tools`
  workload) and a matching `SQLitePCLRaw` bundle.
- The database it opens lives in the WASM runtime's in-memory filesystem. **Without extra work it is
  gone on reload.** Persistence means either copying the file to and from IndexedDB / OPFS around
  writes, or a VFS that writes to OPFS directly. Libraries exist for both; their maturity on .NET 10
  is the open part.
- OPFS is available in Safari on iOS 17+. Safari may evict script-written storage for sites not used
  for weeks; the server copy makes that a re-sync, not a loss (unsynced edits excepted).

## What to find out

1. Which approach (library or hand-rolled copy-to-OPFS) works on .NET 10 + Safari iOS + Chrome.
2. The cost: download size of the native SQLite WASM, and time to open the DB on a mid-range phone.
3. Whether the save-after-write path can lose the last write if the tab is closed immediately (it
   must not lose an outbox entry — test closing the tab right after an edit).
4. That it builds in the Dockerfile under cross-compilation, not just in Rider.

## If it does not work

Fall back to IndexedDB as a document store (one object store per entity, JSON values) behind the
same repository interface. Loses SQL querying in the browser; costing and shopping-list computation
then run over in-memory collections, which at this data size (hundreds of rows) is fine. Record the
answer in a `docs/modules/sync/` doc and delete this file.

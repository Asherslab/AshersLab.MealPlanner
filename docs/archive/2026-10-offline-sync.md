---
title: Offline sync design
kind: plan
status: folded
module: sync
opened: 2026-10-20
closed: 2026-10-21
verified: 2026-10-21
folded_into:
  - docs/modules/sync/offline-sync.md
  - docs/modules/sync/browser-database.md
  - docs/modules/infrastructure/hosting.md
code: []
---

# Offline sync design

The design for keeping every browser and the server on the same data, agreed on 2026-10-20 and built in slice 2
of [the v1 scope](../work/2026-10-v1-scope.md) (ingredient create and rename). How it works now is
[offline sync](../modules/sync/offline-sync.md); this file keeps only why it is shaped this way and what
building it turned up.

## Why ops and not row sync

Two people edit the same small dataset, occasionally at the same time, one of them often on a phone in a shop
with poor reception. Whole-row last-write-wins loses edits: one person renames a meal while the other changes
its serves, and one change vanishes. CRDTs are more machinery than two users need. A log of field-level
operations with a server-assigned order is the smallest thing that gets both right.

## Rejected

- **Edit wins over delete**, for meals. It needs undelete semantics in the UI for a case two people will
  rarely hit. Delete wins, with a tombstone.
- **A plain timestamp for ordering.** A phone whose clock runs a few minutes slow would lose every conflict to
  edits it had already seen. Hence the hybrid logical clock.
- **The epoch as seed data in a migration.** Every server database would share it, and a wiped server would
  look like the same database to every browser. It is minted on first start instead.

## Decided while building

- **One applier for both databases.** The design said the merge rules were shared code; building it showed
  the code that applies a merge to the tables (find the row, load the field clocks, set fields, record clocks)
  was the same on both sides too, so it went into a new project, `Shared.Data`, with the synced tables'
  model. The browser's and server's synced tables are now defined once.
- **`Update` or `Delete` of an unseen id creates it.** Not in the design's table; needed so the result does not
  depend on arrival order (a delete arriving before the create on some replica).
- **Rejected ops leave the outbox.** The design did not say what happens to an op the server cannot accept.
  Kept, it would be pushed and refused forever; it is dropped and the chip says the server refused it.
- **The browser's round lives in `LocalData`**, not the WASM app, so the tests run it against the real host.

## What building it exposed

- **Two contexts in one window break each other** (seen in Chrome, two quick adds while a round ran): every
  context shares SqliteWasmBlazor's one connection object. Fixed with `SerialLocalDbContextFactory`; a test that
  reproduces it had first passed *with* the bug, because in-memory SQLite never interleaves - its test browser
  now yields before every statement.
- **An open change stream can outlive the network going away.** After DevTools-protocol offline and back, the
  stream did not error, so nothing prompted a round for up to 30 s. Fixed with a 5 s retry while rounds fail and
  the browser's `online` event.
- **A `MudListItem` swallows Space and Enter**, so a rename box inside one lost every space and never submitted.
  The list is plain rows.
- **Clearing the "new ingredient" box after the write** wiped, or appended to, the next name typed while the
  write ran. It is cleared before.

---
title: The catalogue
kind: reference
status: current
module: catalogue
verified: 2026-10-21
code:
  - AshersLab.MealPlanner.Shared.Domain/Names.cs
  - AshersLab.MealPlanner.Shared.Domain/Sync/OpRules.cs
  - AshersLab.MealPlanner.Shared.Domain/Stores
  - AshersLab.MealPlanner.Shared.Data/Stores
  - AshersLab.MealPlanner.Grpc/Features/Catalogue/CatalogueSeed.cs
  - AshersLab.MealPlanner.Grpc/Features/Sync/SyncStartup.cs
  - AshersLab.MealPlanner.WASM/Features/Stores
  - AshersLab.MealPlanner.WASM/Components/NameList.razor
  - AshersLab.MealPlanner.WASM/Data/LocalDataPage.cs
  - AshersLab.MealPlanner.Tests/Catalogue
  - .claude/tools/chrome-catalogue-check.mjs
---

# The catalogue

The things everything else is built from: stores, and (as they land) categories and ingredients. How they are
seeded, what a valid one is, and what deleting one does. Read before adding a field to any of them, or anything
that refers to them. Every edit to them is an op like any other - [offline sync](../sync/offline-sync.md) is the
mechanism; this doc is what the catalogue does with it.

## Traps first

- **Seed data is written as ops at startup, never by a migration** (`Grpc/Features/Catalogue/CatalogueSeed.cs`,
  called from `SyncStartup.InitialiseAsync`). A row a migration inserts has no op and no field clocks: no browser
  would ever pull it, and a snapshot leaves out fields no op set. A new seed is a line in `CatalogueSeed.All`.
- **No foreign keys between synced tables.** Ops arrive in any order, so a row can name another this database has
  not received yet, or one deleted on another device meanwhile. A constraint would reject the op on one side and
  the databases would diverge. A reference to a missing or deleted row is shown as gone.
- **Uniqueness is the page's warning, not a rule.** The page refuses a second "Costco" (`Names.Same`: case and
  spacing ignored), but two phones offline can each add one and both survive the merge. The planner sees two and
  deletes one. A check against existing rows cannot be in the op rules: it depends on what each database holds,
  and the browser and server must refuse exactly the same ops ([offline sync](../sync/offline-sync.md), traps).

## Seeds

| Entity | Seeded |
|---|---|
| Store | Woolworths, Costco |

- **Written once per database**: a seed is written when the log holds no op for its id. A renamed seed keeps its
  new name and a deleted one stays deleted across restarts.
- **Fixed ids** (`00000000-5eed-7000-8000-...`), so every database's Woolworths is the same row and a browser's
  queued edit to it survives the server database being replaced.
- **Clock zero, from a device id no browser has** (`CatalogueSeed.Clock`). A seed is a default: any edit anyone
  makes beats it, even one made before the seed was written (a server restored from a backup older than the seed
  gets it again at startup, and a browser's rename pushed back still wins). Pulling a seed never moves a browser's
  clock forward.
- A new database's log therefore starts with the seeds, not empty; a browser's first op is seq 3.
  `MealPlannerHost.SeedOps` is that count for the tests.

## Names

`Shared.Domain/Names.cs`, the one rule for every named thing: trimmed, inner whitespace collapsed (" the  butcher "
is saved as "the butcher"), 1-100 characters. The op rules refuse a name that is not already normalised, so the
page normalises before it writes. Each entity's op rules (`StoreRules`, `IngredientRules`) are `OpRules.Problem` -
the shape checks every entity shares - plus its own per-field checks.

## Stores

`Store (Id, Name, Deleted)`. Only a name: what a store sells and for how much is the product slice's.

**What the planner sees** (`/stores`, "Stores" in the menu): the stores by name; a "New store" box (Enter adds it);
per row a pencil (rename in place) and a bin. The bin asks "Delete Costco? It goes from this list on every device."
- one more tap. A store is deleted as a tombstone ([offline sync](../sync/offline-sync.md), merge rules).

## Deleting something referenced

Decided 2026-10-21. **The page refuses to delete anything still in use, and says what uses it**; nothing cascades
and nothing is archived.

- Cascading is not possible: a delete is one op on one row, and the ops that would change the referencing rows
  could not be made atomic with it on every device.
- Archiving would add a state every list and picker must filter, for no gain over a tombstone - which already
  keeps the row for history.
- The refusal is the page's (`NameList`'s `UsedBy`), from this browser's data. Two devices can still race - one
  deletes a store offline while the other makes it an ingredient's usual store - and then the reference points
  at a tombstone and is shown as gone.

## The pages

`WASM/Data/LocalDataPage.cs` is the base of every page that edits synced data: it starts the browser database
after the first render, reloads on another window's or a sync round's change, and its `WriteAsync` is the only
write path (`LocalOps.WriteAsync`, then a sync kick). `LocalDataGate` shows the loading and failed states.
`Components/NameList.razor` is the add / rename / delete (and reorder) list stores and categories share.

The menu is a drawer (`Layout/MainLayout.razor`): open beside the page from the `Md` breakpoint up, behind the
menu button on a phone, closing when a page is picked.

## As seen

2026-10-21, `.claude/tools/chrome-catalogue-check.mjs`, two headless Chromes on fresh profiles against the running
app: a new profile has the seeded stores; a store added on A (typed with stray spaces) shows normalised on A and B;
a duplicate ("costco") is refused with "There is already a store called costco."; a rename on A reaches B and the
server; B deletes it after the confirmation and A drops it; both profiles' SQLite (copied out of OPFS) and the
server's hold the same row and tombstone, outboxes empty. A phone-sized (390 px) screenshot shows the list with the
drawer closed. Also by hand in Chrome desktop: add, the duplicate message, rename, the delete dialog.

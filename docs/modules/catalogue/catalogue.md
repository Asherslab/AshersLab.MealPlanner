---
title: The catalogue
kind: reference
status: current
module: catalogue
verified: 2026-10-21
code:
  - AshersLab.MealPlanner.Shared.Domain/Names.cs
  - AshersLab.MealPlanner.Shared.Domain/Sync/OpRules.cs
  - AshersLab.MealPlanner.Shared.Domain/ListPosition.cs
  - AshersLab.MealPlanner.Shared.Domain/Stores
  - AshersLab.MealPlanner.Shared.Domain/Categories
  - AshersLab.MealPlanner.Shared.Data/Stores
  - AshersLab.MealPlanner.Shared.Data/Categories
  - AshersLab.MealPlanner.WASM/Features/Categories
  - AshersLab.MealPlanner.Tests/Domain/ListPositionTests.cs
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
| Category | Meat, Dairy, Fruit, Vegetables, Carbs/sides, Pantry, Fridge, Freezer, Household - the planner's spreadsheet's shopping categories, in its order (positions 1-9) |

A database that already existed when a seed was added gets it at its next start: the categories were added to a
running dev database that way (seqs 12-20, after the stores and the ops between).

- **Written once per database**: a seed is written when the log holds no op for its id. A renamed seed keeps its
  new name and a deleted one stays deleted across restarts.
- **Fixed ids** (`00000000-5eed-7000-8000-...`), so every database's Woolworths is the same row and a browser's
  queued edit to it survives the server database being replaced.
- **Clock zero, from a device id no browser has** (`CatalogueSeed.Clock`). A seed is a default: any edit anyone
  makes beats it, even one made before the seed was written (a server restored from a backup older than the seed
  gets it again at startup, and a browser's rename pushed back still wins). Pulling a seed never moves a browser's
  clock forward.
- A new database's log therefore starts with the seeds, not empty; a browser's first op follows them.
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

## Categories

`Category (Id, Name, Position, Deleted)`. Groups the ingredients list - and later the shopping list - in the
household's order, which is the order they walk the shop.

**Order is a position per category, not a swap** (`Shared.Domain/ListPosition.cs`, tests in
`Tests/Domain/ListPositionTests.cs`). Moving a category one place writes one op: its own new position, halfway
between its new neighbours (or one past the end). Two phones moving *different* categories offline both keep their
moves; swapping two positions would write both rows, and the later device's swap would silently undo part of the
earlier one. Two phones moving the *same* category: the later move wins, like any field.

- A new category goes after the last (`ListPosition.AtEnd`: the largest position, floored, plus one).
- Equal positions happen (two phones each add one at the end offline). The list sorts them by name, then id
  (`CategoryEntity.InOrder`), so every device shows the same order; a move that needs a gap between two equal
  positions renumbers the list 1..n in its new order and writes only the categories whose number changed.
- Halving one gap runs out of decimal digits after about 90 moves into it; that also renumbers.
- **Sort in memory, never in SQL.** EF's SQLite providers store `decimal` as text and will not `ORDER BY` it - and
  they do not even agree on the text: the server writes `1.0`, the browser `1`. Compare positions as numbers.

**What the planner sees** (`/categories`, "Categories" in the menu): the categories in order, under "The
ingredients and the shopping list are grouped in this order."; a "New category" box; per row up and down arrows
(one tap moves one place; the top's up and the bottom's down are greyed), a pencil and a bin.

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

Categories, same script: a new profile has all nine seeds; a category added on A goes at the end and shows on B;
two taps of its up arrow move it above the two before it, on A and B; both profiles offline, A moves the second
category to the top while B moves the last up one - after reconnecting both moves stand, in the same order on both;
rename and delete reach the other profile; names and positions (compared as numbers) match in both profiles' SQLite
and the server's.

---
title: The catalogue
kind: reference
status: current
module: catalogue
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.Shared.Domain/Names.cs
  - AshersLab.MealPlanner.Shared.Domain/Sync/OpRules.cs
  - AshersLab.MealPlanner.Shared.Domain/ListPosition.cs
  - AshersLab.MealPlanner.Shared.Domain/Stores
  - AshersLab.MealPlanner.Shared.Domain/Categories
  - AshersLab.MealPlanner.WASM/Layout
  - AshersLab.MealPlanner.WASM/Data/LocalDataGate.razor
  - AshersLab.MealPlanner.Shared.Domain/Ingredients
  - AshersLab.MealPlanner.Shared.Domain/Sync/OpValues.cs
  - AshersLab.MealPlanner.Shared.Data/Ingredients
  - AshersLab.MealPlanner.WASM/Features/Ingredients
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

The things everything else is built from: stores, shopping categories and ingredients. How they are
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
| MealGroup | Beef, Chicken & egg, Fish, Other, Individual, Side, Snack - the spreadsheet's meal-ideas columns ([meals](../meals/meals.md)) |

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

**What the planner sees** (`/stores`; on a phone More → Settings → Stores): the stores by name; a "New store" box (Enter adds it);
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

**What the planner sees** (`/categories`; on a phone More → Settings → Categories): the categories in order, under "The
ingredients and the shopping list are grouped in this order."; a "New category" box; per row up and down arrows
(one tap moves one place; the top's up and the bottom's down are greyed), a pencil and a bin.

## Ingredients

`Ingredient (Id, Name, BaseUnit, CategoryId, UsualStoreId, IsStaple, UnitGuessed, StapleStock, Deleted)`. Rules in
`Shared.Domain/Ingredients/IngredientRules.cs`; the field formats every entity shares are `Sync/OpValues.cs`.

| Field | On the wire | Notes |
|---|---|---|
| `Name` | normalised text | required on create |
| `BaseUnit` | `Gram`, `Millilitre` or `Each` - the enum member's **name** | required on create; never rename a member. Shown as g / ml / each. |
| `CategoryId`, `UsualStoreId` | lower-case hyphenated GUID, or null for none | not checked against existing rows - see the traps |
| `IsStaple` | `true` or `false` | staples are costed by quantity like anything else and left off the shopping list |
| `UnitGuessed` | `true` or `false` | `true` only from the spreadsheet import (removed 2026-10-23; ingredients it made may still carry it); saving the editor sends `false` |
| `StapleStock` | `Have`, `RunningLow` or `Out` - by name | a staple's stock, set one tap at a time on [the pantry](../pantry/pantry.md), alone in its op; `Have` until someone says otherwise. The editor never sends it |

- **A create needs a unit, and the editor has no default.** Every quantity of the ingredient - in a recipe, in a
  pack - is counted in it, so a silent "grams" on a cucumber would make every later number wrong without anyone
  noticing. **Changing the unit later converts nothing**: its products' pack sizes and its meals' quantities are
  then read in the new unit, and mean the wrong thing until someone fixes them.
- **An edit sends only the fields that changed** (`IngredientDraft.FieldsChangedFrom`, tested): two phones changing
  different fields of one ingredient offline both keep their change; the same field, the later edit wins.
- A reference to a category or store that is gone shows as "No category" (listed last) or "store removed", and the
  editor's picker shows none. Saving the editor without picking leaves the reference as it is.
- Rows from before the unit existed read as `Gram` (the migrations' column default, `AddIngredientDetails`); the
  column is text, not the enum's number, so it reads the same as the log.

**What the planner sees** (`/ingredients`): one box, "New ingredient, or search". Typing filters the list as she
types; Add (or Enter) opens the editor with the name filled in. The list is grouped under category headings in the
categories' order, "No category" last; each row is the name, a "Staple" chip when it is one, an orange "Check unit"
chip while its unit is the import's guess, and "counted in each · Costco" beneath. Saving the editor of a guessed
one - even unchanged - sends only `UnitGuessed = false`, which takes the chip off everywhere. Tapping a row opens the same editor. The editor (full screen on a phone): Name; "Counted in" -
Grams (g) / Millilitres (ml) / Each, none chosen for a new one, and Save says "Choose what it is counted in." until
one is; Category and Usual store as the phone's own pickers (native `<select>`s, not MudSelect - bigger and
one-handed); a Staple switch with what it means. Delete is in the editor, with a confirmation. Adding a fully
described ingredient: type the name, Add, three taps (unit, category, store), Save.

## Deleting something referenced

Decided 2026-10-21. **The page refuses to delete anything still in use, and says what uses it**; nothing cascades
and nothing is archived.

- Cascading is not possible: a delete is one op on one row, and the ops that would change the referencing rows
  could not be made atomic with it on every device.
- Archiving would add a state every list and picker must filter, for no gain over a tombstone - which already
  keeps the row for history.
- The refusal is the page's (`NameList`'s `UsedBy`, filled by the stores and categories pages from live
  ingredients), from this browser's data. It names what uses it: "Fruit is used by 1 ingredient: cucumber. Move them
  to another category first." Two devices can still race - one deletes a category offline while the other puts an
  ingredient in it - and then the reference points at a tombstone and is shown as gone
  (`CatalogueSyncTests.An_ingredient_whose_category_was_deleted_elsewhere_survives_pointing_at_the_tombstone`).
- **Products refer to stores and ingredients** ([products and prices](products-and-prices.md)). A store that sells a
  product is refused like one an ingredient uses - the message names both ("Costco is used by 1 ingredient: cucumber,
  and 2 products: ..."; `NameListItem.UsedByProducts`). The ingredient editor's Delete is refused while a product is
  bought as it, naming the products.
- **Meals are made of ingredients** ([meals](../meals/meals.md)): the ingredient editor's Delete is also refused while
  a live meal has it as a line, naming both ("onion is used by 1 product: ..., and 1 meal: cottage pie. Delete the
  products on the Prices page, and take it out of the meals, first.").
- **Meals refer to meal groups** ([meals](../meals/meals.md)); a group a meal is in is refused the same way
  (`NameList`'s `UsedByNoun`).

## The pages

`WASM/Data/LocalDataPage.cs` is the base of every page that edits synced data: it starts the browser database
after the first render, reloads on another window's or a sync round's change, and its `WriteAsync` is the only
write path (`LocalOps.WriteAsync`, then a sync kick). `LocalDataGate` shows the loading and failed states, and the reset for a copy that cannot be opened
([browser database](../sync/browser-database.md)).

**Loading is the page's own top sheet, not a progress bar.** Until a page's first read lands, the gate shows the head
band the page's top sheet will have over blank paper (`local-data-loading`, inert); the rows fill in under a band that
does not move. Measured 2026-10-23 in Chrome, a page change reads in 75-200 ms (Calendar about 450), so the progress
bar that used to stand there flashed for 40-60 ms on every page change. Only a slow read - a page's first visit after
the app starts can take seconds - gets a thin line under the band, and only after 500 ms. So a new page:

- passes the gate its top sheet's `Title` and `Subtitle` (a constant both use), or, for a band built from the data,
  `Head` - the same fragment the sheet uses, which must render before the data is read and hold a data line blank;
  Calendar and Shopping repeat what their band said on the last visit this session, so the band is the right height;
- puts any wrapper that sets the page's width (`.today`, `.shop`, `.pad-column`) *outside* the gate, so the stand-in
  sheet is the real one's width; and styles the band through that wrapper (`.shop ::deep .shop-head …`), since the
  stand-in is the gate's element, not the page's;
- leaves the stand-in's copy of the band without `data-testid`s (`Id(...)` in Calendar and Shopping), so a check never
  clicks an inert button.

Seen with the CPU slowed sixfold, the band's box is the same in both states on every page at 390 and 1280 px, except a
first visit to Calendar or Shopping on a phone, before there is a last visit to repeat.
`Components/NameList.razor` is the add / rename / delete (and reorder) list stores and categories share.

**Every menu reads one list, `Layout/NavModel.cs`** - route, title, icon, group, parent and the one line More and
Settings show. Add or move a page there, not in a menu. On a phone (below MudBlazor's `Md`, 960 px) the bottom bar
(`Layout/BottomNav.razor`) holds Today, Plan, Shop, Meals and More, one tap each; there is no drawer. More lists
Pantry, Receipts, Prices, Ingredients and Plans, then Settings, which holds the lists set up once a season - People,
Who can sign in, Slots of the day, Meal groups, Categories, Stores - and Diagnostics last. So a store is three taps
away (More, Settings, Stores), and every page reached that way has a back arrow to its parent in the app bar
(`app-back`). From `Md` up there is no bottom bar: the drawer (`Layout/MainLayout.razor`) sits open with the four
daily pages, a Kitchen group, and Settings. Which of the two shows is CSS (`MainLayout.razor.css`,
`BottomNav.razor.css`), so the first paint is already right.

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

Ingredients, same script: added on A as each / Vegetables / Costco, B lists it under Vegetables with "counted in each
· Costco" and the server row holds every field; the editor refuses a duplicate name and a missing unit, saying both;
staple and Woolworths set on A show on B (chip and detail); both offline, A changes the unit to grams while B moves it
to Fruit - after reconnecting both profiles show Fruit and grams, and all three databases hold the same row; B cannot
delete Fruit (told the cucumber uses it) nor Woolworths; B deletes the ingredient after the confirmation, A drops it,
the server keeps the tombstone, and Fruit's bin then asks to confirm. Phone-sized screenshots of the list and the
full-screen editor. By hand in Chrome desktop: the editor, the unit error, picking from both selects, the in-use
message on Categories, and a delete. `.claude/tools/chrome-sync-check.mjs` (add and rename now through the editor)
passes as before.

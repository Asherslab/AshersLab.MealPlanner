---
title: Meals
kind: reference
status: current
module: meals
verified: 2026-10-21
code:
  - AshersLab.MealPlanner.Shared.Domain/Meals
  - AshersLab.MealPlanner.Shared.Data/Meals
  - AshersLab.MealPlanner.WASM/Features/Meals
  - AshersLab.MealPlanner.Grpc/Features/Catalogue/CatalogueSeed.cs
  - AshersLab.MealPlanner.Tests/Meals
  - AshersLab.MealPlanner.Tests/Domain/MealRulesTests.cs
  - AshersLab.MealPlanner.Tests/Domain/MealCostingTests.cs
  - AshersLab.MealPlanner.Tests/Domain/MealIngredientRulesTests.cs
  - AshersLab.MealPlanner.Shared.Domain/Ingredients/Quantity.cs
  - AshersLab.MealPlanner.WASM/Features/Ingredients/IngredientEditor.razor
  - AshersLab.MealPlanner.WASM/Components/NameList.razor
  - .claude/tools/chrome-meals-check.mjs
---

# Meals

What the household eats, how it is grouped, what goes into it and what it costs. Read before touching meals, meal
groups, a meal's ingredients, or anything that costs a meal. Every edit is an op, as [offline sync](../sync/offline-sync.md) describes; the ingredients a
meal is made of are [the catalogue](../catalogue/catalogue.md)'s.

## Meal groups

`MealGroup (Id, Name, Position, Deleted)` - the columns of the planner's "meal ideas": Beef, Chicken & egg, Fish,
Other, Individual, Side, Snack. Rules in `Shared.Domain/Meals/MealGroupRules.cs`.

They behave exactly like [shopping categories](../catalogue/catalogue.md#categories): a hand-set position per group
(`ListPosition`), so two phones moving different groups offline both keep their moves; seeded as server ops at clock
zero with fixed ids (`CatalogueSeed.MealGroups`, `00000000-5eed-7000-8000-000000000301` onwards), so a renamed or
deleted seed stays that way. Sort in memory, never in SQL (`MealGroupEntity.InOrder`).

**What the planner sees** (`/meal-groups`, "Meal groups" in the menu): the groups in order under "The meals are
grouped in this order."; a "New meal group" box; per row up and down arrows, a pencil and a bin - the same list as
Stores and Categories (`Components/NameList.razor`).

A group a meal is in cannot be deleted - the page names the meals ("Beef is used by 1 meal: bolognese. Move them to
another group first."), the rule of [the catalogue](../catalogue/catalogue.md#deleting-something-referenced).

## Meals

`Meal (Id, Name, GroupId, Kind, ServesPerBatch, FixedCostCents, Notes, Deleted)`. Rules in
`Shared.Domain/Meals/MealRules.cs`; an edit sends only what changed (`MealDraft.FieldsChangedFrom`, tested).

| Field | On the wire | Notes |
|---|---|---|
| `Name` | normalised text | required on create |
| `GroupId` | GUID or null | not a foreign key; a group that is gone shows as "No group" |
| `Kind` | `Cooked`, `Simple`, `Leftovers`, `NoCost` or `FixedCost` - by name | required on create; never rename a member |
| `ServesPerBatch` | whole number, plain digits (`OpValues.FormatInt`), 1-99 | required on create; 1 for a kind with no batch |
| `FixedCostCents` | whole cents or null | the takeaway's price, all up |
| `Notes` | trimmed text up to 2000, or null; line breaks kept | |

What each kind means, and which fields the editor asks for:

| Kind | Planner sees | Asks for |
|---|---|---|
| `Cooked` | "Cooked in a batch - made from ingredients, several serves at once" | serves a batch makes (no default) |
| `Simple` | "Single serve - made from ingredients, one serve at a time" | nothing more |
| `Leftovers` | "Leftovers - eats a serve of something cooked earlier" | nothing more |
| `NoCost` | "No cost - eating at someone else's" | nothing more |
| `FixedCost` | "Fixed cost - a set price, not ingredients: takeaway" | what it costs all up, and how many it feeds |

- **Serves has no default for a new batch.** A silent "1" would make every per-serve cost four times too high
  without anyone noticing, so Save says "How many serves does a batch make?" until it is typed.
- **Changing the kind clears nothing.** A meal switched from fixed cost to cooked keeps its price and serves in the
  row, so switching back finds them; costing reads only the fields its kind uses. A kind with no batch sends no
  serves at all - the row keeps whatever it had.
- **The op rules cannot require a fixed-cost meal's amount**: an update carries only the fields that changed, so no
  single op sees both `Kind` and `FixedCostCents`. The editor refuses to save one without it; a fixed-cost meal that
  still has none (two phones racing) costs as incomplete.

**What the planner sees** (`/meals`, "Meals" in the menu): one box, "New meal, or search" - typing filters, Add (or
Enter) opens the editor with the name filled in. Meals are listed under their group in the groups' order, "No group"
last, each with "makes 4 serves", "single serve", "leftovers", "no cost" or "$45.00 · feeds 3" beneath. Tapping one
opens its page (`/meals/{id}`): the name, "Beef · makes 4 serves", the notes, and Edit. The editor (full screen on a
phone) is Name, Group (the phone's own picker), the kind as five radio buttons each with what it means, then the
fields that kind needs, then Notes; Delete is in it, with a confirmation, and goes back to the list. Saving a new meal
opens its page. Adding a batch meal: type the name, Add, pick the group, type the serves, Save - two taps and two
fields. A page left open on a meal deleted elsewhere says "This meal is not here".

## A meal's ingredients

`MealIngredient (Id, MealId, IngredientId, Quantity, Note, Deleted)` - one row per ingredient of a meal. Rules in
`Shared.Domain/Meals/MealIngredientRules.cs`.

- **Rows, not a list on the meal.** A list in one field would merge last-writer-wins: two phones each adding an
  ingredient offline would lose one. As rows, both survive (`MealSyncTests.Ingredients_added_on_two_browsers_offline_*`).
- **`Quantity` is nullable on purpose**: "not entered yet" is a real state - the spreadsheet import (slice 7) makes
  every one of them - and the meal's cost says it is incomplete until it is entered. In the ingredient's base unit
  (`OpValues.FormatDecimal`), read in whatever unit the ingredient has now, like a pack size. Typed the way a pack size
  is (`Ingredients/Quantity.cs`, shared with `PackSize`): `500g`, `1.5kg`, `2`, `2x250g`; the wrong kind of unit is
  refused, never converted.
- For a batch meal the quantity is what **a batch** uses; for a single-serve meal, what **one serve** uses. The page
  says which above the list.
- `Note` is normalised text up to 200 ("browned", "for the sauce"), or null.
- Lines list in the order they were added (GUIDv7 ids). The same ingredient may appear twice (cheese in the sauce and
  on top).
- **Deleting a meal deletes its lines too**, one op each from the page (`MealPage.Edit`): nothing cascades on sync. A
  line added elsewhere meanwhile is left behind, shown nowhere and costing nothing.
- **A line whose ingredient is gone** (deleted on another device while this one added it) survives, shows as
  "ingredient removed" and costs as unpriced. The ingredient editor refuses to delete an ingredient a meal uses,
  naming the meals ([the catalogue](../catalogue/catalogue.md#deleting-something-referenced)).
- **Changing a meal's kind keeps its lines.** A takeaway that used to be cooked says "2 ingredients are kept from
  before, and not used while it is fixed cost."

## What a meal costs

`MealCosting.Cost` (`Shared.Domain/Meals/MealCosting.cs`, tested in `Tests/Domain/MealCostingTests.cs`) - steps 2 and
3 of the scope's "How a cost is computed". `MealCostBook` (`Shared.Data/Meals`) gathers its inputs from either
database in one read per table, so the meals list costs fifty meals without fifty queries. Nothing here is stored or
synced: each device computes it from its own data.

**Trap: an incomplete cost is never a smaller number.** Any line with no quantity, or whose ingredient is unpriced or
gone, makes the cost incomplete: `Cost(Cents, Missing)` is shown as "from $6.25 + 1 unpriced", never "$6.25". A meal
made from ingredients that has none yet has no cost at all ("No ingredients yet"), never $0.

| Kind | A batch | A serve |
|---|---|---|
| `Cooked` | Σ lines | batch ÷ `ServesPerBatch` |
| `Simple` | Σ lines (one serve's) | = the batch |
| `FixedCost` | the typed amount (incomplete when none) | amount ÷ the serves it feeds |
| `NoCost` | $0, complete | $0 |
| `Leftovers` | none of its own - the batch it eats from was paid for (slice 10 links them) | none |

- **A line** is quantity × the ingredient's price per unit ([products and prices](../catalogue/products-and-prices.md#an-ingredients-price):
  usual store first), rounded once to whole cents, half away from zero (`UnitPrice.CostOf`).
- **The batch is the sum of the rounded lines**, not the exact sum rounded once. The planner sees the lines and the
  batch on one page and they must add up; the difference is at most half a cent a line.
- **A serve is the batch divided once**, rounded half away from zero: $7.75 over 4 is $1.94 (193.75 c). An incomplete
  batch gives an incomplete serve with the same count.
- Kinds not made from ingredients ignore the lines they kept.

**What the planner sees**: on the meals list, beneath each meal, "makes 4 serves · $1.94 a serve", "makes 4 serves ·
from $1.56 a serve + 1 unpriced", "single serve · no ingredients yet", "$45.00 · feeds 3 · $15.00 a serve". On the
meal's page, a box with "A batch $7.75 · A serve, of 4 $1.94" (just "A serve" for a single-serve meal, "All up" for a
takeaway, "Costs nothing", or for leftovers "that batch is what it costs"); then "Ingredients, for a batch" with "Add
ingredient", and per line the name, "500 g · browned · $12.50/kg" (or "no quantity yet") and its cost on the right -
"$6.25", or "how much?" / "unpriced" in orange.

**Adding an ingredient** (`MealIngredientSheet`, full screen on a phone): tap "Add ingredient" - the search box is
focused with the keyboard up; type a few letters, tap the ingredient - the "How much a batch uses (g)" box takes focus,
showing what it read ("= 1.5 kg") as it is typed; Enter saves. Leaving it empty is allowed and says so. An ingredient
not in the catalogue yet is added on the Ingredients page first (the sheet says so). Tapping a line opens the same
sheet to change the amount or note, or Remove it (no confirmation: re-adding is as quick). Both boxes focus themselves
after the dialog opens, as the price sheet's does, because the dialog's focus trap overrides `AutoFocus`.

## As seen

2026-10-21, `.claude/tools/chrome-meals-check.mjs`, two headless Chromes on fresh profiles against the running app:
a new profile has the seven seeded groups in the spreadsheet's order; a group added on A is last on B; moved up one on
A, second to last on B; renamed on A, renamed on B; the server and both profiles' SQLite hold the same row and the
same number of groups, outboxes empty; B deletes it and A drops it. A phone-sized screenshot of the list.

Meals, same script: A adds one under Beef and is refused saving with no serves ("How many serves does a batch
make?"); with 4 it saves and A lands on its page, "Beef · makes 4 serves", notes on two lines; B lists it under Beef;
B makes it fixed cost, is refused with no amount ("Enter the price."), saves $45 feeding 3, and A's open page shows
"Beef · $45.00 · feeds 3"; A cannot delete Beef; the server and both profiles' SQLite hold the same row (4500 c, the
notes with their line break), outboxes empty - two ops in the server's log, the second carrying only `Kind`,
`ServesPerBatch` and `FixedCostCents`; B deletes it and returns to the list, and A's open page says the meal is not
here. Phone-sized screenshots of the list, the page and the editor.

Ingredients and cost, same script: mince priced at $24.99 for 8x250g at Costco, an onion with no price; a new batch
meal of 4 says "No ingredients yet"; adding 500g of mince (search focused on open, quantity focused after the tap,
Enter saved) shows the line "500 g · browned · $12.50/kg", $6.25, the batch $6.25 and a serve $1.56; an onion with
no quantity makes them "from $6.25 + 1 unpriced" and "from $1.56 + 1 unpriced", and B's list says "makes 4 serves ·
from $1.56 a serve + 1 unpriced"; B is refused "500ml" of onion ("It is counted in each"), sets 2 - still unpriced;
B prices onions at $4.50 for 6 and A's open page completes to $7.75 and $1.94; B cannot delete the onion ("used by 1
product: ..., and 1 meal: ..."); the server and both profiles' SQLite hold the same two lines (500 browned, 2),
outboxes empty; B removes the onion line and A's batch is $6.25 again; deleting the meal leaves it and both lines as
tombstones on the server. Phone-sized screenshots of the page with its lines, the sheet's search and quantity steps,
and the list with costs. `chrome-prices-check.mjs` and `chrome-catalogue-check.mjs` pass as before.

The in-process tests: `Tests/Domain/MealCostingTests.cs` (every kind, rounding of a line, a serve and the lines adding
up, incomplete states, no ingredients, variable-weight meat, the largest values), `MealIngredientRulesTests.cs`,
`MealRulesTests.cs`, and `Tests/Meals/MealSyncTests.cs` - seeds, every field by op and by snapshot, concurrent edits,
lines added on two offline browsers both surviving and costing the same on each, and a line whose ingredient was
deleted elsewhere.

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
  - AshersLab.MealPlanner.WASM/Components/NameList.razor
  - .claude/tools/chrome-meals-check.mjs
---

# Meals

What the household eats, how it is grouped, and what goes into it. Read before touching meals, meal groups or a
meal's ingredients. Every edit is an op, as [offline sync](../sync/offline-sync.md) describes; the ingredients a
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

---
title: The pantry
kind: reference
status: current
module: pantry
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.Shared.Domain/Pantry
  - AshersLab.MealPlanner.Shared.Domain/Ingredients/StapleStock.cs
  - AshersLab.MealPlanner.Shared.Data/Pantry
  - AshersLab.MealPlanner.WASM/Features/Pantry
  - AshersLab.MealPlanner.Tests/Domain/StockTests.cs
  - AshersLab.MealPlanner.Tests/Domain/StockRulesTests.cs
  - AshersLab.MealPlanner.Tests/Pantry/PantrySyncTests.cs
  - .claude/tools/chrome-pantry-check.mjs
---

# The pantry

About how much of each ingredient is in the kitchen, now and on any later day as the plan draws it down - so the
shopping list can subtract what is already there instead of assuming an empty kitchen every week. Read before the
shopping list (which subtracts it), receipts (which add to it) or anything else that touches stock.

## Traps first

- **What meals use is read from the calendar, never stored** (`Shared.Data/Pantry/PantryBook.cs`). Only the hand-made
  movements - bought, adjusted, counted - are rows. Stored draw-downs would need some device to write one for every
  cooked meal (two phones would write it twice), and every skip, move, replan, plan change or recipe edit to write a
  correction - each a chance to drift. Read from the calendar, a skipped dinner gives its mince back on every device at
  once, with no op (`chrome-pantry-check.mjs`: "no stock movement was written for it"). The future needs the calendar read
  anyway: a projection is the same arithmetic on days not yet eaten. The price: **a recipe edit changes what past meals
  used**, back to the last count. Recipes are approximate; a count puts it right.
- **A count stores the level, not a difference** (`StockReason.Counted`). "About half a pack left", tapped on a phone that
  was offline while another phone recorded a purchase, must say what was seen; a difference worked out against the stale
  estimate would be wrong once both arrive. Bought and adjusted *are* differences, and two of them from two phones add up -
  two rows, nothing to merge (the counter rule in [offline sync](../sync/offline-sync.md)).
- **Never below zero.** Meals that want more than the estimate holds leave nothing - "probably out" - and the next purchase
  starts from nothing. A negative estimate would quietly eat into the next shop.
- **On one day, the hand changes come before the meals.** A count made after cooking dinner then has that dinner taken
  off again: stock reads low, never high - the safe way to be wrong for a shopping list (buy a jar too many, rather than
  run out). Two hand changes on a day go in the order they were made (their GUIDv7 ids), so two phones' counts end on the
  later one everywhere.
- **Staples are not drawn down at all.** Oil and spices are used in amounts nobody weighs; their stock is a hand-set
  `Have | Running low | Out` (`Ingredient.StapleStock`, [the catalogue](../catalogue/catalogue.md)), and any movement of a
  staple is ignored.

## The ledger

`StockMovement (Id, IngredientId, Quantity, OccurredOn, Reason, ShoppingItemId, Deleted)`, in the ingredient's base unit.
Rules in `Shared.Domain/Pantry/StockRules.cs`; appended and deleted (undo), never edited - updates are accepted only so a
restored server can be given its fields back, as for prices. `ShoppingItemId` names the shopping-list tick that wrote it,
null for one made here: the list reads stock without its own ticks' movements, and unticking deletes them. **A tick's
movements count only while the tick says so, and only the newest** (`StockMovement.Counting`): two phones ticking one line
bought offline leave two live rows and one pack ([the shopping list](../shopping/shopping-list.md)).

| `Reason` | `Quantity` | Does |
|---|---|---|
| `Bought` | more than 0 | adds it - whole packs, here or ticked bought on the shopping list; later, matched receipt lines |
| `Adjusted` | not 0, usually negative | adds it - "threw this out"; down past zero is out; "have it" on the shopping list adds what it lacked |
| `Counted` | 0 or more | **sets** the level - "out", "½ pack", "that's what's left" |

What the calendar uses (`PantryBook`): a `Cooked` entry uses its meal's lines once, on the day it is cooked (the batch -
leftovers use nothing); a `Simple` entry's lines are one serve's, so once per eater (the people chosen, or the household).
`NoCost`, `FixedCost` and `Leftovers` use nothing; nor does a skipped entry, one the calendar no longer shows, or a line with
no quantity yet. A cooked meal uses its recipe once whatever its serves made - "we made 8" is not read as "we doubled it";
if it should be, the change is the `times` in `PantryBook.UsesAsync`.

Stock (`Shared.Domain/Pantry/Stock.cs`, pure): the movements and uses folded per ingredient, day by day. An ingredient is
`NotTracked` until its first movement - meals before it draw nothing, and there is no estimate. Then each day it is
`InStock`, `Out` (someone said so) or `ProbablyOut` (the meals ran it down). `StockLedger.At(ingredient, day)` is the start
of the day - after its hand changes, before its meals - **which is what the shopping list subtracts**: it loads
`PantryBook.LoadAsync(db, start, end, ignoringItems: its own items)` and reads `At(ingredient, start)`. `After` is the end of a day, `RunsOut` the first day
in a range whose meals take the last of it, and each `StockDay` carries `Short`, how much more the meals wanted than there
was.

## What the planner sees

**Pantry** in the menu (`/pantry`), phone and desktop alike:

- Every ingredient with stock, under its category: "Mince · about 1 kg", and beneath it "runs out Sun 25 Oct" when this
  week's plan uses the last of it, or "500 g left after Thu 29 Oct". "out" and "probably out" are amber.
- **Staples**, each with three chips - Have, Running low, Out. One tap.
- Search: an ingredient with no stock yet is listed under "Not counted yet", to tap and add.

Tapping an ingredient opens its sheet (full screen on a phone): what there is now; **What's left?** - Out, ¼ pack, ½ pack,
1 pack (each a count, so "we're out" is two taps from the list); **Bought** - +1 pack (1 kg), +2 packs; **An amount** -
type "300g" then "That's what's left", "Bought this" or "Threw this out"; **Coming up** - the plan's meals that use it,
"Sun 25 Oct · Bolognese · 500 g"; and **History**, newest first, each with a bin to undo it. The pack is the smallest of
the ingredient's products at its usual store, else anywhere; with none, the pack buttons are not shown. Every movement is
dated today.

## As seen

2026-10-22, `.claude/tools/chrome-pantry-check.mjs`, two headless Chromes on fresh profiles - A desktop-sized, B
phone-sized - every check passing: A adds mince with a 1 kg Costco pack, a staple soy sauce, and a bolognese using 500 g,
planned tomorrow and the day after next. A finds the mince under "Not counted yet" (its sheet listing both bolognese) and
taps "+1 pack": "about 1 kg · runs out Sun 25 Oct", the same on B. B taps "½ pack": both show "about 500 g · runs out Fri
23 Oct". A taps "Running low" on the soy sauce: B shows it, the server holds it. B skips tomorrow's bolognese on the
calendar: A shows "runs out Sun 25 Oct" and no movement was written. Both offline, A taps "Out" and B types 2 kg "Bought
this" - each shows its own ("out", "about 2.5 kg"); back online both show "about 2 kg · 1.5 kg left after Sun 25 Oct". B
deletes its purchase from the history: A shows "out". The four movements and the staple's state are identical in the
server's SQLite and both profiles', outboxes empty. Screenshots of the desktop page and the phone page.

The in-process tests: `Tests/Domain/StockTests.cs` (drawn down by meals and read before them, never below zero and the next
purchase from nothing, an exact draw, a count setting the level and clearing a drift, adjustments and out, the order within
a day, two phones' counts and purchases, nothing before the first movement, out by word staying out, projections and
`RunsOut`, ingredients apart and any input order, exact decimals), `Tests/Domain/StockRulesTests.cs` (the op's fields, the
amount fitting the reason, one spelling each, a staple's three states) and `Tests/Pantry/PantrySyncTests.cs` (through the
real host: draw-downs on another browser, a simple meal per eater, leftovers and unquantified lines using nothing, staples
untouched, uses listed for an ingredient never counted, a skip giving stock back with no movement written; stock read days
after the last movement with the meals since taken off; offline counts
and a purchase from two phones and a staple's state ending the same; an undo; a movement whose amount does not fit its
reason refused before it is queued). Breaking the zero floor, making a count add, drawing a simple meal once, ignoring a
skip, reversing the order within a day, or reading the calendar only from the day asked for each fails them.

---
title: The shopping list
kind: reference
status: current
module: shopping
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.Shared.Domain/Shopping
  - AshersLab.MealPlanner.Shared.Data/Shopping
  - AshersLab.MealPlanner.WASM/Features/Shopping
  - AshersLab.MealPlanner.WASM/Features/Planning/CalendarPage.razor
  - AshersLab.MealPlanner.Tests/Domain/ShoppingPlanTests.cs
  - AshersLab.MealPlanner.Tests/Domain/ShoppingRulesTests.cs
  - AshersLab.MealPlanner.Tests/Shopping/ShoppingSyncTests.cs
  - .claude/tools/chrome-shopping-check.mjs
---

# The shopping list

What to buy for a run of days - the calendar's meals less what the pantry already holds, in whole packs, by store - and
what that shop costs. Used one-handed in a shop with poor reception, so it works entirely from the browser's own
database and its ticks sync when the phone is back in reception. Read before touching the list, its ticks, the Spend
and Eaten costs, or anything that writes stock from a tick.

## Traps first

- **The list reads the pantry without its own ticks** (`ShoppingBook.LoadAsync` passes the list's item ids to
  `PantryBook.LoadAsync(..., ignoringItems)`). Ticking mince bought writes a 2 kg `Bought` movement; read with it, the
  mince would be covered and jump to "in the pantry" under the shopper's thumb. So every movement a tick writes names the
  tick (`StockMovement.ShoppingItemId`), and the list leaves those out. Every other reader - the pantry page, next week's
  list - counts them: the spare is in the pantry for next week. `ShoppingSyncTests.Ticks_made_offline_...` fails without it.
- **A tick undoes the line's last tick before writing its own** (`ShoppingTicking.EditsAsync`): it deletes every live
  movement naming the tick, then writes the new one. Bought → Have → Bought adds one pack, not three; unticking takes the
  pack back out of the pantry on every device. All of a tap's ops go in one `WriteAllAsync`: all or none.
- **Lines are computed, never stored.** Only ticks and extra items are rows. A ticked line the plan no longer needs (the
  toast skipped after the bread was bought) stays on the list, marked "1 pack · not on the plan now", so a tick never
  vanishes - and **what was bought stays bought** (`ShoppingPlan.Unplanned`): the packs its tick put in the pantry, still
  under its store and still in the Spend. Planned-for-nothing would be "in the pantry" at $0 while the pack sat in stock.
  Changed to "have it" it tells the pantry nothing (the plan needs none); bought again, one pack.
- **Ids are derived, so two phones converge.** A list's id is a hash of its two dates (`ShoppingListRules.ListId`); a
  tick's is a hash of the list and the ingredient (`ShoppingItemRules.TickId`). Two phones making "this week's list"
  offline make one list; two ticking the same line tick one row, and the later tick wins. A tick is never deleted -
  unticking is `Tick = None` - because a deleted id is a tombstone that would ignore the next tick. Extra items have
  minted ids and are deleted like anything else.
- **Stock follows the tick, not the movement rows** (`StockMovement.Counting`, applied by `PantryBook`). A tick undoes
  only the movements its own phone can see: two people both ticking the mince bought offline each write a 2 kg pack,
  and after sync both are live rows - counted as rows, the pantry would hold 4 kg and next week's list buy too little,
  with nothing wrong on screen. So a movement a tick wrote counts only while it is what the tick says now (`Bought` for
  bought, `Adjusted` for "have it", nothing when unticked), and only the newest of those. A ticks bread bought and B
  (later) says "have it": the tick ends `HaveIt` and only B's adjustment counts. Both rows stay in the log.
- **The nav and the calendar open the same list.** A list's id is its dates, so "this week" must mean one range
  everywhere: with no list covering today the page opens this calendar week, Monday to Sunday - what the calendar's
  "Shopping list" links to - never "today plus six", which on a Thursday was another list whose ticks never met.

## A list

`ShoppingList (Id, StartsOn, EndsOn)` - a date range, at most 35 days. A different range is a different list. The page
opens on the list covering today (the latest started), else this week Monday to Sunday; "Change days" picks another range,
"Other lists" lists the recent ones. Nothing is written until the first tick or extra item.

## A line

`ShoppingBook` (`Shared.Data/Shopping/ShoppingBook.cs`), from either database:

1. **Needed** - for every calendar entry in range that is not skipped, what its meal uses, read from `PantryBook.UsesOf`:
   a cooked meal's recipe once on its cook date (the batch; leftovers use nothing), a simple meal's lines once per eater.
   Summed per ingredient.
2. **Have** - the pantry's stock at the start of the first day (`StockLedger.At`, [the pantry](../pantry/pantry.md)),
   only when it is `InStock`: out, probably out or never counted is nothing.
3. **Shortfall** = needed − have, never below zero. Zero → **in the pantry**: listed at the bottom so it can be checked,
   not bought, costing nothing.
4. **Packs** (`ShoppingPlan.Choose`): whole packs of one product covering the shortfall - "1 pack (1.6 kg spare)". Products
   at the ingredient's usual store win when it has any; among them a priced one beats an unpriced one, then **the cheapest
   for the packs needed** (300 g of mayo is one 400 g jar, not a 2 kg tub that is cheaper per gram), then the least spare,
   then the product id, so every device picks the same. No product with a pack size: the shortfall is bought as an amount
   ("750 g") and its cost is unknown. More than 999 packs is a typo'd pack size, never offered.
5. **Staples** are never needed by amount: a staple is on the list only when `Running low` or `Out` (one pack of its
   cheapest product), or once ticked.

Grouped by usual store (named stores A-Z, then "No usual store"), then category in the categories' order. Extra items sit
under their store, or under "No usual store" when they have none.

| | Shown as | Spend |
|---|---|---|
| Buy, with a pack | "1 pack (1.6 kg spare) · need 1 kg, have about 600 g" · "$24.99" | the packs |
| Buy, no pack size | "750 g" · "unpriced" | one missing |
| Staple running low | "1 pack · running low" · "$4.00" | the pack |
| In the pantry | "need 300 g, have about 500 g" | nothing |
| Ticked "have it" | "have it" | nothing |
| Extra item | "Bin bags" · "$12.99" or "unpriced" | its price, or one missing |

## Ticking

The whole row is the tap target for **bought** (a big checkbox, the row at least 60 px tall); **Have** beside it says it
is already in the kitchen. Both toggle. What each writes (`ShoppingTicking`), after undoing the line's last tick:

| Tick | Ingredient | Staple |
|---|---|---|
| Bought | a `Bought` movement of the whole packs (or the shortfall with no pack size), dated today | its state back to Have - staples are not counted by amount |
| Have it | an `Adjusted` movement of the shortfall, when the pantry had less than the meals need; nothing for a line it covered | back to Have |
| None (untick) | nothing more | back to Running low (not when someone has since said Out) |

Extra items tick bought only, and write nothing to the pantry: they have no ingredient. **Removing one is behind its ⋮
menu** ("Remove bin bags"), not a button at the row's right edge: that is where every ingredient row has Have, under a
thumb working down the list, and a removed extra is gone from every device with no undo.

## What it costs: Spend and Eaten

The scope's two calendar cost views, on the list and under the calendar's week (`cal-week-cost`, with a link to that
week's list). They answer different questions; skipped entries count in neither.

- **Spend** - what the shop costs, the one to compare with the budget: every line's packs (except those ticked "have it"
  and those the pantry covers), every extra item, and the range's **fixed-cost meals** - a serve per eater at the meal's
  amount shared over its serves (a $30 pizza feeding 3, eaten by 2, is $20). Leftovers and pantry stock cost nothing here:
  already bought. A staple ticked bought still counts, though it is no longer running low.
- **Eaten** - what the food was worth: every serve eaten at its meal's per-serve cost ([meals](../meals/meals.md)), by
  quantity - half a jar of mayo costs half a jar the week it is eaten. A cooked, simple, fixed-cost or no-cost entry is a
  serve per eater; a leftovers entry is the serves it got at the per-serve cost of the batch it eats.

Both are a `Cost` that may be incomplete: an unpriced line, an extra with no price, or an entry whose per-serve cost is
incomplete or unknown is **one missing each** ("from $3.12 + 1 unpriced"), never a silently low total.

## As seen

2026-10-22, `.claude/tools/chrome-shopping-check.mjs` on a fresh server database, two headless Chromes on fresh profiles -
A desktop-sized, B phone-sized - every check passing. A sets up stew twice (500 g of mince, sold in 2 kg packs at $24.99),
a salad (300 g of unpriced mayo), 600 g of mince bought and 500 g of mayo counted, soy sauce running low ($4). B's list,
on the phone: Costco - mince "1 pack (1.6 kg spare) · need 1 kg, have about 600 g · $24.99"; Woolworths - soy sauce "1 pack
· running low · $4.00"; the mayo in the pantry; Spend $28.99, Eaten "from $3.12 + 1 unpriced"; "2 to get · 0 done". B goes
offline, ticks the mince (still a line to buy) and the soy sauce, adds bin bags at $12.99: Spend $41.98, six ops waiting,
nothing on the server, the desktop unchanged. B reconnects: the desktop shows both ticks, the bags and $41.98; the server
holds a 2 kg `Bought` movement naming the tick; A's pantry shows about 2.6 kg. A unticks the mince - B shows it unticked,
the movement deleted - then taps Have: B shows "have it", Spend $16.99, and a 400 g `Adjusted` movement. One list, three
items and every movement identical in the server's SQLite and both profiles', outboxes empty.

Same day, after the review fixes, the same script further on: both profiles offline tick the mince bought; back online the
server holds two live `Bought` rows naming the one tick and A's pantry reads "about 2.6 kg" (one pack, not 4.6 kg). The
bin bags row has no remove button of its own; its ⋮ menu removes it, deleted on the server. "Shopping" in the menu and the
calendar's button both open "Mon 19 Oct – Sun 25 Oct". All checks passing.

In-process: `Tests/Domain/ShoppingPlanTests.cs` (750 g in 2 kg packs, the pantry first and only the shortfall rounded,
covered → in the pantry, out and probably out holding nothing, the usual store then the cheapest for the packs needed, priced
before unpriced then least spare then id, no pack size, the 999-pack limit, a staple's one pack, exact decimals, serves and
sums incomplete never low), `Tests/Domain/ShoppingRulesTests.cs` (derived ids, a list's dates, a tick's fields, a tick or an
extra never both, an extra's fields changed only) and `Tests/Shopping/ShoppingSyncTests.cs` (through the real host: the
week's lines, groups, Spend and Eaten to the cent; offline ticks reaching the desktop and the pack in next week's pantry;
untick taking it back; bought → have → bought adding one pack; two phones ticking one line differently, and both ticking it
bought - one pack in the pantry; a ticked line dropped from the plan staying, with its pack and in the Spend; an unpriced line
and an unpriced extra each one missing in the Spend; a line with no pack size bought as its shortfall; a staple marked Out on
the list and left Out by an untick). Rounding packs down, forgetting the usual store, counting an incomplete serve's every
missing input, reading the pantry with the list's own ticks, not undoing a line's last tick, charging a "have it" line,
counting every movement a tick wrote, costing a dropped line at nothing, leaving unpriced lines out of the Spend, buying
nothing for a line with no pack, listing only running-low staples, or unticking Out to running low each fails them.

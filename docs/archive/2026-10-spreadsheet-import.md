---
title: Spreadsheet import
kind: reference
status: rejected
closed: 2026-10-23
reason: Removed with its code - the planner plans in the app from now on and will never import a spreadsheet again, so its review was not worth refining; the owner enters the first plan by hand.
module: import
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.Shared.Domain/Import
  - AshersLab.MealPlanner.Shared.Data/Import/ImportKnownReader.cs
  - AshersLab.MealPlanner.LocalData/Import/SheetImportWriter.cs
  - AshersLab.MealPlanner.LocalData/Sync/LocalOps.cs
  - AshersLab.MealPlanner.WASM/Features/Import/ImportPage.razor
  - AshersLab.MealPlanner.WASM/Features/Ingredients/IngredientEditor.razor
  - AshersLab.MealPlanner.Tests/Import
  - .claude/tools/chrome-import-check.mjs
---

# Spreadsheet import

How the planner's meal-plan spreadsheet becomes drafts in the app: what it reads, what it asks, what it writes, and
why running it twice is safe. Read before changing the import, or anything it matches against (ingredient, meal,
product and price names).

## Traps first

- **The sheet is personal data. It is read in the browser and never leaves it**, and it is never seed data, a test
  fixture or a doc example. The tests use a sheet made up by hand to match the layout's shapes
  (`Tests/Import/SyntheticSheet.cs`, written into an `.xlsx` in memory); `chrome-import-check.mjs` takes the real one
  as a path and prints counts only.
- **Matching is by name, and only by name.** An ingredient is the same when `ImportNames.Key` is equal (case, spacing,
  punctuation and plurals ignored: "egg" is "Eggs", "beef strips " is "Beef strips"); a meal, a household
  product or a heading when `Names.Same` is. A shopping-list name answered in the review as an ingredient already here
  is remembered by **the product its price made, which keeps the sheet's name** under that ingredient - so the next
  download maps it without asking (`SheetImport.KnownByProduct`). A name renamed in the app since, with no such product,
  is not recognised: importing again asks, or adds the sheet's spelling back
  (`ImportSyncTests.What_the_planner_changed_or_deleted_*`).
- **What matches is never updated** - the planner may have fixed it since; the sheet is not the master after the first
  run. The one exception is the planner's own answer: a unit still marked as the import's guess, which a price
  contradicts, is changed when the review is answered so (below). **Something deleted here is not brought back** -
  ingredient, meal, product or price: the matching reads tombstones too, and says so ("Chives: deleted here before, so
  not added again.", "Lamb mince $24.99 (Plan C16): its product was deleted here, so not added again."). This works on
  a phone that loaded everything from the server's snapshot too: a snapshot carries a tombstone's fields, its name
  included ([offline sync](../sync/offline-sync.md#the-server)).
- **A sheet price is compared with what the sheet said last time, never with the latest price.** Prices are written as
  `PriceSource.Sheet` ("spreadsheet" in a price's history). A product's sheet prices are written only when the sheet now
  says something it did not say on the last day it gave that product any (`SheetImport.SheetSaidLast`, deleted ones
  included) - and then all of them, so a product with two prices in the sheet stays at two. A receipt or typed price
  since does not make an unchanged sheet's price "new": comparing with the latest price would have the old sheet
  overwrite every newer receipt price, dated today.
- **A count is written "N each", never a bare number.** "$3ea" read as pack "1" would be 1 g for an ingredient counted
  in grams, a price silently a thousand times too high per kilo; "1 each" is refused for it, and the review asks.
- **Two devices importing offline at the same time both add everything** - each matched against its own database. The
  planner then sees two of each and deletes one, as with any duplicate ([the catalogue](../catalogue/catalogue.md)).
  It is a one-person, one-off job; nothing stops it.

## What the sheet holds, and what each part becomes

`SpreadsheetLayout.Parse` finds the regions by their headings ("WEEK n", "MEAL IDEAS", "COSTS" - plural: a "Cost" column
is a price column), in any column and any case, on any sheet. A heading word in another region's heading row is one of
its columns, unless an empty column separates them - then the regions sit side by side, each read within its own
columns. A region runs right to the next heading on its row and down to the next heading within its columns. A sheet
with none of the headings is a flat list; a row of it with no name is reported with its prices, not dropped.

| Region | Becomes |
|---|---|
| "WEEK 1".."WEEK n", one row per slot, columns per day | A plan template, "Spreadsheet plan" - see "The plan" below. Only weekday columns: sums or notes the sheet keeps beside them are not days |
| "MEAL IDEAS", one column per meal group | `Meal`s in that group, with their ingredients as lines with no quantity |
| "COSTS", one column per shopping category | `Ingredient`s in that category; the non-food column ("Non-food", "Household") becomes products with no ingredient |
| A sheet with no headings | More ingredients (no category), merged by key; prices on their rows |
| Anything that fits none of these | Listed under "Not imported" with the cell's address and why - never dropped silently |

**A meal-ideas cell** (`SheetText.MealIdea`): `4x Name (a, b, c)`. The `Nx` is optional and may be `8?x`; the
parenthesis may never close (everything after it is ingredients). Inside, parentheses nest: "rice (basmati)" is rice,
noted "basmati", and the list goes on after it. `/` or " or " is alternatives: the first is the line, the rest its note
("or ham"). A trailing `?` is dropped from the name, and any `?` in the cell puts the
whole cell in the meal's notes ("From the spreadsheet: 8?x ..."), so the doubt is not lost.

| The cell | Kind | Serves |
|---|---|---|
| `Nx` with N of 2 or more | Cooked | N |
| Parentheses, no `Nx` | asked; left empty, single serve (Simple) | 1 |
| No parentheses ("Pears", "Garlic naan") | Simple, its one line the ingredient of the same name if there is one | 1 |
| The same name twice | added once, and said | |

**A price** (`SheetText.Price`, `CostItem`) is read in three places: after the name in its cell (`Toilet rolls 14.50/24`,
`Mince $12.50/kg Costco`, `Milk 2L 3.10`); in a column whose heading is a store ("Costco", "Woolies") or "Price"/"$" -
it belongs to the category column on its left; and in the cells after a name on the flat list. In a name cell a price
must look like one - a `$`, two decimals, or what it is for ("12/kg") - so "Eggs 12" is a name. A number cell may hold
the float 4.4999999999999998 for 4.50: rounded only there, never in typed text. A store word in the cell, or the
column's heading, is its store; "Woolies"/"Woollies" mean Woolworths (`SheetText.StoreAliases`). A store after a name
with no price, "Pears (Woolies)", is the ingredient's usual store.

**What a price is for** (`SheetText.ForWhat`) - the planner writes it in words, and each shape is tested with made-up
values in `SheetTextTests`:

| Written | Pack |
|---|---|
| `$4 for 500g`, `$3 for 1Litre`, `$5 for kg`, `$1 per 500g`, `12/kg` | that amount: `500g`, `1Litre`, `1kg` |
| `$3 for 6`, `$5 for 40pk`, `$6 for 24 slices` | a count: `6 each` |
| `$0.80ea`, `$2 each`, `$3 per pumpkin`, `$3 per bag, ~8 rolls per bag` | `1 each` |
| `$17 for 12 servings (6x250g)` | the pack in the note: `6x250g` |
| `$3 for 5 servings`, `$14 for ~2 months` | none - the review shows "the sheet says "5 servings"" beside the box to type it |
| `$2.95` | none - asked |
| `~$6`, `~ $4` | the price, as written |
| `$2.10 for 110g OR $0.80 for 40` | only the first |
| `(on special)`, `(berry/apricot)` | notes in parentheses are dropped |
| `2 for $5`, `3 for 10.00` | a multi-buy: the price for the count, `2 each` - never $2 for five of something |
| `2 for 5` | not read: which is the price is not guessed |
| `<$12 (only on sale)` | not a price: the name is added with none, and "Not imported" lists the cell's words |

## The plan

`SheetImport.Plan.cs`. The "WEEK n" regions become one [plan template](../planning/plan-templates.md) of as many weeks
as the highest week number, with an entry per thing eaten, every entry for everyone.

**Imported once, ever** (trap): the template has a fixed id, `SheetImport.PlanTemplateId`
(`00000000-5eed-7000-8000-000000000501`). A database that holds it - renamed or not - or its tombstone gets no plan
from any later run: no entries, no slots, no plan meals, no plan questions, and "Not imported" says why ("The plan:
already here as ..." / "deleted here before, so not imported again."). After the first run the plan is the app's, as
everything else the sheet gave. The fixed id also means two devices importing offline make one template, not two
(their entries still duplicate - see the duplicate trap above).

**A cell's names** are found by matching as few as possible against the meals - the app's live meals and the meal ideas
this run adds, by `ImportNames.Key` (case, punctuation and plurals ignored): the whole '/'-choice if it is a meal ("Muesli
+ yoghurt"), else each ','-part that is one ("Eggs + soldiers"), else that part's '+'-items
(`SheetText.PlanChoices`, `SheetImport.PlanNames`). Then:

| The cell | Becomes |
|---|---|
| One choice of one or more names ("Rice cakes, cheese, apple") | an entry per name, one choice each |
| Several choices of one name each ("Leftovers/noodles/pizza") | one entry, a choice per name, in the sheet's order |
| Several choices, one of several names ("Takeaway, salad/soup") | left out: "a choice between several things eaten together - add it on the plan's page." |
| The same meal twice in a cell ("Crumpet, eggs + crumpet") | planned once, and said, with every cell it happened in |

**A name becomes a meal**: the meal of that key if there is one; "Leftovers" and "Family dinner" a meal of kind
`Leftovers` / `NoCost` of that name when there is none; a name like a meal is asked (below); any other name a new
single-serve (`Simple`) meal, in no group, made of the ingredient of the same key when there is one ("Apple" of
"Apples") - **made on first sight and reused after**, however many cells name it. A name whose meal was deleted here is
left out of the plan, said once, and not made again. **A name only like a deleted meal** ("Nodles", with "Noodles"
deleted) is asked about like any other near match, with "Noodles - deleted here, leave it out" among the answers and
chosen when it is a likely match; left so, it is left out and said ("read as Noodles, which was deleted here").

**Slots** are the row label's leading word ("Lunch (packed for work)" is Lunch; the rest of the label is listed under
"Not imported", since who an entry is for is not something the sheet says). The nth row of a word in a week is the nth
slot of that name **counting deleted ones, in the day's order** - the two snacks: delete the morning Snack and the
sheet's first Snack row is left out, while the second still goes to the afternoon one. A slot the day lacks is added at
the end, once for every week; one deleted here leaves its rows out rather than coming back.

## Stores, when the sheet names none

A price's store is, in order: the planner's answer for that price; a store word beside it; the ingredient's usual store
(for a household item, the household store); then **one answer for every price that names none** ("Prices that don't
say where they are from were seen at", `ImportChoices.PriceStore`). Until that is chosen those prices are listed "Left
out: no store chosen" - a sheet that names no store at all is one tap, not one per price.

## Units are guesses, marked

The sheet never says what anything is counted in. `UnitGuess.For` guesses by the most specific word of the name
("juice of a lime" is ml though "lime" is each; "tinned tomato puree" g though "tomato" is each), then the category, then grams;
a pack the sheet gave beats both ("24" is a count, "2kg" grams - `SheetImport.GuessUnit`). Every guessed ingredient is
created with **`UnitGuessed = true`** (an ingredient field, [the catalogue](../catalogue/catalogue.md#ingredients)). A
unit chosen in the review is not a guess.

## What it asks

`SheetImport.Review` - only what the sheet cannot tell, each with the likely answer chosen:

| Question | When | Suggested |
|---|---|---|
| Which ingredient is "cheese"? | a meal names something that is not exactly an ingredient, but resembles one (`ImportNames.Candidates`: a letter or two wrong or swapped, one name wholly inside the other - offered first - or a word in common) | a likely misspelling; otherwise a new ingredient. Nothing like it at all: a new one, not asked |
| Is "Sweet chili sauce" one already here? | on a later import, a shopping-list name resembling an ingredient here, and not answered before (a product of that name) | the same: a likely misspelling, else new |
| Units the prices disagree with | an ingredient here whose unit is still the import's guess, and a price's pack is counted another way ("guessed as g · the sheet says $2.00 for 1 each") | keep; until changed, that price is left out with why. Changing it sends the unit and `UnitGuessed = false` |
| Which meal is "Lmab korma"? | a plan name that is not exactly a meal but resembles one (`ImportNames.Candidates`, as for ingredients) - only while the plan will be imported | a likely misspelling (a letter or two wrong or swapped); otherwise "A new single-serve meal". Nothing like any meal: new, not asked |
| How many serves? | a meal with ingredients and no `Nx`, or `N?x` | empty (single serve), or N |
| Headings | a meal-ideas or costs heading not exactly a group or category ("Snacks/sweets") | the closest ("Snack"), or a new one at the end |
| A price's store and pack | always shown, each "Will be imported", "Already here - the same as its latest price" or "Left out: why" | the store and pack the sheet gave |
| Where prices that name no store were seen | some price names no store and its ingredient has no usual store | none - left out until chosen |
| Where household items are bought | there is a non-food column | none - they are left out until chosen |

New ingredients are listed with their guessed unit and a g / ml / each switch each.

## What it writes

`SheetImport.Plan` - pure, from the parsed sheet, what the database holds (`ImportKnownReader`), the answers and the
day. In order: categories and groups (new ones at the end), ingredients, meals and their lines (no quantities),
products, prices (`Sheet`, seen on the day of the import).

- **A product is the same one** when its store, its ingredient (for a household item, its name) and its pack size
  match; otherwise a new product named as the sheet names it - which is how an answer is remembered (traps, above).
- A household item has no unit: its pack is a count, or what the pack says (`100g` is 100).
- **A price is written only when the sheet says something new for its product** (traps, above). So a later download of
  the sheet with more prices filled in adds exactly those prices, and the same sheet twice adds nothing - whatever
  receipts arrived between (`ImportSyncTests.A_later_download_with_more_prices_adds_only_those_prices`,
  `SheetImportTests.A_price_the_sheet_said_last_time_*`, `Two_prices_for_one_product_*`).
- A household item with no price becomes a product of pack 1 at the household store, to be priced on the Prices page.
- A pack of the wrong kind for an existing ingredient's unit ("2kg" for one counted in each) is left out with the
  reason, never converted.

**It is written in one transaction** (`SheetImportWriter.WriteAsync` → `LocalOps.WriteAllAsync`): every edit checked
with the server's own op rules first, then all applied and queued, or none. The plan is made again from the database
at that moment, not from when the review opened, so rows a sync brought meanwhile are matched rather than duplicated.
Ids are a millisecond apart so a meal's lines list in the sheet's order.

## What the planner sees

"Import spreadsheet" (`/import`; on a phone More → Settings → Import spreadsheet). Under a line on how to download the sheet as .xlsx, a file box. Picking
the file shows a box "This adds 71 ingredients, 31 meals, 107 meal ingredients, 1 meal group, 8 products, 1 price." with
**Import**, then the questions above (native selects, the phone's own picker), the new ingredients with their unit
switches, and "Not imported" - every cell or price left out and why. Import, and it says what it added, with buttons
to Ingredients, Meals and Prices. Picking the same sheet again says "Nothing new to import: everything in this sheet is
already here" and Import is greyed.

On the Ingredients page each guessed ingredient has an orange **Check unit** chip; its editor says "The spreadsheet
import guessed this. Check it - saving marks it as checked." Saving (unit changed or not) sends `UnitGuessed = false`.

## As seen

2026-10-21, `.claude/tools/chrome-import-check.mjs` with the planner's real sheet (an export from 2026-10-20, with one
price filled in), two headless Chromes on fresh profiles against a fresh server database (holding only what
`chrome-catalogue-check.mjs` leaves): the review asked 2 ingredient, 7 serves and 2 heading questions and listed 71 new
ingredients; with household items at Costco, Import wrote 219 ops in one transaction in about 7 s - 71 ingredients (all marked guessed), 31 meals, 107 lines with no
quantity, 1 group, 8 household products and the sheet's one price (source `Sheet`). The server's log grew by
exactly 219, and both profiles' SQLite held the same counts with outboxes empty. The same sheet on profile B, and again
on A after a reload: "Nothing new to import", Import greyed, the server unchanged. B saved a guessed ingredient
unchanged: the chip went on A (71 to 70) and the server row read `UnitGuessed = 0`. Phone-sized screenshots of the
review and the Ingredients list. Read by hand from the server afterwards: units, categories, serves (`8?x` as 8 with
the note), groups and lines as the sheet meant them.

2026-10-21, a later download of the same sheet with the owner's prices written in words after every shopping-list
name and no store named anywhere, imported **on top of the first**, same script (`chrome-import-check.mjs <scratch>
<first.xlsx> <later.xlsx>`), on a fresh server database after the first import above. The sheet holds 70 cells with a
`$` in them: 2 are "less than" amounts, reported as not prices (their items added with none); the review listed the 68
others. With prices that name no store at Woolworths and household items at Costco, every name question left at its
suggestion (7 asked) and each of the 10 unit questions answered as the price says: 48 prices imported (source
`Sheet`), 1 already here (the first import's, unchanged), and 19 left out with the reason - 13 priced per serving
or per months (the sheet's words shown), 6 with no amount. 48 + 1 + 19 = 68. The server gained exactly 48 prices, 48
products and 10 ingredients; comparing every field of every row from before, **nothing had changed but the 10 units
answered**, each now confirmed. Both profiles' SQLite matched the server; the later sheet again on B, choosing the same
stores: "Nothing new to import". The same import with the planner's own answers - 6 of the 7 names mapped to the
ingredients here - added 4 ingredients instead of 10, and the same 48 prices. Before this, the parser read none of
those prices: "$X for 500g", "$Xea", "for N servings" and the rest were new shapes, all now in the tests above.

The in-process tests (`Tests/Import`): every cell shape (`SheetTextTests` - `Nx` and `8?x`, unclosed parentheses, `/`
alternatives, prices in and beside names, things that are not prices), name keys and fuzzy matching
(`ImportNamesTests`), unit guesses, the layout from a grid and from a real `.xlsx` (shared strings, rich-text runs,
inline strings, phonetic hints, absolute part paths), the plan and its idempotence (`SheetImportTests`), and the import
through a real browser database and host (`ImportSyncTests`: reaches a second browser; a second run on either writes
nothing; a later sheet adds only its new prices; renamed and deleted rows; nothing written when one edit is refused).
Each was checked by breaking the code - the latest-price skip, the swapped-letters distance, `WriteAllAsync`'s
up-front check, the unclosed parenthesis; for the later sheet, servings read as a count, counts written bare, the one
store for unnamed prices, the remembered answer (on its own) and the weekday columns - and every break failed tests.

The same checks were re-run on 2026-10-21 after a review panel's findings were fixed - the same counts, every check
passing. Those fixes have their own tests, each failing when its fix was broken, one at a time where two masked each
other: the sheet's own last prices as the comparison (any source instead, every day instead of the last), product and
household-product tombstones, the plural "Costs" heading, the gap between side-by-side regions and their column limit,
a flat-list row with no name, the multi-buy, nested parentheses, and the likely-match fallback taken unanswered.

2026-10-22, with the plan: the same script, the same two sheets, a fresh server database. The first sheet's review
added "a 4-week plan of 287 entries" and asked about 9 plan names, one with a likely meal chosen (a swapped-letter
spelling), the rest left as new single-serve meals. Import wrote 826 ops in 12 s: 4 weeks, 287 entries, 298 choices
(7 entries with alternatives), 1 Leftovers and 1 NoCost meal, the 5 seeded slots and no new one. Every entry had a
choice and every choice named a live meal, on the server and in both profiles' SQLite; the done message linked to the
plan. B, A after a reload, and the later sheet on top: the plan's rows unchanged, nothing new. The plan's tests
(`PlanImportTests`) were checked by breaking the imported-once guard and the likely-match suggestion: each failed tests.

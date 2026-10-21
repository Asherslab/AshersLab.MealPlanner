---
title: Spreadsheet import
kind: reference
status: current
module: import
verified: 2026-10-21
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
  punctuation and plurals ignored: "egg" is "Eggs", "chicken schnitzel " is "Chicken schnitzel"); a meal, a household
  product or a heading when `Names.Same` is. Something renamed in the app since is not recognised, so importing again
  adds the sheet's spelling back as a new row (`ImportSyncTests.What_the_planner_changed_or_deleted_*`).
- **What matches is never updated.** The planner may have fixed it since; the sheet is not the master after the first
  run. **Something deleted here is not brought back** - the matching reads tombstones too, and says so ("Chives:
  deleted here before, so not added again.").
- **Two devices importing offline at the same time both add everything** - each matched against its own database. The
  planner then sees two of each and deletes one, as with any duplicate ([the catalogue](../catalogue/catalogue.md)).
  It is a one-person, one-off job; nothing stops it.

## What the sheet holds, and what each part becomes

`SpreadsheetLayout.Parse` finds the regions by their headings, in any column and any case, on any sheet; a region runs
from its heading to the next. A sheet with none of the headings is a flat list.

| Region | Becomes |
|---|---|
| "WEEK 1".."WEEK n", one row per slot, columns per day | Read into `PlanWeek`s (slot = the label's leading word, cells split into choices on `/` and items on `,` and `+`) - **not imported yet**: plan templates are slice 8, which consumes `ParsedSheet.Plan` |
| "MEAL IDEAS", one column per meal group | `Meal`s in that group, with their ingredients as lines with no quantity |
| "COSTS", one column per shopping category | `Ingredient`s in that category; the non-food column ("Non-food", "Household") becomes products with no ingredient |
| A sheet with no headings | More ingredients (no category), merged by key; prices on their rows |
| Anything that fits none of these | Listed under "Not imported" with the cell's address and why - never dropped silently |

**A meal-ideas cell** (`SheetText.MealIdea`): `4x Name (a, b, c)`. The `Nx` is optional and may be `8?x`; the
parenthesis may never close (everything after it is ingredients). Inside, `/` or " or " is alternatives: the first is
the line, the rest its note ("or lime juice"). A trailing `?` is dropped from the name, and any `?` in the cell puts the
whole cell in the meal's notes ("From the spreadsheet: 8?x ..."), so the doubt is not lost.

| The cell | Kind | Serves |
|---|---|---|
| `Nx` with N of 2 or more | Cooked | N |
| Parentheses, no `Nx` | asked; left empty, single serve (Simple) | 1 |
| No parentheses ("Bananas", "Garlic bread") | Simple, its one line the ingredient of the same name if there is one | 1 |
| The same name twice | added once, and said | |

**A price** (`SheetText.Price`, `CostItem`) is read in three places: after the name in its cell (`Toilet rolls 14.50/24`,
`Mince $12.50/kg Costco`, `Milk 2L 3.10`); in a column whose heading is a store ("Costco", "Woolies") or "Price"/"$" -
it belongs to the category column on its left; and in the cells after a name on the flat list. In a name cell a price
must look like one - a `$`, two decimals, or what it is for ("12/kg") - so "Eggs 12" is a name. A number cell may hold
the float 4.4999999999999998 for 4.50: rounded only there, never in typed text. A store word in the cell, or the
column's heading, is its store; "Woolies"/"Woollies" mean Woolworths (`SheetText.StoreAliases`). A store after a name
with no price, "Pears (Woolies)", is the ingredient's usual store.

## Units are guesses, marked

The sheet never says what anything is counted in. `UnitGuess.For` guesses by the most specific word of the name
("lime juice" is ml though "lime" is each; "tinned tomato" g though "tomato" is each), then the category, then grams;
a pack the sheet gave beats both ("24" is a count, "2kg" grams - `SheetImport.GuessUnit`). Every guessed ingredient is
created with **`UnitGuessed = true`** (an ingredient field, [the catalogue](../catalogue/catalogue.md#ingredients)). A
unit chosen in the review is not a guess.

## What it asks

`SheetImport.Review` - only what the sheet cannot tell, each with the likely answer chosen:

| Question | When | Suggested |
|---|---|---|
| Which ingredient is "cheese"? | a meal names something that is not exactly an ingredient, but resembles one (`ImportNames.Candidates`: a letter or two wrong or swapped, or a word in common) | a likely misspelling; otherwise a new ingredient. Nothing like it at all: a new one, not asked |
| How many serves? | a meal with ingredients and no `Nx`, or `N?x` | empty (single serve), or N |
| Headings | a meal-ideas or costs heading not exactly a group or category ("Snacks/desserts") | the closest ("Snack"), or a new one at the end |
| A price's store and pack | always shown; "Left out: no store chosen" until it has both | the store and pack the sheet gave |
| Where household items are bought | there is a non-food column | none - they are left out until chosen |

New ingredients are listed with their guessed unit and a g / ml / each switch each.

## What it writes

`SheetImport.Plan` - pure, from the parsed sheet, what the database holds (`ImportKnownReader`), the answers and the
day. In order: categories and groups (new ones at the end), ingredients, meals and their lines (no quantities),
products, prices (`Manual`, seen on the day of the import).

- **A product is the same one** when its store, its ingredient (for a household item, its name) and its pack size
  match; otherwise a new product named as the sheet names the ingredient.
- **A price is written only when it differs from the product's latest** (`PriceReadings`' order). So a later download
  of the sheet with more prices filled in adds exactly those prices, and the same sheet twice adds nothing
  (`ImportSyncTests.A_later_download_with_more_prices_adds_only_those_prices`).
- A household item with no price becomes a product of pack 1 at the household store, to be priced on the Prices page.
- A pack of the wrong kind for an existing ingredient's unit ("2kg" for one counted in each) is left out with the
  reason, never converted.

**It is written in one transaction** (`SheetImportWriter.WriteAsync` → `LocalOps.WriteAllAsync`): every edit checked
with the server's own op rules first, then all applied and queued, or none. The plan is made again from the database
at that moment, not from when the review opened, so rows a sync brought meanwhile are matched rather than duplicated.
Ids are a millisecond apart so a meal's lines list in the sheet's order.

## What the planner sees

"Import spreadsheet" in the menu (`/import`). Under a line on how to download the sheet as .xlsx, a file box. Picking
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
quantity, 1 group, 8 household products and the sheet's one price as a manual observation. The server's log grew by
exactly 219, and both profiles' SQLite held the same counts with outboxes empty. The same sheet on profile B, and again
on A after a reload: "Nothing new to import", Import greyed, the server unchanged. B saved a guessed ingredient
unchanged: the chip went on A (71 to 70) and the server row read `UnitGuessed = 0`. Phone-sized screenshots of the
review and the Ingredients list. Read by hand from the server afterwards: units, categories, serves (`8?x` as 8 with
the note), groups and lines as the sheet meant them.

The in-process tests (`Tests/Import`): every cell shape (`SheetTextTests` - `Nx` and `8?x`, unclosed parentheses, `/`
alternatives, prices in and beside names, things that are not prices), name keys and fuzzy matching
(`ImportNamesTests`), unit guesses, the layout from a grid and from a real `.xlsx` (shared strings, rich-text runs,
inline strings, phonetic hints, absolute part paths), the plan and its idempotence (`SheetImportTests`), and the import
through a real browser database and host (`ImportSyncTests`: reaches a second browser; a second run on either writes
nothing; a later sheet adds only its new prices; renamed and deleted rows; nothing written when one edit is refused).
Each was checked by breaking the code - the latest-price skip, the swapped-letters distance, `WriteAllAsync`'s
up-front check, the unclosed parenthesis - and every break failed tests.

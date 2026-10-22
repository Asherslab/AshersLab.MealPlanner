---
title: Products and prices
kind: reference
status: current
module: catalogue
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.Shared.Domain/Products
  - AshersLab.MealPlanner.Shared.Domain/Sync/OpValues.cs
  - AshersLab.MealPlanner.Shared.Domain/Prices
  - AshersLab.MealPlanner.Shared.Data/Products
  - AshersLab.MealPlanner.Shared.Data/Prices
  - AshersLab.MealPlanner.Tests/Domain/MoneyTests.cs
  - AshersLab.MealPlanner.Tests/Domain/PriceRulesTests.cs
  - AshersLab.MealPlanner.Tests/Domain/IngredientPricingTests.cs
  - AshersLab.MealPlanner.WASM/Features/Products
  - AshersLab.MealPlanner.Tests/Domain/PackSizeTests.cs
  - AshersLab.MealPlanner.Tests/Domain/ProductRulesTests.cs
  - AshersLab.MealPlanner.Tests/Catalogue/ProductSyncTests.cs
  - .claude/tools/chrome-prices-check.mjs
---

# Products and prices

What a store actually sells, and what it cost. Read before touching products, or anything that turns a pack's price
into a cost. The stores and ingredients they hang off are [the catalogue](catalogue.md); every edit is an op, as
[offline sync](../sync/offline-sync.md) describes.

## Traps first

- **A pack quantity is in the ingredient's base unit, read in whatever unit the ingredient has now.** A 2 kg pack is
  `2000`, for an ingredient counted in grams. Changing the ingredient's unit converts nothing, so its products' pack
  sizes then mean the wrong thing until someone fixes them.
- **Decimals travel in one spelling** (`OpValues.FormatDecimal`: invariant, no trailing zeros). `decimal` keeps its
  scale, and EF's SQLite providers store it as text that differs between the server (`2000.0`) and a browser
  (`2000`), so a snapshot re-reads the column and re-formats it. The op rules refuse `2000.0`. Compare in SQL with
  `cast(PackQuantity as real)`.
- **Aliases are not a field of the product.** The receipt descriptions a product has been matched to are their own
  rows (`ProductAlias`, one per description, written by [receipt import](../receipts/receipts.md)). A list in one field
  would merge last-writer-wins: two phones each matching a different receipt line offline would lose one alias.

## Products

`Product (Id, Name, StoreId, StoreCode, PackQuantity, IngredientId, Deleted)`. Rules in
`Shared.Domain/Products/ProductRules.cs`.

| Field | On the wire | Notes |
|---|---|---|
| `Name` | normalised text | required on create; what the store calls it |
| `StoreId` | GUID, never null | required on create |
| `StoreCode` | normalised text up to 50, or null | Woolworths stockcode / Costco item number; what a receipt matches first |
| `PackQuantity` | `OpValues.FormatDecimal`, > 0, at most 1,000,000 and 3 decimal places | required on create |
| `IngredientId` | GUID or null | null for a household item, priced but never in a recipe |

Many products per ingredient (one per store, sometimes two sizes at one store). Not foreign keys, as everywhere in
the catalogue: a product whose store or ingredient is gone shows "store removed" or under "Not food".

**Pack sizes are typed the way the pack says them** (`PackSize.Parse` over `Ingredients/Quantity.cs`, which a meal's
quantities share; tested in `Tests/Domain/PackSizeTests.cs`):
`500g`, `2kg`, `1.4KG`, `8x250g`, `375ml`, `2L`, `6x375ml`, `18pk`, `148CT`. Only the units of the ingredient's own
kind are accepted - `1.5L` for an ingredient in grams is refused ("It is counted in g"), never converted. A comma is
never a decimal point. The editor shows what it read ("= 2 kg") under the box before saving. A number too big for a
decimal (a pasted run of digits) is refused, "That number is too big.", never thrown while the box renders.

**Trap: the edit box holds every digit, not what the list shows.** The list reads 1234.5 g as "1.235 kg"; the box reads
"1.2345kg" (`Quantity.Editable`). Save reads the box again, so a box filled from the rounded text would change the pack
size to 1235 g - and send an op - without the planner touching it. `Tests/Domain/QuantityTests.cs` reads every
quantity back through both editors.

**Deleting**: a product can always be deleted. An ingredient a product is bought as cannot (the ingredient editor's
Delete says "mince is used by 1 product: ... Delete them on the Prices page first."), nor can a store that sells one
(the stores page lists the products beside the ingredients) - the rule of
[the catalogue](catalogue.md#deleting-something-referenced).

## Prices

`PriceObservation (Id, ProductId, PriceCents, ObservedOn, Source, PerKgCents, Deleted)`. Rules in
`Shared.Domain/Prices/PriceRules.cs`; a new one is written as one create with every field (`PriceEntry.Fields`).

| Field | On the wire | Notes |
|---|---|---|
| `ProductId` | GUID, never null | required |
| `PriceCents` | whole cents as plain digits (`OpValues.FormatCents`), 1 to 10,000,000 | one pack; never dollars, never a decimal |
| `ObservedOn` | `yyyy-MM-dd` | the day it was seen; no time, no zone |
| `Source` | `Manual`, `Receipt`, `Live` or `Sheet` - by name | the pages write `Manual`; the [spreadsheet import](../import/spreadsheet-import.md) `Sheet`, so a later import compares only with what the sheet said; [receipts](../receipts/receipts.md) `Receipt`, on the receipt's date; live prices come later |
| `PerKgCents` | whole cents or null | variable-weight meat: what a gram costs when the pack price depends on its weight |

- **Append-only by use, not by rule.** The pages add a price and delete a mistaken one; nothing edits one, because the
  history is the point. The op rules still accept an update: a browser giving back what a restored server lost
  sends every field as an update ([offline sync](../sync/offline-sync.md)), and a create-only rule would reject those.
- **The latest price is the newest day, then the last entered** (`PriceReadings.Latest`, tested): a price back-filled
  for last month never replaces this week's. Two on one day: the later id (GUIDv7, so the later entry). The source does
  not matter - a receipt and a typed price are equal.
- Deleting a product leaves its prices as rows nothing shows: there is no cascade on sync.
- **Money is typed as dollars and stored as cents** (`Money.ParseCents`, tested in `Tests/Domain/MoneyTests.cs`):
  `24.99`, `$24.99`, `24`, `.99`. A third decimal place, a comma (`1,50`) and zero are refused - a typo read as a
  price would be wrong silently.

## An ingredient's price

`IngredientPricing.Price` (`Shared.Domain/Prices/IngredientPricing.cs`, tested in
`Tests/Domain/IngredientPricingTests.cs`) - step 1 of costing: what one gram, millilitre or each costs. The browser
runs it offline; the server will run the same code.

1. Each product is priced by its **latest** observation (newest day, then last entered; any source). A product with
   no observation prices nothing.
2. **A product at the ingredient's usual store wins**, even when another store is cheaper - the shopping list sends
   the planner there. Two at the usual store (two sizes): the cheaper per unit.
3. Otherwise - no usual store, or none of its products priced - **the cheapest per unit at any store**, flagged
   `FromUsualStore = false`.
4. Nothing priced: **null, "unpriced" - never zero.** [A meal's cost](../meals/meals.md#what-a-meal-costs) counts
   it as missing.

Equal per-unit prices go to the newer observation, then the product id, so every device picks the same product.

**Variable-weight meat**: for an ingredient counted in grams, an observation with a per-kg price is priced by the
kilo, not by the pack. A 1.25 kg tray at $18.73 of a product nominally 1 kg would otherwise be $18.73/kg instead of
$14.99/kg. A per-kg price on anything not counted in grams is ignored.

**The price is a ratio, rounded once** (`UnitPrice`): $24.99 for 2000 g is kept as (2499 c, 2000), never divided out -
1.2495 c/g is not whole cents, and decimal division can repeat (100 c for 3). `CostOf(quantity)` multiplies first,
divides, and rounds once to whole cents, half away from zero (937.5 c is 938 c). Which is cheaper is decided by
cross-multiplying, so no rounding picks a product. Shown per kg, per L, or each: "$12.50/kg".

## What the planner sees

`/prices`; on a phone More → Prices. Every ingredient, under its category in the household's order (the order of the
spreadsheet's price columns), with what costing will use beneath its name - "$16.66/kg at Costco", "$15.00/kg -
cheapest, at Woolworths" when the usual store has no price, or "unpriced" - and its products beneath that: the name, then "Costco · 2 kg · #12345", and on the right
its latest price and the day seen ("$24.99 / 21 Oct", or "no price"). A search box
filters by ingredient or product name; store chips ("All stores", "Costco", "Woolworths") show only that store's
products. Products with no ingredient are last, under "Not food".

- **Entering a price: tap the product, type, Enter.** The price sheet opens with the price box already focused and
  the phone's decimal keypad up (`inputmode=decimal`); Enter (or Save) adds the price, seen today. "Seen on" (a native
  date picker) back-fills an older price; "Per kg" appears for an ingredient counted in grams. Beneath is the
  history, newest first, each with its day and "typed in" / "receipt" / "online" / "spreadsheet", and a bin for a mistake (one
  confirmation).
- **Adding a product under an ingredient**: tap its +. The editor opens with the ingredient chosen, the store set to
  the picked chip, else the ingredient's usual store, and the name set to the ingredient's. Type the pack size and,
  optionally, today's price, Save: the product and its first price in one dialog.
- "New" beside the search adds one with no ingredient chosen (the search text as its name).
- The product's editor - with Delete - is "Edit product" on its price sheet.

The editor (full screen on a phone): Ingredient and Store as native selects (the phone's own picker), the name, the
pack size labelled with its unit ("Pack size (g)"), the first price (new products only) and the optional store code.

**The sheet focuses its price box itself** (`PriceSheet.OnAfterRenderAsync`, after 100 ms), not with `AutoFocus`: the
MudDialog's focus trap takes focus for its own root after the box's `AutoFocus` has run, whatever
`DialogOptions.DefaultFocus` says (`FirstChild` focuses the trap's sentinel). Seen in headless Chrome; without it every
price costs a tap on the box first.

## As seen

2026-10-21, `.claude/tools/chrome-prices-check.mjs`, two headless Chromes on fresh profiles against the running app:
A adds an ingredient in grams and a product under it typed `8x250g`; both profiles show "Costco · 2 kg · #12345",
the server row holds 2000; B is refused `1.5L`, saves `1.5kg`, A shows 1.5 kg; A cannot delete the ingredient or
Costco and is told the product's name; both profiles' SQLite hold 1500 with empty outboxes. Phone-sized screenshots
of the page and the editor.

Prices, same script: A taps the product, the price box has focus, `24.99` and Enter saves; both rows show $24.99; B
back-fills $22.50 for 1 Oct and both rows still show $24.99; A enters $18.73 at $14.99/kg and B shows it; B is refused
`1,50` ("Type a price like 24.99."); B's history reads $18.73, $24.99, $22.50; B deletes $18.73 and A falls back to
$24.99; a new Woolworths product saved with its first price shows $7.50 on B. The server holds all three prices, the
deleted one as a tombstone, and both profiles' SQLite the same, outboxes empty.

Unit price, same script: "unpriced" before any price; $16.66/kg at Costco for $24.99 / 1.5 kg; $14.99/kg while the
latest price had a per-kg price; still $16.66/kg at Costco once a cheaper Woolworths product ($15.00/kg) exists; and,
after B removes the usual store, "$15.00/kg - cheapest, at Woolworths" on both profiles.
`chrome-catalogue-check.mjs` and `chrome-sync-check.mjs` pass as before.

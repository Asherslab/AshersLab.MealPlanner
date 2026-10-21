---
title: Products and prices
kind: reference
status: current
module: catalogue
verified: 2026-10-21
code:
  - AshersLab.MealPlanner.Shared.Domain/Products
  - AshersLab.MealPlanner.Shared.Domain/Sync/OpValues.cs
  - AshersLab.MealPlanner.Shared.Data/Products
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
- **Aliases are not a field of the product.** The receipt names a product has been matched to will be their own
  rows (one per alias), written by receipt import (slice 12). A list in one field would merge last-writer-wins:
  two phones each matching a different receipt line offline would lose one alias.

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

**Pack sizes are typed the way the pack says them** (`PackSize.Parse`, tested in `Tests/Domain/PackSizeTests.cs`):
`500g`, `2kg`, `1.4KG`, `8x250g`, `375ml`, `2L`, `6x375ml`, `18pk`, `148CT`. Only the units of the ingredient's own
kind are accepted - `1.5L` for an ingredient in grams is refused ("It is counted in g"), never converted. A comma is
never a decimal point. The editor shows what it read ("= 2 kg") under the box before saving.

**Deleting**: a product can always be deleted. An ingredient a product is bought as cannot (the ingredient editor's
Delete says "mince is used by 1 product: ... Delete them on the Prices page first."), nor can a store that sells one
(the stores page lists the products beside the ingredients) - the rule of
[the catalogue](catalogue.md#deleting-something-referenced).

## What the planner sees

`/prices`, "Prices" in the menu. Every ingredient, under its category in the household's order (the order of the
spreadsheet's price columns), each with its products beneath: the name, then "Costco · 2 kg · #12345". A search box
filters by ingredient or product name; store chips ("All stores", "Costco", "Woolworths") show only that store's
products. Products with no ingredient are last, under "Not food".

- **Adding a product under an ingredient**: tap its +. The editor opens with the ingredient chosen, the store set to
  the picked chip, else the ingredient's usual store, and the name set to the ingredient's. Type the pack size, Save:
  two or three taps and one box when the store is right.
- "New" beside the search adds one with no ingredient chosen (the search text as its name).
- Tapping a product opens the same editor, with Delete.

The editor (full screen on a phone): Ingredient and Store as native selects (the phone's own picker), the name, the
pack size labelled with its unit ("Pack size (g)"), and the optional store code.

## As seen

2026-10-21, `.claude/tools/chrome-prices-check.mjs`, two headless Chromes on fresh profiles against the running app:
A adds an ingredient in grams and a product under it typed `8x250g`; both profiles show "Costco · 2 kg · #12345",
the server row holds 2000; B is refused `1.5L`, saves `1.5kg`, A shows 1.5 kg; A cannot delete the ingredient or
Costco and is told the product's name; both profiles' SQLite hold 1500 with empty outboxes. Phone-sized screenshots
of the page and the editor.

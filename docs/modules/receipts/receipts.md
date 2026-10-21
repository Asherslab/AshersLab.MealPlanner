---
title: Receipts
kind: reference
status: current
module: receipts
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.Shared.Domain/Receipts
  - AshersLab.MealPlanner.Shared.Data/Receipts
  - AshersLab.MealPlanner.Shared.Data/Pantry/StockMovement.cs
  - AshersLab.MealPlanner.WASM/Features/Receipts
  - AshersLab.MealPlanner.WASM/wwwroot/js/receipt-pdf.js
  - AshersLab.MealPlanner.WASM/wwwroot/lib/pdfjs
  - AshersLab.MealPlanner.Tests/Receipts
  - .claude/tools/chrome-receipts-check.mjs
  - .claude/tools/lib/synthetic-invoice.mjs
---

# Receipts

A Woolworths invoice read on the phone, its lines matched to products, and imported: prices from what was paid, stock
from what came home, and an alias per line so the next invoice matches by itself. Read before touching the import, the
matching, or anything that adds stock from a purchase. Costco receipts (OCR) are slice 13 - their format is in
[receipt formats](../../work/2026-10-receipt-formats.md).

## Traps first

- **Read by position, never in the order the PDF stores its text.** On a real invoice the totals' labels come out before
  the line table and their values after it. `PdfRows.Of` (`Shared.Domain/Receipts/PdfText.cs`) sorts runs into rows by
  baseline; `WoolworthsInvoiceTests.Reading_order_is_position...` shuffles them.
- **Bold is printed twice.** The invoice fakes bold by overprinting: every heading and label arrives as two identical runs
  at one spot. Undeduplicated, "Dairy" reads "DairyDairy". `PdfRows.Of` drops a run repeating another's text and position.
- **Ligatures split words** - "chicken breast fi llet" is three runs that touch. Runs less than a point apart are joined
  without a space (`PdfRows.Join`).
- **A long description wraps above and below its numbers.** The row with the line number and amounts can have no text of
  its own, half its description 3.8 pt above and the rest 4.5 pt below; category headings are rows of text too, 12-17 pt
  away. Text within `WoolworthsInvoice.WrapDistance` (6.5 pt) of a numbered row is its description; anything further is a
  heading. Rows are 12.7 pt apart.
- **A receipt that does not add up is never imported** (`ParsedReceipt.TotalsProblem`): the lines must make the sub total,
  and the sub total and fees the invoice total. Lines the reader cannot place - a page of lines with no column headings -
  are not guessed at; the totals then fail and the review says both figures. All seven real invoices (June-September 2026)
  add up.
- **Every id is derived, never minted** (`ReceiptRules`): a receipt's from its store and order number, a line's from the
  receipt and its position, its price and stock movement from the line, an alias from the store and its text. So two phones
  importing one invoice offline make the same rows - one receipt, one lot of stock - and "already imported" is one lookup.
  **The store is the reader's, never the planner's**: a Woolworths invoice is the seeded Woolworths (`StoreRules.Woolworths`,
  the same id in every database), with no store to pick in the review - a store picked differently would make a different
  receipt id and let the same purchase in twice. For the same reason "already imported" also matches the order number under
  any store (`ReceiptImport.LoadAsync`). A deleted seeded store refuses its receipts. The price: a deleted receipt's id is a tombstone forever, so nothing offers to delete one. New products are the
  exception (minted): two phones each making a product for the same line offline make two.
- **A suggestion never matches by itself.** Only the store's code and an alias do; a wrong automatic match would price and
  stock the wrong thing with nothing on screen to say so.
- **The file is never stored or uploaded.** `wwwroot/js/receipt-pdf.js` keeps the open PDF only while the review is up (to
  draw it on request) and drops it on import, cancel, or leaving the page. Only what was read from it becomes ops.

## Reading an invoice

The browser reads the PDF with **pdf.js, vendored** under `wwwroot/lib/pdfjs` (the legacy build, 5.7.284, for older iPhones'
Safari; Apache-2.0, its `LICENSE` beside it) - the app makes no third-party request. `receipt-pdf.js` hands .NET only the
text runs - text, x, baseline y from the top, width - as JSON (`PdfJson`, source-generated so trimming keeps it); every
decision is in `Shared.Domain/Receipts`, tested on synthetic invoices (`Tests/Receipts/SyntheticInvoice.cs` lays out the real
positions and traps; nothing from a real invoice is in the repo).

`WoolworthsInvoice.Parse`:

| Part | As printed | Read as |
|---|---|---|
| Header, every page | `Invoice/Order Number: 300000001`, `Date: 3 Mar 2026` | the order number (dedupe key), the date |
| Column headings | `Line Description Ordered Supplied Price Amount` | each column starts 12 pt left of its heading - numbers are right-aligned. Found on each table, never fixed |
| Category heading | a row of text alone ("Dairy, Eggs & Fridge") | `Category`, a hint only: names vary between orders |
| A line | `4  Woolworths black beans pdm 420g  1  1  $1.00  $1.00` | supplied, the shelf price, the amount |
| GST marker | `* Earth choice floor cleaner 750ml` | marker stripped, `Gst` kept as a household hint |
| Weighed meat | numbered row with `$13.00/Kg` and no amount, then `~ name` with `1 @ 700g` and `$9.10` | one line: weight 700 g, $13.00/kg, $9.10 |
| Substitutions | a table of its own: the original with `0` supplied and `$0.00`, then `(Sub) replacement` | two lines: the original "not supplied"; the substitute at its shelf price, its amount the original's price (the difference is `DiscountCents`) |
| Totals | `Sub Total`, `Paper bags`, `Collection fee`, `Service fees`, `Invoice Total`, `Paid Amount`, sometimes `Refund Amount` | fees named by the label just left of their amount; a refund is weighed meat supplied under the maximum |

No Woolworths invoice prints a stockcode, so its lines match by alias; `StoreCode` is there for Costco's item numbers.

## Matching a line

`ReceiptMatching.Match`, in order:

1. **The store's code** on a product at the receipt's store - certain.
2. **An alias**: the description, spaces normalised and case ignored (`ProductAliasRules.Key`), matched before. Certain;
   an alias to a product since deleted matches nothing.
3. **Suggestions**, up to four: products at the store sharing the most words (Dice), then ingredients whose every word is in
   the description ("black beans" in "Woolworths black beans pdm 420g") to make a product of. Words drop the store's brand
   words, `pdm`, pack words and sizes, and fold plurals. Ties by name, so every phone lists the same.

Matched any way but 1 or 2, importing writes the alias - **the second receipt matches by itself**. Matching a description
to another product later moves its alias (one row per description, the later match wins).

## Importing

`ReceiptImport` (`Shared.Data/Receipts/ReceiptImport.cs`) loads the review from either database and writes it as one
`WriteAllAsync` - all or none:

| Written | When |
|---|---|
| `Receipt` - store, order number, date, sub total, fees, total, paid, refund, the day imported | always |
| a `ReceiptLine` per line - description, store code, category, supplied, shelf price, amount, weight, per kg, discount, product, kind | every line, matched or not; and one per fee that cost something |
| a `Product` | each line the planner made one for: under an ingredient, or none ("not food") |
| a `ProductAlias` | each line matched any way but its alias or code |
| a `PriceObservation`, `Source = Receipt`, on the receipt's date | each matched line that came home: the shelf price; weighed meat, what that tray cost and its per-kg price |
| a `Bought` `StockMovement`, on the receipt's date | each food line that came home: packs supplied × the product's pack, or weighed meat in grams by its weight |

A line's kind (`ReceiptLineKind`): **Food** - its product has an ingredient; **Household** - its product has none; **Fee**;
**Ignored** - the planner left it out (nothing written but the line). An unmatched line is imported, prices nothing and
stocks nothing; it is Household when GST-marked or under a household-sounding heading, else Food. A line not supplied
writes no price and no stock. **Staples get no movement** - their stock is the hand-set state ([the pantry](../pantry/pantry.md)).

## A receipt and a shopping-list tick for the same purchase

The rule the scope left to this slice (`ReceiptStock`, `StockMovement.Counting`):

- **The receipt wins.** A tick is the shopper's note in the shop - "beans, in the trolley", one pack; the receipt is what
  came home - two tins, 700 g. Counting both would put the beans in the pantry twice.
- **Which tick:** a bought tick of the same ingredient, ticked from 7 days before the receipt's date to 2 after (an online
  order ticked when placed; ticked in the shop, the invoice dated at pickup), not replaced already - the nearest in date,
  then the later. One tick per line, one line per tick.
- **How:** the receipt's movement names the tick in `StockMovement.CoversItemId`. From then on that tick's movements do not
  count - on every device, and **whenever they arrive**: the cover names the tick, whose id is derived, so a second phone's
  pack for the same tick, synced after the receipt, is silenced too. The tick stays ticked.
- **The shopping list** reads the pantry without its own ticks' movements, and now without a purchase that replaced one
  of them (`PantryBook.LoadAsync(..., ignoringItems)`), so the line stays "bought", not "in the pantry", and its pack stays
  in the Spend.
- **The planner can turn it off** in the review - two shops of beans in one week are two purchases.
- **Not covered:** a tick still offline on another phone when the receipt is imported is not seen, so not replaced; both
  then count. The review is normally done at home, after both phones have synced. The pantry's sheet corrects it in two taps.

## What the planner sees

**Receipts** in the menu (`/receipts`). "Pick a Woolworths invoice": the file is read on the phone and the review opens -
the store and date, the order number, the sub total, fees and total, and "The lines add up to the sub total" (or, in red,
both figures and that it cannot be imported). "Show the invoice" draws its pages. Then a card per line: its description,
"2 × $1.00 = $2.00" (or "700 g at $13.00/kg = $9.10", "not supplied", "a substitute"), a green "matched" chip when its alias
matched, an "Is it Black beans 420g?" button for the best suggestion (one tap confirms), and **It is** - a native select:
the suggestions, the store's products, "a new product of" each ingredient, "a new product, not food", "leave it out". A new
product shows its name and pack size, read from the description ("420g", "= 420 g"). A food line whose purchase a tick
noted shows "Replaces the shopping list's tick of Thu 22 Oct (500 g)", ticked. Under each line, what importing does:
"Prices it at $1.00 a pack; 840 g of Black beans into the pantry." **Import 19 lines** (disabled while the totals fail, a new
product lacks a name or pack, or the order is in already) then "Imported 19 lines: 12 prices, 8 into the pantry, 3 not
matched." Picking an order already imported shows "This order was imported already, on Thu 22 Oct 2026. It cannot be
imported twice." Below, the imported receipts: store, date, total, "19 lines · 3 not matched · imported Thu 22 Oct".

First receipt: every line is a tap or two. The second: the products bought before match by themselves.

## As seen

2026-10-22, `.claude/tools/chrome-receipts-check.mjs` with the seven real invoices, two headless Chromes on fresh profiles -
A desktop-sized, B phone-sized - every check passing. A adds beans and chicken, plans a bean stew (500 g) tomorrow and ticks
the beans bought: a 500 g movement naming the tick. B picks a synthetic invoice dated today (generated by
`lib/synthetic-invoice.mjs`: two tins of beans, a weighed chicken, a cleaner, bags): it adds up, nothing matches by itself,
"Show the invoice" draws its two pages. B makes a product of the beans (pack read as 420g; "Replaces the shopping list's tick
of Thu 22 Oct (500 g)" offered), of the chicken (800g, the heaviest tray) and a household one: "840 g of Beans", "700 g of
Chicken", "Not food". Imported, the server holds the receipt and its four lines (Food, Food, Household, Fee), receipt prices
$1.00, $5.00 and $9.10 at $13.00/kg, an 840 g movement naming A's tick, 700 g of chicken and three aliases. A's pantry reads
"about 840 g" (not 1.34 kg); A's list still shows the beans bought. B picks the same file: refused, naming the day. A second
synthetic order: every line matched by its alias, no tick offered. Then A imports each real invoice as it reads - all seven
add up, 19, 25, 13, 18, 16, 21 and 18 lines and their fees - and B is refused the first again. Both profiles' SQLite hold the
server's receipts, lines, aliases, prices and stock, outboxes empty.

In-process: `Tests/Receipts/WoolworthsInvoiceTests.cs` (header, lines, headings and totals; any storage order; bold once and
ligatures joined; wrapped descriptions and nearby headings; weighed meat, also in kilos; substitutions; lines over two pages;
lines it cannot place failing the totals; a wrong total; not an invoice; money as printed), `ReceiptMatchingTests.cs` (code
before alias, alias whatever its case, an alias to a deleted product, suggestions never applied, ingredients by their words,
a stable order, words, pack sizes in the ingredient's own unit), `ReceiptStockTests` (the nearest tick in the window, its
edges, packs or weight), `ReceiptRulesTests.cs` (derived ids, every field accepted and what each refuses, a cover silencing a
tick's movements whenever they arrive) and `ReceiptSyncTests.cs` (through the real host: what an import writes and the next
receipt matching by itself, refused twice - also under another store -, never imported when it does not add up, two phones
importing one receipt, a receipt and a tick counting once - and the list still reading it as bought - and two purchases when
the planner unticks the cover the review offered), and `ReviewChoiceTests.cs` (what the page decides from each choice and the
cover's checkbox: `ReviewChoice.Decide`).
Each was checked by breaking the code: no cover rule, no wrap distance, no fee check, ligatures spaced, no alias step,
weighed meat at no price, a minted stock id, the list counting a covering purchase, no bold dedupe, the window a day short.

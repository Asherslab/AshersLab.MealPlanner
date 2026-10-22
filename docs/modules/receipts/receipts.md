---
title: Receipts
kind: reference
status: current
module: receipts
verified: 2026-10-23
code:
  - AshersLab.MealPlanner.Shared.Domain/Receipts
  - AshersLab.MealPlanner.Shared.Domain/Stores/StoreRules.cs
  - AshersLab.MealPlanner.Shared.Data/Receipts
  - AshersLab.MealPlanner.Shared.Data/Pantry/StockMovement.cs
  - AshersLab.MealPlanner.WASM/Features/Receipts
  - AshersLab.MealPlanner.WASM/wwwroot/js/receipt-pdf.js
  - AshersLab.MealPlanner.WASM/wwwroot/lib/pdfjs
  - AshersLab.MealPlanner.WASM/wwwroot/lib/tesseract
  - AshersLab.MealPlanner.Tests/Receipts
  - .claude/tools/chrome-receipts-check.mjs
  - .claude/tools/chrome-costco-check.mjs
  - .claude/tools/lib/synthetic-invoice.mjs
  - .claude/tools/lib/synthetic-costco.mjs
  - .claude/tools/chrome-ereceipt-check.mjs
  - .claude/tools/lib/synthetic-ereceipt.mjs
---

# Receipts

A Woolworths invoice or eReceipt, or a Costco receipt, read on the phone, its lines matched to products, and imported: prices from what
was paid, stock from what came home, and a way for the next receipt to match by itself - Costco's item numbers, Woolworths'
descriptions. Read before touching a reader, the import, the matching, or anything that adds stock from a purchase.

## Traps first

- **Read by position, never in the order the PDF stores its text.** On a real Woolworths invoice the totals' labels come out
  before the line table and their values after it. `PdfRows.Of` (`Shared.Domain/Receipts/PdfText.cs`) sorts runs into rows by
  baseline; `WoolworthsInvoiceTests.Reading_order_is_position...` shuffles them.
- **Bold is printed twice.** The invoice fakes bold by overprinting: every heading and label arrives as two identical runs
  at one spot. Undeduplicated, "Dairy" reads "DairyDairy". `PdfRows.Of` drops a run repeating another's text and position.
- **Ligatures split words** - "chicken breast fi llet" is three runs that touch. Runs less than a point apart are joined
  without a space (`PdfRows.Join`).
- **A long description wraps above and below its numbers.** The row with the line number and amounts can have no text of
  its own, half its description 3.8 pt above and the rest 4.5 pt below; category headings are rows of text too, 12-17 pt
  away. Text within `WoolworthsInvoice.WrapDistance` (6.5 pt) of a numbered row is its description; anything further is a
  heading. Rows are 12.7 pt apart.
- **A Costco receipt is a picture, one bitmap shown across two pages.** Each page of the Costco app's PDF is a window onto the
  same image, so a line falls across the page break. `receipt-pdf.js` reads **the bitmap itself** (pdf.js's decoded image
  XObject, deduplicated across pages), never the rendered pages: no line is ever cut.
- **OCR misreads the amount column, and only that** (measured below): the GST flag runs into the amount (`49.99 1` reads
  `49.991`), the decimal point is lost (`6.99 0` reads `6990`), a discount's `3.00 - 0` reads `300-0`. Every amount has two
  decimals and the flag is one digit, so `CostcoReceipt` reads the amount with the flag required first
  (`AmountWithFlag`); an amount it still cannot read is worked out from the quantity and price and marked to check.
- **A receipt that does not add up is never imported** (`ParsedReceipt.TotalsProblem`): every line's own sums (packs × shelf
  price, less its discount, is its amount - `ParsedLine.SumProblem`; weighed meat excepted), the lines make the sub total,
  the sub total and fees the total, and where the receipt prints its items sold (Costco), the lines' quantities make it. A
  misread price would otherwise be recorded as the product's price. Woolworths lines the reader cannot place are not guessed
  at - the totals then fail. A Costco receipt that fails is corrected in the review (below) and checked again.
- **Every id is derived, never minted** (`ReceiptRules`): a receipt's from its store and order number, a line's from the
  receipt and its position, its price and stock movement from the line, an alias from the store and its text. So two phones
  importing one receipt offline make the same rows - one receipt, one lot of stock - and "already imported" is one lookup.
  **The store is the reader's, never the planner's**: a Woolworths invoice is the seeded Woolworths, a Costco receipt the
  seeded Costco (`StoreRules.Woolworths`/`Costco`, the same ids in every database), with no store to pick in the review - a
  store picked differently would make a different receipt id and let the same purchase in twice. For the same reason
  "already imported" also matches the order number under any store (`ReceiptImport.LoadAsync`). A deleted seeded store
  refuses its receipts. The price: a deleted receipt's id is a tombstone forever, so nothing offers to delete one. New
  products are the exception (minted): two phones each making a product for the same line offline make two.
- **A suggestion never matches by itself.** Only the store's code and an alias do; a wrong automatic match would price and
  stock the wrong thing with nothing on screen to say so.
- **The file is never stored or uploaded.** `wwwroot/js/receipt-pdf.js` keeps the open PDF and its picture only while the
  review is up (to draw them) and drops both on import, cancel, or leaving the page. OCR runs in the browser. Only what was
  read becomes ops.
- **Two reads can overlap, and the later pick wins.** A file picked twice, or a change event fired twice, starts a second
  read while the first is in pdf.js. Each read holds its own document and destroys only its own; the page numbers each
  pick and passes the number to `read`, and a read of an earlier pick gives way (`receipt-read-superseded`) and is
  ignored rather than reported. Before, the second read destroyed the first's document mid-read ("Worker task was
  terminated"), the first's failure then destroyed the second's, and both said "That file is not a PDF this device can
  read." for a good file (reproduced in Chrome by firing the change event twice). The order picks reach pdf.js is not
  the order they were made - a larger file takes longer to copy - which is why the number comes from the page. A read
  that fails for real logs pdf.js's own message (`ReceiptsPage`, a warning) behind that sentence.
- **Never `page.getTextContent()` - Safari cannot run it.** It loops over a `ReadableStream` with `for await`, and
  Safari's `ReadableStream` has no async iterator (Safari 26.3 on a Mac: "undefined is not a function"); the vendored
  pdf.js does not polyfill it. `receipt-pdf.js` `textItems` reads `page.streamTextContent()` with a reader instead -
  the same items. Every Woolworths PDF failed in Safari until 2026-10-23 with "That file is not a PDF this device can
  read."; seen fixed in Safari itself through `safaridriver` against the add-on sim, the published 0.0.46 image failing
  the same way first. Any new pdf.js call: check it for `for await` over a stream before relying on it in Safari.

## Reading a Woolworths invoice

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

No Woolworths invoice prints a stockcode, so its lines match by alias.

## Reading a Woolworths eReceipt

A shop at the till, not online: the Woolworths app saves its **eReceipt** as a text PDF of the till roll - one tall page,
monospaced, every word its own run. `WoolworthsEReceipt.Parse` (`Shared.Domain/Receipts/WoolworthsEReceipt.cs`), tested on
`Tests/Receipts/SyntheticEReceipt.cs` (the real layout's positions, nothing from a real receipt):

| Part | As printed | Read as |
|---|---|---|
| Header | the store, `TAX INVOICE - ABN ...`, then `Description ... $` | where the items start |
| An item | `WW Full Cream Milk 2L  3.40`; `#` in front for GST, `^` for a promotional price | one pack at its amount; markers stripped, `#` kept as `Gst` |
| More than one | the name alone, then `Qty 2 @ $9.00 each  18.00` | 2 packs at $9.00 |
| Weighed | the name alone, then `1.250 kg NET @ $4.00/kg  5.00` | one line: 1250 g at $4.00/kg, $5.00 - like the invoice's weighed meat |
| An offer | `COLA MULTIBUY OFFER  -4.00`, a row of its own | not a line: off the item before it (`DiscountCents`), as Costco's discounts are. Its price stays the shelf price |
| A name alone that nothing follows | `^Promotional Price`, the legend under the items | dropped |
| A name alone followed by a row it does not know | | **refused**, naming both: how many came home is not guessed |
| Sub total | `12 SUBTOTAL $64.20` | the sub total, and the count as `ItemsSold` (a weighed item is one) |
| Between it and the total | a rewards discount, if any | a fee, named as printed |
| Total | the first `TOTAL` after the sub total | the total; the payment and GST blocks after it are not read |
| Stamp | `WOOLWORTHS 1234`, `POS 004 TRANS 5108 10:41 12/03/2026` | the date; the receipt number `2026-03-12 10:41 1234 004 5108` - the dedupe key |

Its descriptions are the till's abbreviations ("Choc Chip Bscts 300g"), not the online invoice's, so the first eReceipt
teaches aliases of its own. The paper bag is an item (`#Woolworths Paper Bag  0.25`), not a fee as on the invoice: GST-marked,
it is imported as household when left unmatched. The reader is chosen by what the file holds: the invoice's order-number
header, else the eReceipt's headings, sub total and stamp, else (no text) a Costco picture.

## Reading a Costco receipt

A PDF with no text at all is a picture: `receipt-pdf.js` `ocr` takes the bitmap and runs **tesseract.js in the browser**,
vendored under `wwwroot/lib/tesseract` (tesseract.js 6.0.1, its LSTM cores with and without SIMD, the `eng` model
`4.0.0_best_int`; Apache-2.0, `NOTICE` says which file is which). About 7 MB, downloaded only when a picture is first read,
then cached by the browser. It reads the bitmap at its own size (780 px wide), one block of text (`psm 6`), and hands .NET
each line's text and where it is (`OcrJson`). `CostcoReceipt.Parse`:

| Part | As printed | Read as |
|---|---|---|
| Header | logo, address, `TAX INVOICE ($)`, `Member ...` | ignored; a line naming `member`, `tax invoice` or `abn` starts the items |
| An item | the name; optionally pack size and logistics codes (`6X500G MP6 P200 SL300`); then `700101  1x  21.49  21.49 0` | `StoreCode` the item number, quantity, shelf price, amount, the GST flag (`Gst`); the description is the name plus any pack size from the second line - the codes dropped |
| The same item number again | scanned separately | one line: quantities and amounts added |
| A discount | `TPD -BUY 2 SAVE` / `700909  1x  3.00  3.00 - 0` (a trailing `-`, or a `TPD` name) | not a line: its amount comes off the item before it (`DiscountCents`), and a discount worked out from its price marks that item to check. Before any item, a line of its own: no packs, all discount (0 × $3.00 less $3.00 is -$3.00), so it adds up and sells nothing |
| Total | `**** TOTAL (INCL GST)  91.45` | the total, and the sub total (no fees). Its point lost (`9145`) is read as an amount's is; **one that cannot be read at all is $0.00, not a refusal** - the receipt does not add up and the review opens with the total to correct. No stamp is refused: it carries the dedupe key, which the review does not correct |
| Payment | `VISA CREDIT  91.45` | paid |
| Items sold | `TOTAL NUMBER OF ITEMS SOLD = 5` | `ItemsSold`, checked against the lines' quantities (a discount is not an item) |
| Stamp | `03/03/2026 05:07:00 PM 100 001 0002 003` | the date; with the time and the register/transaction numbers, the receipt number - the dedupe key, `2026-03-03 17:07:00 100 001 0002 003`. The file's name also encodes it, but a file can be renamed |

The price a discounted line records is the **shelf price**, not what the deal made it - like a Woolworths substitute; the next
shop pays the shelf price. Its amount is what was paid, so the receipt's totals hold. In a column of digits, OCR's `O` is
read as `0` and `l`, `I`, `|` as `1`, and an item row OCR ran together (`700202 1x8.99 8.99 0`) is still an item: one that did not
match would become the next item's name and vanish - a line the review cannot correct because it never existed.

### Why in the browser

Measured on 2026-10-22 against the two real Costco receipts (September-October 2026, in `private/receipts/`), by hand-typed
ground truth of the 47 lines that matter - names, codes lines, item rows, totals, items sold, stamp - kept outside the repo:

| | Raw OCR, lines exact | Characters right |
|---|---|---|
| tesseract.js, bitmap at its own size | 37 of 47 | 98.5% (14 wrong of 931) |
| the same, scaled 2× or 3× | 30-31 of 47 | 97.0% |

At its own size every item number, name, unit price, total and date read exactly; 9 of the 10 inexact lines were the amount
column's traps above, the tenth a lost space in the footer. Through `CostcoReceipt.Parse`, **both receipts read every field
right, add up and match their items sold with no correction.** Time per receipt in a fresh headless Chrome profile, the
engine's download included: 1.4-1.7 s at desktop speed, 2.7-2.9 s with the CPU slowed 4×, 4.7-5.0 s slowed 8×. So the
fallbacks (Tesseract on the server, or a vision model) were not built. A real iPhone has not been timed; if it is slow, the
first place to look is the non-SIMD core older Safari falls back to.

## Matching a line

`ReceiptMatching.Match`, in order:

1. **The store's code** on a product at the receipt's store - certain. Costco's item number: a product made from a Costco
   line carries it, so **the next receipt matches by its item number**.
2. **An alias**: the description, spaces normalised and case ignored (`ProductAliasRules.Key`), matched before. Certain;
   an alias to a product since deleted matches nothing.
3. **Suggestions**, up to four: products at the store sharing the most words (Dice), then ingredients whose every word is in
   the description ("black beans" in "Woolworths black beans pdm 420g") to make a product of. Words drop the store's brand
   words, `pdm`, pack words and sizes, and fold plurals - the same way from either side, so "Peach" meets "peaches" and
   "Bay leaves" meets "bay leaf" (`Singular`: -ies and -ie to -y, -oes, -ches/-shes/-sses/-xes, then -s, and a short list
   of -ves words, since "olives" is not "olif"). Ties by name, so every phone lists the same.

Matched any way but 1 or 2, importing writes the alias - unless the product will match by the line's code next time (a new
product made from a coded line). A Costco line matched to an existing product with no code, or another code, teaches the
alias. Matching a description to another product later moves its alias (one row per description, the later match wins).

A new product's pack size is read from the description (`ReceiptMatching.PackGuess`): the last size printed in the
ingredient's kind of unit - "500g - 800g" is the heaviest tray; "EGGS 24PK 1.4KG" is 24 each, or 1400 g.

## Importing

`ReceiptImport` (`Shared.Data/Receipts/ReceiptImport.cs`) loads the review from either database and writes it as one
`WriteAllAsync` - all or none:

| Written | When |
|---|---|
| `Receipt` - store, order number, date, sub total, fees, total, paid, refund, the day imported | always |
| a `ReceiptLine` per line - description, store code, category, supplied, shelf price, amount, weight, per kg, discount, product, kind | every line, matched or not; and one per fee that cost something |
| a `Product` | each line the planner made one for: under an ingredient, or none ("not food"); with the line's code |
| a `ProductAlias` | each line matched any way but its alias or code, unless its code will match next time |
| a `PriceObservation`, `Source = Receipt`, on the receipt's date | each matched line that came home: the shelf price; weighed meat, what that tray cost and its per-kg price |
| a `Bought` `StockMovement`, on the receipt's date | each food line that came home: packs supplied × the product's pack, or weighed meat in grams by its weight - **only while "Add this shop's food to the pantry" is on** |

**An old shop is imported for its prices only.** The review's "Add this shop's food to the pantry" starts on for a shop in
the last week and off for an older one (`ReceiptStock.IntoPantryByDefault`, 7 days). Its food is long eaten, but nothing
planned since its date would draw it down, so on its old date it would sit in the pantry and every shopping list after
would subtract it. Off, the import writes everything but the stock - receipt, lines, products, prices, aliases - each line
says "Not into the pantry", and no shopping-list tick is offered to replace. The planner can turn it either way.

A line's kind (`ReceiptLineKind`): **Food** - its product has an ingredient; **Household** - its product has none; **Fee**;
**Ignored** - the planner left it out (nothing written but the line). An unmatched line is imported, prices nothing and
stocks nothing; it is Household when GST-marked or under a household-sounding heading, else Food. A line not supplied
writes no price and no stock. **Staples get no movement** - their stock is the hand-set state ([the pantry](../pantry/pantry.md)).

## A receipt and a shopping-list tick for the same purchase

The same for both stores (`ReceiptStock`, `StockMovement.Counting`):

- **The receipt wins.** A tick is the shopper's note in the shop - "beans, in the trolley", one pack; the receipt is what
  came home - two tins, 700 g. Counting both would put the beans in the pantry twice.
- **Which tick:** a bought tick of the same ingredient, ticked from 7 days before the receipt's date to 2 after (an online
  order ticked when placed; ticked in the shop, the invoice dated at pickup), not replaced already - the nearest in date,
  then the later. One tick per line, one line per tick.
- **How:** the receipt's movement names the tick in `StockMovement.CoversItemId`. From then on that tick's movements do not
  count - on every device, and **whenever they arrive**: the cover names the tick, whose id is derived, so a second phone's
  pack for the same tick, synced after the receipt, is silenced too. The tick stays ticked.
- **The shopping list** reads the pantry without its own ticks' movements, and without a purchase that replaced one of them
  (`PantryBook.LoadAsync(..., ignoringItems)`), so the line stays "bought", not "in the pantry", and its pack stays in the
  Spend.
- **The planner can turn it off** in the review (`ReviewChoice.Decide`) - two shops of beans in one week are two purchases.
- **Not covered:** a tick still offline on another phone when the receipt is imported is not seen, so not replaced; both
  then count. The review is normally done at home, after both phones have synced. The pantry's sheet corrects it in two taps.

## What the planner sees

**Receipts** (`/receipts`; on a phone More → Receipts, or Today's "Import a receipt"). "A Woolworths invoice (the PDF from the order's page), a Woolworths eReceipt (the PDF
the Woolworths app saves after a shop in store) or a Costco receipt (the PDF the Costco app saves)". A Woolworths invoice or
eReceipt opens its review at once (an eReceipt worded "receipt", not "order"); a Costco receipt shows "Reading the receipt's
picture… 45%" for a few seconds first. The review: the store and date, the order or receipt number, the totals ("Sub total
$16.10 · Paper bags $2.00 · Total $18.10", or Costco's "Total $91.45 · 5 items sold"), and "The lines add up to the sub total"
/ "...to the total, and to the 5 items sold" - or, in red, what does not add up. "Show the invoice/receipt" draws its pages.
Then a card per line: on a Costco receipt **the strip of the receipt it was read from, above it on a phone, beside it on a
desktop** - its name, codes and numbers as printed, and a discount's line under the item it came off. Its description,
"#700303 · 2 × $6.99 = $10.98 · $3.00 off" (or "700 g at $13.00/kg = $9.10", "not supplied", "a substitute"), a green
"matched" chip when its code or alias matched, an "Is it Black beans 420g?" button for the best suggestion (one tap confirms) -
a product, or the first time something is bought a new product of an ingredient ("Is it Curry paste?" on "Ayam Red Curry
Paste 185g", its pack read as 185 g), so a first receipt is not a dropdown per line,
and **It is** - a native select: the suggestions, the store's products, "a new product of" each ingredient, "a new product,
not food", "leave it out". A new product shows its name and pack size, read from the description ("420g", "= 420 g"). A food
line whose purchase a tick noted shows "Replaces the shopping list's tick of Thu 22 Oct (500 g)", ticked. Under each line,
what importing does: "Prices it at $1.00 a pack; 840 g of Black beans into the pantry."

**Correcting OCR** (Costco only). A line whose sums disagree is marked "Check this line: 1 × $8.99 is $8.99, not $8.39."; one
whose amount could not be read, "...worked out from the price: check it." **Correct what was read** on any line opens its
description, item number, quantity, price and amount, filled as read; **Correct** re-checks the whole receipt
(`ReceiptCorrection.Line`; the discount stays as read, so an amount under the price is never taken for one). **Correct the
total** does the same for the total, items sold and date (`ReceiptCorrection.Receipt`). The planner's choices on every line
are kept through a correction - **unless the correction made the line another item** (its item number or description
changed; `ReviewChoice.Survives`): then it is matched afresh. Kept, a choice made for the misread would import the line under
the wrong product and teach that product the line's description as an alias.

**Import 19 lines** (disabled while anything does not add up, a correction is open, a new product lacks a name or pack, or
the receipt is in already) then "Imported 19 lines: 12 prices, 8 into the pantry, 3 not matched." Picking a receipt already
imported shows "This order was imported already, on Thu 22 Oct 2026. It cannot be imported twice." Below, the imported
receipts: store, date, total, "19 lines · 3 not matched · imported Thu 22 Oct".

First receipt: every line is a tap or two. The second: the products bought before match by themselves.

## As seen

2026-10-22, against a fresh server database, two headless Chromes on fresh profiles each - A desktop-sized, B phone-sized -
every check passing:

- `.claude/tools/chrome-receipts-check.mjs` with the seven real Woolworths invoices: A adds beans and chicken, plans a bean
  stew tomorrow and ticks the beans bought. B picks a synthetic invoice (`lib/synthetic-invoice.mjs`): it adds up, nothing
  matches by itself, "Show the invoice" draws two pages; B makes products of the beans (420g, replacing A's tick) and the
  chicken (800g, the heaviest tray) and a household one. The server holds the receipt, four lines, prices $1.00, $5.00 and
  $9.10 at $13.00/kg, 840 g of beans naming A's tick, 700 g of chicken and three aliases; A's pantry reads "about 840 g". The
  same file again: refused. A second order: every line by its alias. Then all seven real invoices add up under the per-line
  check too - 19, 25, 13, 18, 16, 21 and 18 lines - and the first is refused on B.
- `.claude/tools/chrome-costco-check.mjs` with the two real Costco receipts, B's CPU slowed 4×: A adds mince and apples,
  plans a bolognese tomorrow and ticks the mince bought. B picks a synthetic Costco receipt (`lib/synthetic-costco.mjs`: one
  JPEG bitmap across two pages, item numbers new each run): read in 3.2 s, progress shown; it adds up and matches its 5 items
  sold; every item number exact; the two loaves one line, "2 × $6.99 = $10.98 · $3.00 off"; the nappies GST; six strips of
  the picture drawn. B makes products of the mince (3 kg from "6X500G", replacing A's tick) and apples, leaves the bread
  unmatched, the nappies household. The server holds the receipt under Costco, its four lines with their item numbers and the
  discount, three products carrying the item numbers, three prices, 3 kg of mince naming A's tick, and no aliases; A's pantry
  reads "about 3 kg". The same file on B: refused. A picks a second receipt printing the apples at $8.39: flagged, the apples
  line marked, import disabled, the other lines matched by item number; A corrects the amount to $8.99, it adds up, imports,
  three more prices. Both real receipts: read in 1.7 and 1.5 s, add up and match their items sold with no correction, 5
  lines each, imported. Both profiles' SQLite hold the server's receipts, lines, products, prices and stock, outboxes empty.
- `.claude/tools/chrome-ereceipt-check.mjs` with the real eReceipt: B picks a synthetic eReceipt (`lib/synthetic-ereceipt.mjs`):
  "Total $25.30 · 6 items sold", adds up, numbered by the till's stamp, worded as a receipt, five lines (the offer not one);
  imported as read. The server holds it, the bananas 1250 g at $4.00/kg, two colas at $9.00 less $4.00, milk, butter and the
  bag. The same file on A: refused. The real eReceipt: adds up to its 18 items sold, 16 lines, imported. Both profiles'
  SQLite hold the server's receipts and lines, outboxes empty.

In-process: `Tests/Receipts/WoolworthsEReceiptTests.cs` (items, counted, weighed and offer lines, totals and stamp; any
storage order; weighed items as one item sold; a rewards discount as a fee; a second row it does not know refused, a wrong
count failing the totals; no count or store number; an offer before any item; an eReceipt and an invoice each refused by the
other's reader), `Tests/Receipts/WoolworthsInvoiceTests.cs` (header, lines, headings and totals; any storage order; bold once and
ligatures joined; wrapped descriptions and nearby headings; weighed meat, also in kilos; substitutions; lines over two pages;
lines it cannot place failing the totals; a wrong total; not an invoice; money as printed), `CostcoReceiptTests.cs` (synthetic
OCR lines with the measured misreads: items, repeats merged, a discount off the item before it, total, items sold, stamp and
number; every way the amount ran into the flag; where each line is on the picture; a misread amount, a wrong total and a
wrong count each failing; an unreadable amount worked out and marked, its discount's too; a run-together row; a
discount by its minus or its name alone, and one before any item; a total without its point, and an unreadable one left
to correct; letters in digits; not a receipt, no date),
`ReceiptCorrectionTests.cs` (a corrected amount adding up, an amount under the price not taken for a discount, a discount
kept, fields refused and why, the total, items sold and date), `ReceiptMatchingTests.cs` (code before alias, alias whatever its
case, an alias to a deleted product, suggestions never applied, ingredients by their words, a stable order, words, pack sizes
in the ingredient's own kind of unit), `ReceiptStockTests` (the nearest tick in the window, its edges, packs or weight),
`ReceiptRulesTests.cs` (derived ids, every field accepted and what each refuses, a cover silencing a tick's movements whenever
they arrive), `ReceiptSyncTests.cs` (through the real host: what an import writes and the next receipt matching by itself,
refused twice - also under another store -, never imported when it does not add up, two phones importing one receipt, a
receipt and a tick counting once - and the list still reading it as bought - and two purchases when the planner unticks the
cover the review offered), `CostcoSyncTests.cs` (item numbers becoming product codes and the next receipt matching by them, an
alias taught only for a product without the code, a purchase replacing its tick, refused twice) and `ReviewChoiceTests.cs`
(what the page decides from each choice and the cover's checkbox, and a choice surviving a correction only as the same
item).

Each was checked by breaking the code: no cover rule, no wrap distance, no fee check, ligatures spaced, no alias step,
weighed meat at no price, a minted stock id, the list counting a covering purchase, no bold dedupe, the window a day short,
no order-number lookup across stores, the cover's checkbox ignored, the GST flag optional, the discount not taken off, no
line-sum check, no items-sold check, a correction working the discount out, the alias only for codeless lines, only the last
pack size tried.

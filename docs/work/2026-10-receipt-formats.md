---
title: Receipt formats
kind: discussion
status: accepted
module: receipts
opened: 2026-10-20
verified: 2026-10-20
code: []
---

# Receipt formats

What a Woolworths invoice and a Costco receipt actually contain, written from nine real ones (seven
Woolworths, June–September 2026; two Costco, September–October 2026) kept in `private/receipts/`.
The parsers are written against this; their tests use **synthetic** fixtures reproducing these
layouts, never the real files.

## Woolworths online-order invoice

**Text PDF** (produced by PDFsharp; 18 embedded fonts). Text extraction works; no OCR needed. Two
pages: lines on page 1, totals on page 2 — or both on page 1 for a short order.

Header (every page): "Tax Invoice", ABN, `Invoice/Order Number: <9 digits>`, `Customer: <name>`,
`Date: <d Mon yyyy>`, `Pickup: <hhmm - hhmm>`. The order number is the dedupe key.

Lines are a table — `Line | Description | Ordered | Supplied | Price | Amount` — grouped under
**category headings** that are rows of their own (Bakery, Dairy Eggs & Fridge, Freezer, Fruit & Veg,
Pantry, Meat, Lunch Box, Household Cleaning, Toiletries, …). Category names vary between orders
("Dairy" vs "Dairy, Eggs & Fridge"), so treat them as a hint, not a key. The `Line` numbers are not
in display order.

| Case | Looks like | Handle |
|---|---|---|
| Normal | `4  Woolworths black beans pdm 420g  1  1  $1.00  $1.00` | Pack size is in the description (`420g`, `2l`, `4 pack`, `50 pack`, `9g x 2 pack`, `each`). Parse it for `PackQuantity`. |
| Multiple | `Abe's bagels the natural pdm 4 pack  3  3  $5.50  $16.50` | Qty 3 of one product. |
| Variable weight (meat) | Two rows: `Woolworths rspca approved chicken breast fillet skinless small 459g - 749g  1  $14.50/Kg` then `~ <same name>  1 @ 662g  $14.50/Kg  $9.60` | The description wraps across lines. The `~` row has the supplied weight and the real amount. Record per-kg price and weight. |
| GST item | Description starts with `* ` | Strip the marker. Mostly household items — a useful hint for `Kind = Household`. |
| Substitution | A separate "Substitutions" table after the main one: original row with Supplied `0` and `$0.00`, then `(Sub) <replacement>` with the real amount | Record the supplied product. The original row costs nothing. |
| Out of stock | Supplied `0` | No price observation (it was not bought). |

Totals block: `Sub Total`, `Paper bags`, `Collection fee`, `Service fees`, `Invoice Total`, GST,
`Paid Amount`, sometimes `Refund Amount` (variable-weight items supplied under the maximum are
refunded). **Validate: Σ line amounts = Sub Total.** Refuse to import silently on mismatch — show both
figures.

Text extraction order is not visual order: on one sample the totals' labels came out before the line
table and their values after it. Parse by position (pdf.js gives x/y per text item), not by reading
the extracted string top to bottom.

## Costco receipt (from the Costco app)

**Image-only PDF** — no fonts, two embedded images (the receipt is a rendered bitmap split across two
pages, and **a line can be cut in half at the page break**). Needs OCR. The render is clean (no
photo noise, no skew), which is the easy case for OCR.

Header: Costco logo, legal entity, warehouse address, ABN, `TAX INVOICE ($)`, `Member <number>`.

Each item is three lines:

```
CENTRAL AGRI BEEF MINCE            ← name, upper case, abbreviated
8X250G MP8 P256 SL365              ← optional: pack size + logistics codes (MP, P, L, C, D, SL) — ignore the codes
195032    1x    22.99    22.99 0   ← item number, qty, unit price, amount, GST flag (1 = GST, 0 = none)
```

- **The item number is a stable product key** — better than anything on the Woolworths invoice.
  Match on it first; the name is for humans.
- Pack size is in the name or the second line: `1KG`, `1.4KG`, `700G`, `8X250G`, `30PK 1.75KG`,
  `18PK`, `148CT`, `100G`.
- **Discount lines**: `TPD -PICK 'N' MIX` / `<item>  1x  7.49  7.49 - 0` — a trailing `-` means
  negative. It applies to the line(s) immediately before it (seen after two loaves of bread, a
  buy-two deal). Allocate it back across the preceding item(s) of the same product so their unit
  price is right; do not record it as its own product.
- The same product appears as separate lines when scanned separately — merge by item number.
- The GST flag sits right against the amount (`59.99 1`) — an OCR trap: `59.991`.

Footer: `**** TOTAL (INCL GST)`, `TOTAL (EXCL GST)`, `GST Amount`, payment method line, a GST code
table, `TOTAL NUMBER OF ITEMS SOLD = n`, date/time `dd/mm/yyyy hh:mm:ss AM` plus a register/
transaction number. **Validate twice: Σ amounts (after discounts) = total incl. GST, and line count =
items sold.** Date + transaction number is the dedupe key; the file name (`receipt_<digits>.pdf`)
also encodes it but is not to be trusted.

Unknown: whether the Costco website's receipt download differs from the app's (the owner had no
login to hand on 2026-10-20). If it is a text PDF or a CSV, prefer it.

## Matching lines to products

1. Exact match on store + store code (Costco item number; Woolworths stockcode once known).
2. Exact match on store + a previously seen alias (the raw description).
3. Otherwise: suggest products/ingredients by fuzzy name, and let the planner pick — or create a
   product under an existing or new ingredient, or mark the line Household / Ignored. The raw
   description becomes an alias, so the next receipt matches at step 2.

The review screen shows the receipt image beside the parsed lines, flags every unmatched or
low-confidence line, and will not save while the totals check fails unless the planner explicitly
corrects a line.

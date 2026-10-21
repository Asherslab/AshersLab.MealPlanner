---
title: Receipt formats
kind: discussion
status: folded
module: receipts
opened: 2026-10-20
closed: 2026-10-22
verified: 2026-10-22
folded_into:
  - docs/modules/receipts/receipts.md
code:
  - AshersLab.MealPlanner.Shared.Domain/Receipts
---

# Receipt formats

What a Costco receipt contains and its traps, written from two real ones (September-October 2026) kept in
`private/receipts/`: the research slice 13 builds on. The parser will be written against this; its tests use **synthetic**
fixtures reproducing the layout, never the real files.

The Woolworths invoice (seven real ones, June-September 2026) and matching lines to products landed with slice 12 on
2026-10-22 and are in [receipts](../modules/receipts/receipts.md) - the layout, its traps, the alias, and what an import
writes. Costco's lines go through the same matching and import (`ReceiptMatching`, `ReceiptImport`): only the reader is new.
Its item number fills `ParsedLine.StoreCode`, which matches before an alias.

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

## For the review

The research said the review shows the receipt beside the parsed lines. For Woolworths, "Show the invoice" draws the
PDF's pages; a Costco receipt is an image PDF and can be drawn the same way (`receipt-pdf.js` `show`). It also said a
review that fails its totals cannot be saved "unless the planner explicitly corrects a line"; the Woolworths import
refuses it outright, since all seven real invoices add up. OCR will misread digits, so slice 13 likely needs that
correction: an edit of a line's amount in the review, re-checked against the totals.

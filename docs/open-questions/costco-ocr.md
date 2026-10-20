---
title: Reading Costco receipts with OCR in the browser
kind: investigation
status: open
module: receipts
opened: 2026-10-20
verified: 2026-10-20
code: []
---

# Reading Costco receipts with OCR in the browser

Costco's app exports receipts as image-only PDFs (see [receipt formats](../work/2026-10-receipt-formats.md)).
Is tesseract.js, run in the browser, accurate enough that the planner rarely has to correct a line?
The owner's call on 2026-10-20 was "do it in the browser if it's reliable enough". Settled by slice 13
of [the v1 scope](../work/2026-10-v1-scope.md).

## How to judge "reliable enough"

Against the two real receipts in `private/receipts/receipt_*.pdf` (not committed), plus any more the
owner adds:

- Every item number, quantity and amount read exactly — the totals check and the items-sold count
  both pass with no manual correction.
- The GST flag is separated from the amount (`59.99 1`, not `59.991`).
- The discount line's trailing `-` is read.
- A line cut across the page break is reassembled.
- Time on a phone: acceptable if under ~10 seconds for a typical receipt, with progress shown.

Tuning that is allowed: render the PDF page at a higher scale before OCR, restrict the character
whitelist for the number columns, run the numeric columns as a separate pass.

## If it is not reliable enough

In order of preference:

1. **Server-side Tesseract** (native, in the image) — better accuracy controls, costs Pi CPU for a few
   seconds per receipt. The image is processed in memory and discarded, as in the browser.
2. **A vision model** (e.g. the Claude API) server-side, with an API key from configuration. Most
   accurate, costs money per receipt, sends the receipt off-site — needs the owner's agreement.

The owner explicitly ruled out iOS-only OCR paths (Shortcuts / Live Text): the planner also uses a
desktop.

Record the answer in `docs/modules/receipts/` and delete this file.

---
title: Version 1 acceptance walk-through
kind: handover
status: in-progress
module: meta
opened: 2026-10-22
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.WASM/Pages/Home.razor
  - AshersLab.MealPlanner.WASM/Layout/MainLayout.razor
  - AshersLab.MealPlanner.WASM/Features/Meals/MealIngredientSheet.razor
  - AshersLab.MealPlanner.WASM/Features/Planning/CalendarEntrySheet.razor
  - AshersLab.MealPlanner.WASM/Features/Receipts/ReceiptsPage.razor
  - AshersLab.MealPlanner.Shared.Domain/Meals/MealCosting.cs
---

# Version 1 acceptance walk-through

The scope doc's definition of done, driven start to finish on a phone-sized screen on 2026-10-22: what worked, what was
fixed on the way, what is left, and **the owner's remaining checks, in order**. Read this before handing the app to the
household. The durable behaviour is in `docs/modules/`; this is the record of the walk and the list of what only the owner
can do.

> Version 1 is done when the planner can, on her phone: enter a new meal, plan next week, see what it costs, take the
> shopping list to Costco and Woolworths, and import both receipts afterwards. - [the scope](2026-10-v1-scope.md)

## How it was driven

The app from Rider (`AshersLab.MealPlanner.AppHost: https`) on a **new, empty server database**, so the walk started
where the household will: seeded stores, categories, meal groups and slots, and nothing else. Two headless Chromes on
fresh profiles: **A, the phone** - 390 × 844, touch events, every tap at the centre of what a finger would hit and every
word typed as key input; **B, a desktop** at 1440 × 900, watching. Native `<select>`s and the file picker were set the way
the OS picker sets them (the value and a `change` event; `DOM.setFileInputFiles`). Made-up data throughout: three people,
a chicken curry, leftovers, a takeaway, and a synthetic Costco receipt and Woolworths invoice built by
`.claude/tools/lib/synthetic-*.mjs` to match the shopping list. One real invoice and one real Costco receipt from `private/`
were then read on the phone for layout and totals only, and cancelled - counts recorded, nothing quoted.

## The walk, step by step

| Step | What the phone did | Result |
|---|---|---|
| Open the app | Home | Was the scaffold's server check ("Nothing to plan yet"), its table scrolling sideways. **Fixed**: today's meals and four big buttons. |
| Menu | ☰ → Meals | The drawer stayed over the page it had opened. **Fixed.** |
| Enter a new meal | Meals → "Chicken curry" → Add → group, 4 serves → Save | Fine: full-screen editor, then the meal's page. |
| Its ingredients | Add ingredient → "Chicken thigh" | "No ingredient called ... - add it on the Ingredients page first": a trip away and back for every new ingredient. **Fixed**: New ingredient "Chicken thigh" → the ingredient editor → back with it chosen and the amount focused. Four ingredients entered this way, "1kg", "800ml", "500g", "200g", Enter each. |
| Its cost, unpriced | The meal page | "from $0.00 + 4 unpriced" - reads as a price. **Fixed**: "4 unpriced". |
| Plan next week | Home → This week's plan → › → Mon → Dinner + → "curry" → tap → Save | Fine. With nobody on People, "Everyone" counted as one: "3 left over" for a curry for three. **Fixed** by a home-page prompt to add the household; three people added on People. |
| A meal not made yet | Tue → Lunch + → "Leftovers" | The search showed nothing at all - no hint, no way on. **Fixed**: New meal "Leftovers" → the meal editor (Leftovers) → back with it chosen and Alex still picked. Thursday's "Takeaway pizza" ($36, feeds 3) made the same way. |
| Prices | Prices → + per ingredient → pack, price | Fine; a wrong pack corrected through the price sheet's Edit product. Curry paste left for the receipt to price. |
| See what it costs | The calendar week | "from $81.98 + 1 unpriced" - checked by hand: 1 × $19.99 + 2 × $1.50 + 1 × $22.99 + 3 × $12.00. The meal "from $3.54 a serve + 1 unpriced". The desktop's grid showed the same, synced. |
| The shopping list | Calendar → Shopping list | Costco and Woolworths, by category, packs and spare ("1 pack (1 kg spare)"), 390 px wide. |
| At Costco, no signal | Offline; tick chicken and rice; add "Toilet paper $15.49, Costco"; tick it | Instant, "Offline · 6 waiting"; back online, the desktop showed all three Bought. |
| Woolworths | Tick coconut milk and curry paste | Fine. |
| Costco receipt | Home → Import a receipt → the PDF | Read by OCR, added up; chicken matched by item number with "Replaces the shopping list's tick"; "Is it Rice?" one tap; toilet paper "a new product, not food". Imported. |
| Woolworths invoice | Same | Coconut milk offered as "Is it Coconut milk?"; curry paste - its name inside "Ayam Red Curry Paste 185g" - had to be found in the dropdown. **Fixed**: the one-tap button offers a new product of an ingredient too, pack read as 185 g. Imported. |
| Afterwards | Pantry, meals, week, on the desktop | Pantry 2 kg chicken, 10 kg rice, 800 ml, 185 g - **not doubled**: each receipt line replaced its tick. The curry "$4.57 a serve" (checked: $10.00 + $3.00 + $1.15 + $4.11 = $18.26 ÷ 4). Spend $105.07 (now the toilet paper and two 185 g jars of curry paste for the 200 g the recipe uses). |

Rows read back at the end - `Receipt`, `ReceiptLine`, every `StockMovement` with whether it covers a tick,
`PriceObservation` and `ProductAlias` - were identical in the server's SQLite, the phone's and the desktop's (2, 6, eight
movements in four tick/receipt pairs, 8, 3). Every page was measured at 390 px: none scrolls sideways. The real invoice
(19 lines) and the real Costco receipt (5 lines) both read and added up on the phone. The home prompt for an empty
household was seen on a fresh profile against an empty server, and gone once one person was added.
`chrome-calendar-check.mjs` passes with its cost assertion changed to the new "1 unpriced"; the suite passes (881 tests).

## What was fixed (each its own commit)

| Commit | Fix |
|---|---|
| `bb00262` | The menu closes on a phone when a page opens (`MainLayout` on `LocationChanged`; MudNavLink's `OnClick` never reached it). |
| `a23e3eb` | Home: today's meals and This week's plan / Shopping list / Meals / Import a receipt; server and device cards under "About this app", wrapping. |
| `c32ffb2` | A meal's new ingredient is made from the meal's ingredient sheet (`IngredientDialogs.EditAsync`, shared with the Ingredients page). |
| `42da1ae` | An incomplete cost with nothing known reads "4 unpriced", not "from $0.00 + 4 unpriced" (`Cost.Format`; tests proven by mutation). |
| `9555050` | A new meal from the calendar entry sheet. |
| `f208ea1` | Home asks for the household while nobody is on People. |
| `570f1d7` | Receipts: the one-tap "Is it ...?" for a first-time item (a new product of an ingredient), not only a known product. |

Before them, `2e6e3c6` fixed the standalone deploy's review findings (the chart's NetworkPolicy on by default and required
for a pod-network proxy, `/0` refused as a trusted proxy, the display-name header, the S3 Secret from a file).

## What is left - bigger than a walk-through fix

| What | Why it matters | Owner |
|---|---|---|
| **Open the shopping list before walking into the shop.** There is no service worker: the app opened cold with no signal does not load, and inside Home Assistant it never will ([offline sync](../modules/sync/offline-sync.md#where-offline-works)). Open, ticks offline and the sync on reconnect all work. | The scope's "used one-handed in a shop with poor reception" holds only with the app already open. A PWA service worker is the fix for the standalone deployment; ingress cannot have one. | A later slice (PWA), if the household runs standalone; otherwise a habit. |
| The plan editor's entry sheet still says "add it on the Meals page first" - no New meal there. | Building a rotation from scratch costs trips; the calendar (the weekly case) has it. Same pattern as `ca4b815`. | A later slice. |
| A line ticked Bought keeps reading its whole need ("2 packs") after a receipt bought fewer (one 185 g jar of curry paste for 200 g). | By design the list reads the pantry without its own ticks; nothing says "1 of 2 bought". Only odd when a recipe needs just over a pack. | Decide with the planner after real use. |
| OCR time on a real iPhone is unknown (headless Chrome on the Mac: 1-2 s). | A slow phone may take tens of seconds per Costco receipt. | Owner check 1. |
| Opening a PDF from the Costco app and the Woolworths order page on iOS. | Only the file picker was driven here; on iOS the PDF has to reach Files (Share → Save to Files) first. | Owner check 1. |

## The owner's remaining checks, in order

Each needs the owner's phone, the Pi or the real data; none can be done from the repo.

1. **iPhone Safari** - the steps in [sqlite-in-the-browser](../open-questions/sqlite-in-the-browser.md#the-ios-check--for-the-owner)
   (reloads, closing Safari, a restart, two tabs). Add, on the same phone: import one Costco receipt and one Woolworths
   invoice from Files and note how long the Costco one takes to read; open the shopping list, lock the phone in airplane
   mode for a few minutes, unlock, tick two lines, turn airplane mode off, and check the desktop shows them.
2. **The add-on on the Pi** - the owner prerequisites and "Still to prove on the Pi" in
   [home-assistant-ingress](../open-questions/home-assistant-ingress.md): install from the repository, the panel and a deep
   link through the real proxies, the change stream left open, "Signed in as" the HA user, first-load time and RAM, the
   companion app.
3. **A real backup and restore** - step 6 of the same doc: a partial backup of Meal Planner only, its `data/` holding
   `mealplanner.snapshot.db` and no live files, a restore, and the lost edit coming back from a browser.
4. **Wipe the test data on the Pi** before anything real goes in: uninstall the add-on **with its data** and install it
   again (deleting only `/data/mealplanner.db` is not enough - the next start restores `mealplanner.snapshot.db`, the last
   backup's copy). Each phone or computer that opened it then asks once; choose **Clear my copy → Delete them for good**,
   not Send my copy, or the test data comes straight back.
5. **The first real import of the sheet** - Import spreadsheet with the planner's latest download, answering its
   questions (units guessed, plan names matched); then People (the household), and a first week from the plan.
6. **Wipe the developer Mac's data** if it was ever pointed at real data: `~/Library/Application Support/AshersLab.MealPlanner/`
   (app stopped) and the browser profiles used for testing. From the day real meals are entered, no data is disposable:
   a schema change then needs a migration on both sides and a protocol version check (root `AGENTS.md`).

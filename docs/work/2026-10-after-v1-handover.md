---
title: After version 1 - handover
kind: handover
status: in-progress
module: meta
opened: 2026-10-22
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.WASM/Data/LocalDatabase.cs
  - AshersLab.MealPlanner.WASM/Data/LocalDataGate.razor
  - AshersLab.MealPlanner.WASM/wwwroot/js/shared-database.js
  - AshersLab.MealPlanner.WASM/Pages/Home.razor
  - AshersLab.MealPlanner.LocalData/Migrations
---

# After version 1 - handover

Where the build stands on 2026-10-22 and what comes next, for the next session picking it up. Version 1's slices
(the [scope](2026-10-v1-scope.md)'s build order, 0 to 14) are all on `master`, each reviewed, its findings fixed by the
station after it, and the whole walked on a phone-sized screen in [the acceptance walk-through](2026-10-v1-acceptance.md).
Read that doc for what is left and the owner's checks; this one adds only what happened after it.

## How it was built

One orchestrating Claude session on the nodeterm canvas opened one Claude "station" per slice, working in the main
checkout on `master`, one at a time: each armed to start on the previous station's reported success and after the
previous slice's review panel (two or three reviewers plus a verdict node). Each station's brief was a shared preamble
(read the rules, `git pull`, the slice gate in `AGENTS.md`, `report-outcome` at the end) plus the slice and the previous
verdict's findings to fix first. That pattern worked; reuse it for the next leg.

Canvas habits worth knowing:

- A node that shows `LAUNCH FAILED` starts with the `run` verb; a node opened while its project is off screen is queued
  until `run` or the project is viewed.
- A station idle without an outcome is usually waiting on a background job of its own - read its terminal before
  nudging it.
- The Rider MCP sometimes drops in a station's session; `/mcp` in that pane reconnects it.

## Next step 1 - a way out when a device's copy cannot be opened (done)

Built 2026-10-22 as specified below; how it works now is in
[the browser database](../modules/sync/browser-database.md#when-this-browsers-copy-cannot-be-opened). The choices the
brief left open: the reset deletes the whole OPFS pool, so the browser comes back as a **new device** (its old clock
state goes with the files, and reusing the id could repeat an HLC); the unsent count is the outbox, read through EF
even though migrating failed, and "no telling" when that read fails. Building it found a second, general bug:
two windows starting on a database with pending migrations both applied them and the second failed - now one
window at a time starts (the `mealplanner-db-start` Web Lock). Seen working with
`.claude/tools/chrome-device-reset-check.mjs`. What the brief asked for, kept for the record:

**Found 2026-10-22, in the owner's own Chrome.** A profile last opened during slice 5 held a browser database whose
`__EFMigrationsHistory` recorded `20261021030511_AddPriceObservations` - a draft migration a station applied and then
regenerated before committing it as `20261021031204_AddPriceObservations`. On load EF applied the committed id, `CREATE
TABLE "PriceObservation"` failed because the table existed, and Home showed twice: "This browser could not open its copy
of the data (SCHEMA_INCOMPATIBLE) ... Reset the database to recreate it". **The app has no way to do that reset** - on a
phone the household would be stuck. That profile was cleared by removing the OPFS `databases` directory from a
same-origin page that does not boot the app (`/healthz`), after which the app loaded and pulled from the server.

No committed migration was ever renamed or deleted; the server is unaffected. Stations' checks use fresh profiles,
which is why none hit it. Once real data exists the same trap is a migration edited after a device applied it - see
"This app is not in production yet" in `AGENTS.md`.

Brief:

1. When the local database fails to open (SCHEMA_INCOMPATIBLE, and any other open or migrate failure on that path),
   show the error with a **Reset this device's copy** action. Confirm first, in the server-reset dialog's style
   (`05c4a8b`): anything this device has not yet sent to the server is lost for good; everything else comes back from
   the server. Deliberate and two-step, never automatic.
2. It must work while the database cannot be opened and with other windows of the app open (`shared-database.js`
   leader and followers; the SAH pool holds access handles): release whatever holds the files, delete this device's
   database files and whatever device or sync state must go with them (decide from the code and record the choice),
   reload into a fresh copy that pulls from the server, and make the other windows reload too so none keeps writing to
   deleted files.
3. If the unsent changes can still be counted (the outbox), say how many will be lost; if not, say it cannot be known.
4. Seen working: reproduce the failure in a fresh profile (plant a mismatched migration history, or a stray table a
   pending migration creates), see the error, reset with a second window open, confirm the server's rows come back;
   check it at 390 px.
5. Tests for any pure logic; `docs/modules/sync/` says what a device does when its copy cannot be opened.

## Next step 2 - the owner's checks

In order, in [the acceptance walk-through](2026-10-v1-acceptance.md#the-owners-remaining-checks-in-order): iPhone Safari,
the add-on on the Pi, a real backup and restore, wiping the Pi's test data, the first real import of the sheet. Also
outstanding from earlier: confirm the GHCR package is Public, and delete the stray git-ignored `~` folder at the repo root.

**Docker Desktop's image pulls hang on the developer Mac** (Docker Hub and MCR alike, though the host reaches both).
Slice 13 skipped its check behind the simulated Home Assistant ingress because of it; slice 14 fetched images over the
registry API with curl instead. Restart Docker Desktop, then run `.claude/tools/ingress-sim` once to close that gap.

## Next step 3 - a frontend design leg (done)

Done on 2026-10-22 with the impeccable design skill (installed globally with frontend-design and
web-design-guidelines; impeccable.style). The owner chose the direction on the skill's decision page: "The Fridge-Door
Pad" for its form, with "The Kitchen Bench" palette and week tiles. What it is now: [PRODUCT.md](../../PRODUCT.md),
[DESIGN.md](../../DESIGN.md), and the direction contract in `.impeccable/surfaces/`. The navigation changed first, at
the owner's ask: a phone bottom bar (Today, Plan, Shop, Meals, More), More and Settings pages, the debug cards moved to
Settings > Diagnostics. Then every page was restyled and the skill's finish reviewer ran: eight material fixes (the
week tile showed $0.00 for an unpriced week, magnets missing on Plan, mismatched names on the desktop drawer, and
smaller ones), all scored resolved; disposition ship for those fixes. Building it found and fixed a sync gap - a pull
confirming this device's own edits reported no change, so pages never re-read what was unsent.

Open for the owner: look at it on the phone; whether any page still feels stock (the dialogs, the date pickers and the
receipt review were restyled but are the least-seen pages).

## Later, from the walk-through

No offline cold start (a PWA service worker, standalone deployment only); "New meal" in the plan editor's entry sheet;
"1 of 2 bought" on a ticked line a receipt part-covered. Version 2's integrations are in
[2026-10-version-2-integrations.md](2026-10-version-2-integrations.md).

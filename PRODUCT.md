# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

One real user: the household's main planner, not a developer. The owner built it and will rarely open it. The
household is two adults and a toddler; the planner plans for all three. The toddler mostly shares the main meals but
eats differently for snacks and some slots, so every feature must work for an entry that is only the toddler's, and for
a shared meal the toddler eats a share of.

The planner's day with it, in order of frequency:

1. **Today** - what's on for today, what's left over.
2. **The plan** - the calendar: view, swap, skip. Planning is done mostly a month at a time, so the month at a glance
   (what is planned, what has gaps, what has gone by) matters as much as the week.
3. **The shopping list, in the shop** - one-handed, often with poor reception, ticking lines off.
4. **Meals** - browsing and editing meals and their ingredients.

Setup and heavy editing (meals, plan templates, prices, receipt import) are hoped to happen on a desktop, but expect
them to happen on the phone too, eventually mostly. Phone-first; the desktop is a wider view of the same app, not a
separate one.

## Product Purpose

Replaces a Google Sheet meal plan: a rotating 4-week plan laid onto a calendar, batch cooking with serves eaten as
leftovers, a shopping list per store less what is already in the pantry, and what each meal and week costs, priced
from imported Woolworths and Costco receipts. Success: **all planning happens in the app** - the planner stops opening
the sheet, or any spreadsheet beside it, and the weekly shop comes from the app.

## Positioning

Built for this one household's actual routine - batch cooking and leftovers counted as serves, two stores with
different staples, a toddler on separate slots - rather than a generic recipe app. Works offline: the browser holds
its own copy and syncs when it can.

## Operating Context

- Phone in a supermarket aisle, one hand holding a basket or a toddler; reception drops.
- Phone at the bench while cooking; glancing at today.
- Weekly: a Woolworths online order (text PDF invoice) or shop in store (the Woolworths app's eReceipt, a text PDF of
  the till roll), and a Costco trip (image-only receipt exported from the Costco app), all imported afterwards to update
  prices and the pantry.
- Runs as a Home Assistant add-on on a Raspberry Pi, opened through Home Assistant (its ingress frame, and its
  phone app); also standalone.

## Capabilities and Constraints

- Blazor WebAssembly + MudBlazor; offline-first with browser SQLite and a sync chip that says whether edits have
  reached the server.
- Areas: Today, Calendar (and plan templates), Shopping, Pantry, Receipts, Meals, Ingredients, Prices, and the
  household set-up lists: People, Who can sign in, Slots of the day, Meal groups, Categories, Stores.
  Technical status (server version, this device) is for diagnosis only.
- Money in cents; a cost that cannot be computed shows as incomplete ("from $X + 1 unpriced"), never as zero.
- Plain words throughout: no developer vocabulary (ops, sync epochs, migrations) in anything the planner reads.
- Budget (Actual Budget) and live Woolworths prices are version 2; not designed for yet.

## Evidence on Hand

Real data arrives once the planner's spreadsheet is imported. The original spreadsheet and receipts are in
`private/` and must never be quoted into code, docs or designs; use synthetic examples.

## Product Principles

1. Today first: the app opens on what matters right now, not on a menu.
2. One hand, bad reception: anything used in a shop works with a thumb and never waits on the network.
3. Honest numbers: unknown costs and unsent changes are said plainly, never hidden or zeroed.
4. Set-up stays out of the way: lists configured once a season live behind Settings, not beside daily work.
5. Written for the planner, not the builder.

## Accessibility & Inclusion

No specific requirement given. Phone use outdoors and in shops: legible at arm's length, large touch targets.

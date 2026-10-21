---
title: Documentation map
kind: reference
status: current
verified: 2026-10-22
---

# Documentation

Start here. Every doc carries front-matter saying what it is (`kind`) and whether it is still true
(`status`), so you can tell reference from a plan without reading it.

Writing or moving a doc rather than reading one? [AGENTS.md](AGENTS.md) is the procedure — which
directory, what front-matter, and how a doc leaves `work/`.

Operating the repo — running the app, driving the browser, reading the database — is not here. That
is the global `run-and-inspect-app` skill plus this repo's `.claude/app-local.md`, and the repo-wide
rules are in [../AGENTS.md](../AGENTS.md).

## For how something works, read `modules/` only

Present tense, kept current, no dates.

- [How the app is hosted](modules/infrastructure/hosting.md) — the projects, one process serving
  gRPC-Web and the WASM app, the request pipeline and its `UseRouting` trap, and where the data lives.
- [How the app is deployed](modules/infrastructure/deployment.md) — the one image, the Home Assistant add-on,
  ingress (base path, the gRPC-Web prefix trap, who may connect, the HA user), CI to GHCR, and the owner's
  install steps; standalone on Kubernetes or Docker: refusing to start with no auth, a sign-in proxy's header believed
  only from the proxy, the chart, Litestream to S3, and what the owner must do to deploy it.
- [The browser database](modules/sync/browser-database.md) — EF Core SQLite in OPFS, the `wasm-tools`
  workload trap, starting it after the first render, sharing it across windows, one context at a time per
  window, local migrations, and what it costs.
- [Offline sync](modules/sync/offline-sync.md) — every edit is an op: the clock and merge rules, push, pull
  and the change stream, the protocol version check, and where offline does and does not work. **Read before
  any write path.
- [The catalogue](modules/catalogue/catalogue.md) — stores, categories and ingredients: seeded as server ops, valid
  names, hand-set order by position, an ingredient's unit, no foreign keys, and why deleting something in use is
  refused.**
- [Products and prices](modules/catalogue/products-and-prices.md) — what each store sells, pack sizes typed as the
  pack says them, price history, an ingredient's price per unit (usual store first, rounded once), and why aliases
  are not a field.

- [Spreadsheet import](modules/import/spreadsheet-import.md) - the planner's sheet into drafts: where it finds meals,
  ingredients and prices, units marked as guesses, what it asks, one transaction, and why a second run adds only
  what is new.
- [People, slots and plan templates](modules/planning/plan-templates.md) — the household, the slots of the day, and the
  rotation of weeks: an entry per thing eaten, its meal choices as rows, who eats it, the week grid and the day view.
- [The calendar](modules/planning/calendar.md) — a plan laid onto real dates (safe to repeat, and from two phones at
  once: derived ids, creates that carry only what never changes), choosing an alternative, overriding, skipping and
  replanning, and what a serve costs on each day.
- [Batches and serves](modules/planning/batches-and-serves.md) — a cooked entry's batch, leftovers eating it oldest first
  or by choice, what a skip does, and why serves are counted from the calendar instead of stored (no counter op).
- [The pantry](modules/pantry/pantry.md) — stock on hand: a ledger of bought / adjusted / counted movements, what the
  calendar's meals use read rather than stored, never below zero, a count setting the level, staples as Have / Running low
  / Out, and the stock at a date the shopping list subtracts.
- [The shopping list](modules/shopping/shopping-list.md) — the days' meals less the pantry in whole packs by store, why it
  reads the pantry without its own ticks, what Bought and Have it write to stock, derived ids for two phones, and the
  Spend and Eaten costs.
- [Receipts](modules/receipts/receipts.md) — a Woolworths invoice read in the browser by position (its traps: overprinted
  bold, ligatures, wrapped descriptions) and a Costco receipt by OCR in the browser (one bitmap across two pages; the amount
  column's misreads, measured; correcting a line beside its strip of the picture), refused unless it adds up, the store the
  reader's, matched by code then alias then a confirmed suggestion, derived ids so a second import is refused, and the rule
  that a receipt replaces the shopping-list tick for the same purchase.
- [Meals](modules/meals/meals.md) — meal groups, meals and their five kinds, a meal's ingredients, and its batch and
  per-serve cost (incomplete, never lower, when anything is unpriced).

## The other three directories

| Directory | What is in it | Trust it for |
|---|---|---|
| `work/` | plans and discussions for changes not yet finished | what someone intends to do |
| `open-questions/` | one unresolved question per file | knowing something is *not* settled |
| `archive/` | finished, rejected or superseded work | history and reasoning only — **never** current behaviour |

### In flight now

- [Version 1 scope](work/2026-10-v1-scope.md) — what the app is, every decision already made, the
  domain model, how costs are computed, and the build order. **Start here.**
- [Version 1 acceptance](work/2026-10-v1-acceptance.md) — the definition of done driven on a phone-sized screen
  (2026-10-22): what works, the seven gaps fixed, what is left, and **the owner's remaining checks, in order** (iPhone,
  the add-on on the Pi and a real backup and restore, wiping the test data, the first real import).
- [Version 2 integrations](work/2026-10-version-2-integrations.md) — Actual Budget and Woolworths live
  prices. Not to be built yet.

### Open

- [SQLite in the browser](open-questions/sqlite-in-the-browser.md) — proven in Chrome; waiting on the
  owner's iPhone check, whose steps it lists.
- [Blazor WASM behind Home Assistant ingress](open-questions/home-assistant-ingress.md) — proven against a
  simulated Supervisor; waiting on the owner to install the add-on on the Pi, and to take and restore a backup there,
  whose steps it lists.

## Lifecycle

A doc has one `status`, and one way out of it.

```
proposed ──accepted──> in-progress ──landed──> folded ──> archive/
    │                       │
    └──rejected─────────────┴──superseded────> archive/
```

- `modules/` docs are always `status: current`. They are rewritten in place, never dated, never
  superseded.
- `work/` docs are `proposed`, `accepted`, `in-progress` or `landed`. When the change lands, the
  durable facts move into `modules/` and the file moves to `archive/` with `folded_into:`.
- `archive/` docs are `folded`, `rejected` or `superseded`. Never edited — supersede instead.
- `open-questions/` docs are `open`. Closing one means deleting it and updating the module doc.

`verified:` is the last date someone checked a doc against the code. If the code in `code:` has
changed since, treat the doc as suspect and fix it while you are there.

## Naming

- `modules/<module>/<topic>.md` — no dates, no version words.
- `work/YYYY-MM-<slug>.md` and `archive/YYYY-MM-<slug>.md` — dated by when the work opened.
- `open-questions/<slug>.md` — no dates.

## Where this came from

The convention, and most of the architecture, is inherited from `GSBC.ImpactKids` via
`GSBC.Accounting`. Where those repos' `docs/modules/` describe something this repo reuses, read it
there rather than copying it here, and record only the difference.

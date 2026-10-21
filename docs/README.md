---
title: Documentation map
kind: reference
status: current
verified: 2026-10-21
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
  install steps.
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

## The other three directories

| Directory | What is in it | Trust it for |
|---|---|---|
| `work/` | plans and discussions for changes not yet finished | what someone intends to do |
| `open-questions/` | one unresolved question per file | knowing something is *not* settled |
| `archive/` | finished, rejected or superseded work | history and reasoning only — **never** current behaviour |

### In flight now

- [Version 1 scope](work/2026-10-v1-scope.md) — what the app is, every decision already made, the
  domain model, how costs are computed, and the build order. **Start here.**
- [Receipt formats](work/2026-10-receipt-formats.md) — what a Woolworths invoice and a Costco receipt
  contain, their traps, and how lines match products.
- [Deployment design](work/2026-10-deployment.md) — why one image and no YARP; the add-on and CI parts
  have landed in [deployment](modules/infrastructure/deployment.md). Left: standalone k8s, slice 14.
- [Version 2 integrations](work/2026-10-version-2-integrations.md) — Actual Budget and Woolworths live
  prices. Not to be built yet.

### Open

- [SQLite in the browser](open-questions/sqlite-in-the-browser.md) — proven in Chrome; waiting on the
  owner's iPhone check, whose steps it lists.
- [Blazor WASM behind Home Assistant ingress](open-questions/home-assistant-ingress.md) — proven against a
  simulated Supervisor; waiting on the owner to install the add-on on the Pi, whose steps it lists.
- [Reading Costco receipts with OCR in the browser](open-questions/costco-ocr.md) — is tesseract.js
  accurate enough? Slice 13.

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

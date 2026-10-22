# AshersLab.MealPlanner

A household meal planner. Meals and their ingredients, a rotating meal plan laid onto a calendar,
batch serves tracked as they are eaten, a shopping list split by store, and what it all costs —
priced from imported Woolworths and Costco receipts, later from live Woolworths prices, and compared
against the month's grocery budget in Actual Budget.

Same shape as `GSBC.ImpactKids` and `GSBC.Accounting` (Aspire + code-first gRPC + Blazor WASM +
MudBlazor), with three deliberate departures, all recorded in the scope doc:

- **Offline-first.** The browser holds its own SQLite and an op log; the server is the master and
  reconciles. Not the `RefreshableStore` fetch-on-demand pattern.
- **SQLite on the server, not Postgres.** It runs on a Raspberry Pi as a Home Assistant add-on.
- **One container in production, not four.** A Home Assistant add-on is one image. The gRPC host
  serves the WASM app itself; there is no YARP and no migrations Job in production.

**Read [docs/work/2026-10-v1-scope.md](docs/work/2026-10-v1-scope.md) before writing any code.** It is
the agreed design and the slice order. The other `docs/work/` docs are its detail; `docs/open-questions/`
lists what must be proven before the design leans on it.

# The repo is public — personal data lives in `private/`

`private/` is git-ignored and holds the real receipts, the original meal-plan spreadsheet and
`LOCAL-NOTES.md` (server URLs, household context). Read them; never commit them, never quote a name,
member number, order number or URL from them into code, docs, tests or commit messages.

- Parser tests use **synthetic** fixtures written by hand to match the real layouts — never a real
  receipt, redacted or not. `*.pdf`, `*.xlsx` are ignored repo-wide; add a synthetic fixture with
  `git add -f` deliberately.
- Before every push: `git status --ignored` and `git diff --cached --stat`. If anything under
  `private/` or any `.pdf`/`.xlsx` is staged, stop.
- Secrets (Actual password, API keys) come from configuration — add-on options, k8s Secrets,
  user-secrets locally. Never a committed `appsettings*.json`.

# Documentation

`docs/` carries the reasoning behind decisions that are not obvious from the code.
**[docs/README.md](docs/README.md) is the map** — read it before hunting through the tree.

- `docs/modules/<module>/` — how things work now. The **only** place to read from when answering
  "how does X work". Always `status: current`.
- `docs/work/YYYY-MM-<slug>.md` — plans, handovers and design discussions. Write new ones here,
  never in `modules/`.
- `docs/open-questions/` — one unresolved question per file.
- `docs/archive/` — finished, rejected or superseded work. Read for *why*; never cite it as current
  behaviour, and never edit it.

Every doc has front-matter with `kind`, `status`, `code:` and `verified:`. **Writing, moving or
folding a doc — or changing code a doc describes — follow [docs/AGENTS.md](docs/AGENTS.md).**

`./update-sln-docs.sh` regenerates the docs solution folders in `AshersLab.MealPlanner.sln`. Run it
after any add, move or delete under `docs/`.

## Where to read about the inherited architecture

Do not copy the GSBC repos' docs into this one. Read them there (siblings under
`/Users/asherp/Personal-Projects/Git/`) and record only the difference:

- `GSBC.Accounting/docs/archive/2026-08-impactkids-conventions.md` — the recipe for one vertical slice
  and the "do not copy this" list. The closest thing to a template for this repo's project layout.
- `GSBC.Accounting/docs/modules/infrastructure/deployment.md` — the CI pipeline shape (semantic
  version → multi-arch images). This repo pushes to **GHCR, not Docker Hub**, and has no Argo step.
- `GSBC.ImpactKids/docs/frontend-store-architecture.md` — the online store pattern. This repo's
  local-first store replaces it; read it to know what *not* to reach for.

# Local tooling

**Never `dotnet run`.** Run configurations only — `mcp__rider__execute_run_configuration`, per the
global `run-and-inspect-app` skill plus `.claude/app-local.md`, which resolves that skill's
placeholders for this repo — and says where this stack is smaller than the one the skill assumes (no
YARP, no containers, SQLite). The run configurations are shared files in `.run/`; the tests run through
the `AshersLab.MealPlanner.Tests` configuration.

Build through Rider rather than the CLI:

- build — `mcp__rider__build_solution`, which also works while the app is running
- per-file analysis — `mcp__rider__get_file_problems`

`dotnet ef` is fine to run directly — there is no MCP equivalent.

**Edit files with the Edit/Write tools, never with `sed` or a `python` heredoc.** A shell replace that
matches nothing fails silently and hides the diff.

**The GSBC stacks' ports are taken.** They run at the same time as this one. Every fixed host port
here must differ from both — see `GSBC.Accounting/.claude/app-local.md` for theirs, and record this
repo's in its own `app-local.md`.

# Implementation Rule — Vertical Slices

Work in vertical slices, not in layers. A slice is one operation carried end-to-end: contract → server
model → local (browser) model → op application → UI.

One operation per slice. Do not batch a layer across operations. Do not stop for approval at layer
boundaries.

**Exception — the foundation.** The scope doc's slice 0 (browser SQLite persistence, the op log, push
and pull) is infrastructure every later slice stands on. Build and prove it first, as its own slices,
before any feature UI.

## One slice, one reviewable change

Each slice is finished when it:

- builds — `mcp__rider__build_solution`
- registers what it added — server service mapping and client registration
- **has been seen working in the running app.** Start it through Rider, drive the page, and read the
  rows it actually wrote — in the browser's SQLite *and* the server's. For anything that syncs, check
  it from a second browser profile too.
- has tests for anything with arithmetic or parsing — costing, unit conversion, pack rounding, serves,
  receipt parsers, op merge rules. These are pure functions; there is no excuse for them to be
  untested.
- updates any `docs/modules/` doc whose documented behaviour it changed, per
  [docs/AGENTS.md](docs/AGENTS.md)

The only thing allowed through the gate unfinished is something a later slice in the same feature
will complete. Say so.

## Money

- **Store money as integer cents, quantities as decimal in the ingredient's base unit (g, ml, each).**
  Never `double` for either.
- **A cost that cannot be computed is shown as incomplete, never as zero.** An ingredient with no price
  makes every meal using it "from $X + 1 unpriced"; it does not quietly cost nothing.

## This app is in production

**The household is entering real data** (since 2026-10-23). Both the server SQLite and every browser's
local SQLite hold rows worth keeping. Never wipe either, never squash or rewrite an applied migration.

- A schema change is an **additive EF migration on both sides** — `Grpc` and `LocalData` — that keeps
  existing rows meaningful. A new column is nullable or has a default that means "as before".
- A new op field, or a changed meaning of one, **bumps `SyncProtocol.Current`** — an older browser throws
  on a field it does not know. Raise `OldestSupported` to match when an older browser would misread what
  the server now sends; it reloads into the new build with its outbox intact
  ([offline sync](docs/modules/sync/offline-sync.md#protocol-version)).
- A rename or removal of an op field must still apply the ops already in the log, which are replayed on
  restore and on a fresh browser.

## When to stop and ask

Only for what cannot be undone by editing code:

- anything touching data outside this repo — the Actual Budget server, the Home Assistant instance,
  any cluster
- anything with an out-of-repo prerequisite — GHCR package visibility, repository secrets, Home
  Assistant configuration
- an ambiguity where two readings lead to materially different designs

Otherwise report at slice boundaries and keep going.

# Git

`master` is the trunk, and while this app is pre-production it is also the working branch.

**Commit on `master` and push it.** No branch, no PR, no asking first. Commit per slice, message
written for somebody reading `git log` a year from now — what changed and why.

Still ask first for:

- **`--force`, and any history rewrite on a pushed branch.**
- **branch and tag creation.** CI creates version tags itself; you do not.

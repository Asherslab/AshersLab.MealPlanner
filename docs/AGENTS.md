# Rules for writing in `docs/`

Applies to everything under `docs/`. [README.md](README.md) is the map for readers; this file is the
procedure for changing it. Follow it exactly — the point is that nobody has to tidy up after you.

# Which directory

Pick by **lifetime**, not by topic.

| You are writing | Goes in | `kind` |
|---|---|---|
| How something works now | `modules/<module>/<topic>.md` | `reference` |
| A plan, RFC, design discussion, or handover for work not finished | `work/YYYY-MM-<slug>.md` | `plan` / `handover` / `discussion` |
| An unresolved question, with the evidence gathered so far | `open-questions/<slug>.md` | `investigation` |
| Nothing — it is finished, rejected or superseded | `archive/` (only ever by moving a `work/` doc there) | unchanged |

`modules/<module>/` gets a `README.md` when it has two or more docs. One doc, no README.

## What belongs here versus in a skill

This repo has both, and they answer different questions.

- `docs/` — **why the code looks like that.** Durable behaviour, constraints, traps in the domain.
- `.claude/skills/` — **how to operate the repo.** Running the app, driving the browser, reading the
  database, the tooling gotchas that cost an hour each.

A rule about the app's behaviour goes in `docs/modules/`. A rule about your own workflow goes in a
skill or in an `AGENTS.md`. Do not write the same thing in both — link instead.

## Reading

- Answering "how does X work" → read `modules/` and `open-questions/` only.
- **Never cite `work/` or `archive/` as current behaviour.** `work/` is intent; `archive/` is history.
  Both are routinely wrong about the present, on purpose.
- `archive/` is read-only. Never edit a doc there. If it needs correcting, it needs superseding.

# Front-matter

Every doc. No exceptions.

```yaml
---
title: How a meal is costed              # sentence case, matches the H1
kind: reference                          # reference | plan | handover | discussion | investigation
status: current                          # see the lifecycle below
module: costing                          # omit only for docs/README.md
opened: 2026-10-20                       # work/ and open-questions/ only
verified: 2026-10-20                     # last date someone checked this against the code
code:                                    # repo-relative paths this doc describes
  - AshersLab.MealPlanner.WASM/Features/Costing
---
```

Rules:

- `code:` paths must exist. They are how a reader (and a future audit) finds what a doc is about.
  Directories are fine; prefer the narrowest thing that is still stable.
- `verified:` is a claim someone checked. Bump it only when you actually compared the doc to the code.
  Never bump it as a formality.
- Absolute dates, `YYYY-MM-DD`. Never "last week", "recently", "next sprint".
- Terminal statuses need their exit field — `folded_into:`, `superseded_by:` or `reason:`. See below.
- Don't repeat `status` in the prose. Two halves of the same fact drift.

# Lifecycle

```
proposed ──accepted──> in-progress ──landed──> folded ──> archive/
    │                       │
    └──rejected─────────────┴──superseded────> archive/
```

| `status` | Means | Lives in |
|---|---|---|
| `proposed` | written up, not agreed | `work/` |
| `accepted` | agreed, not started | `work/` |
| `in-progress` | code being written | `work/` |
| `landed` | code merged, doc not yet folded | `work/` — transient, fold it now |
| `folded` | durable facts moved into `modules/` | `archive/` |
| `rejected` | decided against | `archive/` |
| `superseded` | replaced by another doc | `archive/` |
| `current` | true now | `modules/` |
| `open` | unresolved | `open-questions/` |

`modules/` docs are only ever `current`. They are rewritten in place — never dated, never superseded,
never given a "v2".

## Folding — do this when the change merges

1. Move the durable facts into the relevant `modules/` doc. Rewrite them into present tense: "the
   service validates the token", not "we changed it to validate the token". Drop what only mattered
   while the work was in flight — task lists, ordering, "not built yet", blockers now cleared.
2. Keep in the `work/` doc only what is history: why alternatives were rejected, what a migration
   exposed, verification results.
3. `git mv docs/work/<file> docs/archive/<file>` — keep the filename and its date.
4. Set `status: folded`, add `closed: <today>` and `folded_into:` listing every `modules/` doc you
   touched.
5. Bump `verified:` on the `modules/` docs you edited.
6. Update the `modules/` doc's own links, and the "In flight now" list in [README.md](README.md).

If nothing in a `work/` doc is worth keeping, folding is still the move — an empty `folded_into:` is a
signal it was pure procedure. Delete only genuinely worthless files.

## Rejecting or superseding

- Rejected: `status: rejected`, `closed:`, `reason:` — one sentence on why. Then `git mv` to `archive/`.
- Superseded: `status: superseded`, `superseded_by:` pointing at the new doc, and the new doc gets a
  line saying what it replaces. Then `git mv` to `archive/`.

## Closing an open question

Answer goes into the `modules/` doc, then delete the `open-questions/` file. If the answer is "we tried
and it does not work", that is durable — write it down in the module doc as a constraint before
deleting.

# When you change code

Not optional, not follow-up work:

- Changed behaviour a `modules/` doc describes → **update that doc in the same commit.** A doc that
  disagrees with the code is worse than no doc.
- Touched a path in some doc's `code:` list → either bump its `verified:` (you checked, still accurate)
  or fix the doc.
- Renamed or moved a path → update every `code:` list referencing it.
- Merged work a `work/` doc covers → fold it, per the procedure above.

# When you create a doc

1. Correct directory and filename per the table above.
2. Full front-matter.
3. H1 matching `title`, then one short paragraph saying what the doc is for and who should read it.
4. Link it: from its module `README.md`, or from the "In flight now" / "Open" list in
   [README.md](README.md).
5. **Run `./update-sln-docs.sh`** from the repo root. It regenerates the docs solution folders in
   `AshersLab.MealPlanner.sln` from the filesystem, so the file shows up in Rider without "Show all files".
   Run it after any add, delete, rename or move under `docs/` — it is idempotent, and it verifies the
   result still parses before writing. Do not hand-edit those solution folders; the script owns them
   (GUIDs are tracked in `docs/.sln-guids`, which is committed and must not be edited either).
   `./update-sln-docs.sh --check` reports out-of-date without changing anything.

# Style

Written for someone who has the code open and does not know why it looks like that.

- Say why, not what. The code says what. If a paragraph is restating a method, delete it.
- Cite specifics: `Path/To/File.cs:123`, exact config keys, exact error text. No "the relevant service".
- Lead with the trap. If missing something breaks production silently, that goes first and in bold —
  not in a "notes" section at the bottom.
- Present tense in `modules/`. No changelog voice, no "recently", no "now" meaning "since my change".
- Tables for anything with more than three parallel cases. Prose for reasoning.
- Wrap at 120 columns.
- Absolute dates always.
- One topic per file. If a doc needs two H1-level subjects, it is two docs.
- This is a household app used mostly on a phone by someone who is not a developer: when a doc
  describes something the planner sees, say what they see and how many taps it costs.
- **This repo is public.** Never put a household member's name, an address, a membership or order
  number, a payment detail or a household server URL in a doc. Those live in `private/` (git-ignored).
  Product lines as a store prints them ("Woolworths black beans 420g $1.00") are fine as examples.

Do not:

- Write a doc restating an AGENTS.md rule, or a doc that only says a change was made — the commit does.
- Add "Last updated" lines, tables of contents, or status badges in prose. Front-matter carries that.
- Leave a doc in `work/` at `status: landed`.
- Create a `modules/` doc for a module with no durable behaviour yet. Put it in `work/` until it exists.

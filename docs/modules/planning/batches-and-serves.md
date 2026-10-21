---
title: Batches and serves
kind: reference
status: current
module: planning
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.Shared.Domain/Planning/Serves.cs
  - AshersLab.MealPlanner.Shared.Domain/Planning/CalendarEntryRules.cs
  - AshersLab.MealPlanner.Shared.Data/Planning/ServesBook.cs
  - AshersLab.MealPlanner.Shared.Data/Planning/CalendarEntry.cs
  - AshersLab.MealPlanner.WASM/Features/Planning/CalendarPage.razor
  - AshersLab.MealPlanner.WASM/Features/Planning/CalendarEntrySheet.razor
  - AshersLab.MealPlanner.WASM/Features/Planning/ServesText.cs
  - AshersLab.MealPlanner.Tests/Domain/ServesTests.cs
  - AshersLab.MealPlanner.Tests/Planning/ServesSyncTests.cs
  - .claude/tools/chrome-serves-check.mjs
---

# Batches and serves

What a cooked meal makes and who eats it: a batch on the calendar, its serves counted down by the meal itself and the
leftovers after it, and what a skip does to them. Read before touching anything that counts serves, or before the pantry
(which draws stock down by cooked entries) and the "eaten" cost view lean on them. The calendar itself is
[the calendar](calendar.md); a meal's kind and serves per batch are [meals](../meals/meals.md).

## Traps first

- **Serves are counted, never stored** (`Shared.Domain/Planning/Serves.cs`). A batch is a calendar entry whose meal is
  `Cooked`; what is left of it is what it made less a serve per eater of everything that ate from it. There is no "serves
  eaten" field and no counter op: two phones each adding a leftovers lunch offline make two calendar entries, and both
  are counted once synced, in either order (`ServesTests.Two_leftovers_entries_added_on_two_phones_are_two_draws_in_any_order`,
  `ServesSyncTests.Leftovers_added_offline_on_two_phones_both_eat_*`). The `Increment` op kind slice 2 set aside for this
  was never needed; see [offline sync](../sync/offline-sync.md) for the rule that replaced it.
- **The planner's own numbers are plain fields, the later edit winning**: a cooked entry's `ServesMade` ("we made 8") and
  a leftovers entry's `EatsFrom` (the batch it eats). Each is a statement of fact, not a change to apply, so last-writer-wins
  is right for them.
- **Everything is read through `CalendarBook`** (`ServesBook.LoadAsync`). An entry the calendar does not show - removed,
  or an untouched one its plan no longer puts there - cooks and eats nothing, exactly like a skipped one.
- **A week's numbers must not depend on which week is on screen**, so `ServesBook` reads the calendar **from its first
  entry** (`ServesBook.FirstDayAsync`) to two weeks after the days asked for. No shorter window is right: each "whatever is
  left" pick depends on what every earlier pick left, and the picks chain back without limit - a three-week window once gave
  a lunch 1 serve where the whole calendar gave 2 (`ServesSyncTests.A_chain_of_whatever_is_left_picks_*`). The two weeks
  after are a batch's last possible eater: a lunch can be set to eat a batch up to 14 days old.
- **Saving the entry sheet clears serves made only when the box was edited** (`CalendarEntryRules.ServesMadeToSave`). An
  untouched box equal to the meal's number would otherwise send `ServesMade = null` with a newer clock and erase a number
  another phone had set.

## The rules

| Case | What happens |
|---|---|
| A cooked entry | Makes `ServesMade`, or the meal's `ServesPerBatch` when that is null. Its own eaters eat first; more eaters than serves is "short" |
| Eaters | The people chosen, or everyone: the household's live people (at least 1). A serve each - no portion sizes |
| A leftovers entry eating "whatever is left" (`EatsFrom` null) | The oldest batch cooked in the 7 days before it that has enough, else the oldest with any. Oldest first: what would spoil first is eaten first |
| A leftovers entry with a chosen batch | Eats that batch, even when an older one has serves |
| Order | The day, then the slot's position, then the entry id: lunch never eats that night's dinner |
| A skipped cooked entry | Made nothing. Leftovers set to eat it are flagged **not cooked**; leftovers eating whatever is left look for another batch, and are flagged not cooked when the skipped one was all there was. Never left pointing at serves that do not exist |
| A skipped leftovers entry | Eats nothing: its serves stay for whoever is next |
| A chosen batch that is gone (removed, no longer a cooked meal) | Flagged **gone**, eats nothing |
| A chosen batch cooked after the entry (moved since) | Flagged **cooked later**, eats nothing |
| Not enough | Takes what is there: "only 1 of 2 serves" |
| Moved or replanned entries | Counted where they are now (`PlannedEntry.Day`), so a move changes who eats first |

`Serves.Count` is a pure function of a list of `ServesEntry`s and returns every batch's draws in order (with what is left
after each) and every leftovers entry's batch, need, got and problem. `ServesBook` turns the calendar into that list and
answers the page: `BatchOf`, `LeftoversOf`, and `ChoicesFor` (the batches a leftovers entry can be set to eat, with what
each has left before it).

**Who eats the cooked meal itself is a decision, not a given**: the scope says "leftovers slots consume" serves; this
counts the cooking night's eaters too, because a 4-serve stew for 3 people at dinner leaves 1, and the meal's per-serve
cost already divides the batch by its serves. If the planner's "4x" turns out to mean "4 meals for the household", the
change is one line in `Serves.Count` (the cooked case's own draw).

## What the planner sees

On the calendar (desktop grid and phone day alike), under an entry's cost:

- **A cooked entry**: "makes 6 · 2 left after Thu", "makes 6 · eaten by Wed", "makes 6 · 2 left over" (nothing planned
  eats it), "makes 2 · 1 short" (red).
- **A leftovers entry**: "Stew from Mon · 2 left after"; in red when it has less than it needs - "nothing to eat",
  "only 1 of 2 serves of Stew", "Stew from Mon wasn't cooked - nothing to eat", "its batch is gone - nothing to eat",
  "Stew isn't cooked until Thu - nothing to eat". Its cost is a serve of the batch it eats.
- **Serves**, below the week (above "To replan"): one line per leftovers entry with less than it needs - "Saturday's
  leftovers has nothing to eat", "Tuesday's leftovers has nothing to eat: Stew on Monday was skipped" - then one per batch
  cooked this week with serves nobody is planned to eat - "2 serves of Stew from Monday left after Thursday will go
  uneaten".

In the entry sheet, a cooked meal shows **Serves made** (blank is the meal's own number; typing that number back clears
it), and a leftovers meal shows **Eats from**: "Whatever is left, oldest first", then the batches cooked in the two weeks
before it, most recent first - "Stew · Mon 26 Oct · 4 left". Choosing a batch is two taps after opening the entry.
Fields a meal does not show are kept: picking another meal for a moment loses nothing.

## As seen

2026-10-22, `.claude/tools/chrome-serves-check.mjs`, two headless Chromes on fresh profiles - A desktop-sized, B
phone-sized - in a week nobody had used, the household 5 people, every check passing: A cooks a stew for 15 on Monday with
leftovers on Tuesday, Wednesday and Saturday - "makes 15 · eaten by Wed", "5 left after", "0 left after", Saturday "nothing
to eat" in red and in the week's notes; B (day view) shows the same. B skips the stew: A's three lunches all say it wasn't
cooked, and the notes name it; B un-skips it. A skips Tuesday's lunch and Saturday eats its serves. A sets 20 made: "5 left
after Sat" and "5 serves of Stew from Monday left after Saturday will go uneaten", on B too. A cooks soup at Monday lunch and
Wednesday's lunch eats it first (oldest); B's sheet offers whatever is left and both batches, and B sets the stew -
A shows it and the server holds `EatsFrom`. Both offline each add Thursday's lunch: the server holds both, and A and B count
them the same. The seven entries' rows - `ServesMade`, `EatsFrom`, the skip - are identical in the server's SQLite and both
profiles', outboxes empty. Screenshots of the desktop week and the phone day.

The in-process tests: `Tests/Domain/ServesTests.cs` (the cooking night first then leftovers in order, a serve per person,
nothing left and not enough, a skipped batch flagging chosen and automatic leftovers, falling back to another batch, a skipped
lunch giving its serves back, slot order within a day and the id within a slot, moves and a replanned cooking, oldest-first
and the week limit at exactly 7 days, a chosen batch, gone and cooked-later batches, short at the table, two phones' lunches
in either order) and `Tests/Planning/ServesSyncTests.cs` (through the real host: the same counts on another browser - and the
same lunch eating on both - a skip of the cooking flagging both lunches and a replan onto another day, serves made, two
offline lunches and a skipped one, a chosen batch and a removed one, the picker's "N left" with an earlier eater the same
day, and the same numbers whichever week is loaded). Breaking the skipped-leftovers rule, oldest-first, or the not-cooked
flag fails both files' tests; so does, since the 2026-10-22 review fixes, dropping the id tie-break, the 7-day `>=`, the
same-day slot order of the picker, the edited-only clear of serves made, or going back to a three-week window.

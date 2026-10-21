---
title: The calendar
kind: reference
status: current
module: planning
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.Shared.Domain/Planning/Rotation.cs
  - AshersLab.MealPlanner.Shared.Domain/Planning/CalendarEntryRules.cs
  - AshersLab.MealPlanner.Shared.Domain/Planning/PlanTemplateRules.cs
  - AshersLab.MealPlanner.Shared.Data/Planning/CalendarEntry.cs
  - AshersLab.MealPlanner.Shared.Data/Planning/CalendarEntryEntity.cs
  - AshersLab.MealPlanner.Shared.Data/Planning/CalendarBook.cs
  - AshersLab.MealPlanner.LocalData/Planning/CalendarApply.cs
  - AshersLab.MealPlanner.WASM/Features/Planning/CalendarPage.razor
  - AshersLab.MealPlanner.WASM/Features/Planning/CalendarEntrySheet.razor
  - AshersLab.MealPlanner.WASM/Features/Planning/ApplyPlanDialog.razor
  - AshersLab.MealPlanner.WASM/Features/Planning/CalendarText.cs
  - AshersLab.MealPlanner.Tests/Domain/RotationTests.cs
  - AshersLab.MealPlanner.Tests/Domain/CalendarRulesTests.cs
  - AshersLab.MealPlanner.Tests/Planning/CalendarSyncTests.cs
  - .claude/tools/chrome-calendar-check.mjs
---

# The calendar

What is planned on each real date: a plan's rotation laid onto the calendar, then changed by hand - another of an
entry's choices, any other meal, who eats it, skipped, moved, removed, or added. Read before touching a calendar entry,
applying a plan, or anything that reads "what is eaten when" (batches and serves, the pantry, the shopping list). The
plans themselves are [plan templates](plan-templates.md); every edit is an op ([offline sync](../sync/offline-sync.md)).

## Traps first

- **A calendar entry's create carries only what never changes**: its template entry, and the date and slot it was
  planned for (`CalendarEntryRules.CreateFields`). Applying a plan on a device that has not seen the latest edits sends
  creates clocked *after* them; if a create carried the meal, the status or where it is, it would undo a skip, a move or
  a chosen meal made elsewhere. The status, the meal, the people and a move are fields of their own, written only by the
  entry sheet. Tested: `CalendarSyncTests.Applying_again_on_a_browser_that_never_saw_the_changes_undoes_none_of_them`.
- **An entry a plan makes has an id derived from its template entry and date** (`Rotation.EntryId`, a version-8 GUID
  from SHA-256), not a GUIDv7. Two phones applying the same plan offline create the same rows; the merge makes the second
  create a no-op, so nothing is ever duplicated. Applying again creates only ids the calendar has never held -
  **tombstones included**, so an entry removed from the calendar stays removed. Because those ids are not time-ordered, a
  day's entries are ordered by their template entry's id (`CalendarBook.On`).
- **Dates, never datetimes.** A week is seven `DateOnly` days from a Monday (`Rotation`), so a daylight-saving change
  cannot shift an entry to the wrong day. Every date on the wire is `yyyy-MM-dd`, stored as that text in both databases
  (the browser's SQLite reader cannot read `DateOnly`; see the price's `ObservedOn`), which is also why range queries
  compare text.
- **An untouched entry from a plan is shown only while the plan still puts it there** (`CalendarBook.Resolve`): its
  template entry live, the template's start and week count mapping its date to that entry's week and day. Removing an
  entry from the plan, shortening the plan or moving its start takes untouched entries off the calendar on every device,
  with no op of its own and nothing to conflict. Anything the planner changed stays. **Everything that reads the calendar
  must go through `CalendarBook`**, or it will count rows the planner cannot see.
- **A changed entry writes out its meal and people.** As planned, both are null and follow the plan (make soup the usual
  choice in the plan and next week's untouched entries say soup). The moment anything else changes - a skip, a move,
  another meal, other people - both are written in full (`CalendarEntryDraft.ChangesFrom`), so a skipped entry waiting to
  be replanned still knows it was the toddler's noodles after that plan entry is deleted. Saving it as planned again sends
  the nulls back.
- **A past entry still `Planned` counts as eaten** (`PlannedEntry.EatenBy`). Nobody ticks meals off; skipping is the one
  thing recorded. Batches, serves and the pantry (slices 10 and 11) read it that way.

## The row

`CalendarEntry` (`Shared.Data/Planning/CalendarEntry.cs`), rules in `Shared.Domain/Planning/CalendarEntryRules.cs`:

| Field | Meaning |
|---|---|
| `TemplateEntryId` | the plan entry it came from; null for one added by hand. Never changed |
| `Date`, `SlotId` | where it was planned. Never changed |
| `MealId` | null: the template entry's usual (first) choice, as it is now. Otherwise another choice or any meal |
| `People` | null: the template entry's people (everyone, for one added by hand); `everyone`; or a `PlanPeople` list |
| `Status` | `Planned` or `Skipped`, by name; a new entry is `Planned` |
| `MovedDate`, `MovedSlotId` | where it is now, when moved; null where it was planned. Always written together |

`PlanTemplate.StartsOn` is the Monday the plan's week 1 began (null until it is first applied). It is one decision for the
household, so the later edit wins; the calendar shows "Spreadsheet plan: week 3" from it. Nothing is a foreign key: an
entry whose meal was deleted shows "meal removed"; one in a deleted slot is not shown on the grid.

`CalendarBook` is how pages read it: the entries on some days (moved ones on the day they moved to), each resolved into
its meal, people and choices, and `ToReplan` - every skipped entry, wherever it is.

## Applying a plan

`Rotation` (pure, `Shared.Domain`): `WeekOf(startsOn, weekCount, date)` counts whole weeks from the start, floor division,
wrapping both ways - the week before week 1 is the last week. `StartFor(monday, week)` turns "this week is week 3" into
the start. `Generate` puts each template entry on every date in a range whose week and day match (entries beyond the week
count nowhere; at most 84 days at once), and `Apply` keeps only the ids the calendar lacks. `CalendarApply.PlanAsync`
(`LocalData`) builds the edits: the template's `StartsOn` when it changed, and the creates - written as one
`WriteAllAsync`. The page and the tests both call it.

Moving a plan's start and applying again is safe: the old untouched entries stop being shown (their dates no longer map
to their week), the new ones are created, and nothing a planner changed is touched. Two plans applied to the same week
are both shown - a separate toddler plan beside the household's is a real use.

## What a skip leaves for slice 10

A skipped entry eats nothing, cooks nothing and costs nothing; this slice records it and nothing else. When batches
arrive, **skipping a `Cooked` entry means its batch was never cooked**, and every leftovers entry drawing from it must be
flagged as having nothing to eat; skipping a `Leftovers` entry returns its serve. The hook is the `Status` field and
`PlannedEntry.Skipped`/`EatenBy`: batches must read entries through `CalendarBook`, and treat a removed (deleted) entry
exactly like a skipped one. Nothing here computes serves yet.

## What the planner sees

**Calendar** (`/calendar`, first in the menu): "Week of Mon 26 Oct" with arrows either side, "This week" when elsewhere,
and **Fill from a plan**. Under the title, which week of each plan this is ("Spreadsheet plan: week 3"). The week is in the
address (`?week=2026-10-26`), so a reload stays on it.

- **On a desktop** (960 px and wider) the week is the plan's grid with dates across: each entry is its meal, a dot per
  person when it is not for everyone, and what a serve costs - "$15.00 a serve", "from $0.00 a serve + 1 unpriced" (never
  a low number for an incomplete cost), "no ingredients yet", "leftovers" (costed by its batch, slice 10), "no cost".
  Today's date is coloured, past days greyed.
- **On a phone** a day at a time: seven day buttons (Mon/26 ...), "Monday 26 October", then the slots down the screen.
- **A skipped entry** stays on its day, struck through, saying "skipped", and is listed under **To replan** below the
  week ("Noodles · Tue 27 Oct · Lunch").

**Fill from a plan**: the plan, "The week of Mon 26 Oct is Week [n]" (from the plan's start when it has one), and "this
week" or 2-12 weeks; Fill. It says "Rotation: added 34 entries; 2 already on the calendar, left as they are." **Planning
next week on a phone: the arrow, Fill from a plan, Fill - three taps** once the plan has a start.

**The entry sheet** (tap an entry; full screen on a phone): the date and slot, "From Spreadsheet plan · week 2"; the plan
entry's choices as chips when it has more than one (one tap chooses), a search for any other meal, "Who eats it", and
"Move to" a date and slot. **Skip** answers at once - "we didn't have it" is two taps. A skipped entry's sheet says so and
offers **Not skipped**; picking another day and Save replans it (moved and no longer skipped, one op). **Remove** deletes it
for good: applying the plan again does not bring it back. The + in a cell adds an entry by hand: search, tap, Save.

**Deleting something the calendar uses** follows the catalogue's rule: a meal, person or slot named by a coming entry (today
or later, not skipped) is refused, "... is planned in Rotation, the calendar on Tue 27 Oct". Past entries hold nothing back -
they show "meal removed" instead, so a meal eaten once is not undeletable forever.

## As seen

2026-10-22, `.claude/tools/chrome-calendar-check.mjs`, two headless Chromes on fresh profiles - A desktop-sized (1280 px),
B phone-sized (390 px) - against the running app holding the imported plan and earlier runs' data, every check passing: A
makes four meals (one with an unpriced ingredient, a $45 takeaway for 3) and a two-week plan, opens next week's calendar
(the grid) and fills two weeks with next week as week 1 - "added 3 entries"; Monday dinner is the usual choice; costs read
"no ingredients yet", "from $0.00 a serve + 1 unpriced" and "$15.00 a serve"; the server's plan has `StartsOn` that Monday;
the following week says "week 2". B (the day view) chooses the other choice and A shows it. A skips Tuesday's lunch - struck
through, listed to replan on both; B replans it onto Wednesday's dinner from the list and A shows it there. A adds a takeaway
by hand and removes another entry. Both fill the same two weeks again: "added 0 entries; 3 already on the calendar", and the
server's rows are unchanged. Both go offline and each fills the week after next ("added 2 entries" each); back online the
server holds each of that week's entries once. The six rows (one a tombstone) are identical in the server's SQLite and both
profiles', outboxes empty. Screenshots of the desktop week, the phone day and the phone entry sheet.

The in-process tests: `Tests/Domain/RotationTests.cs` (Mondays across years and a leap day, week numbers wrapping forwards
and backwards, a rotation through two daylight-saving changes, the start from "this week is week n", generation from
mid-week and before the start, the day limit, ids, applying again) and `Tests/Domain/CalendarRulesTests.cs` (every field's
one spelling, the create's fields, who eats it, and every case of what saving the sheet sends) and
`Tests/Planning/CalendarSyncTests.cs` (through the real host: two browsers applying offline make each entry once; a stale
browser's apply undoes no skip, move, meal or removal; untouched entries follow the plan's choices and drop out when the
plan loses an entry; moving the start doubles no day; a hand-added entry skipped and replanned). Breaking the "untouched
entries follow the plan" rule in `CalendarBook.Resolve` fails two of the sync tests.

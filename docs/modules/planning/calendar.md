---
title: The calendar
kind: reference
status: current
module: planning
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.Shared.Domain/Planning/Rotation.cs
  - AshersLab.MealPlanner.Shared.Domain/Planning/RotationPhases.cs
  - AshersLab.MealPlanner.Tests/Domain/RotationPhasesTests.cs
  - AshersLab.MealPlanner.Shared.Domain/Planning/MonthGrid.cs
  - AshersLab.MealPlanner.Tests/Domain/MonthGridTests.cs
  - .claude/tools/chrome-month-check.mjs
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
  template entry live, and the plan's rotation **on that date** mapping it to that entry's week and day. Removing an
  entry from the plan takes its untouched entries off the calendar on every device, with no op of its own and nothing to
  conflict. Anything the planner changed stays. **Everything that reads the calendar must go through `CalendarBook`**, or
  it will count rows the planner cannot see - deleting a slot was once refused over an entry nobody could see.
- **Changing a plan's start or week count changes the calendar from a date on, never before it**
  (`Shared.Domain/Planning/RotationPhases.cs`). The plan's `StartsOn`/`WeekCount` are its rotation now; its `Earlier`
  field lists the rotations it had, each until the day it changed, written in the same op as the change. So "the week of
  2 Nov is week 1" moves 2 November on, and last week - what was eaten, which batches and serves count - stays as it
  was. Fill changes the rotation from the week filled; the plan editor's week count from today. Filling an older week as
  the week it was changes nothing; filling it as another week replaces every later change. Two devices changing one plan
  at once: the later op's start and history win whole.
- **Saving sends only the fields the planner changed** (`CalendarEntryDraft.ChangesFrom`). A skip sends the status, a
  move the two move fields; neither ever sends the meal or people. As planned, the meal and people are null and follow the
  plan (make soup the usual choice and the untouched entries say soup); choosing the plan's own again sends null back.
  Writing them out with a skip would let an offline phone's skip, clocked later, undo the soup another phone chose
  (`CalendarSyncTests.A_skip_from_a_stale_device_keeps_the_meal_another_device_chose`). The price: a skipped or moved
  entry follows its plan entry, so one whose plan entry is removed shows "meal removed" until a meal is chosen.
- **A past entry still `Planned` counts as eaten** (`PlannedEntry.EatenBy`). Nobody ticks meals off; skipping is the one
  thing recorded. The pantry (slice 11) reads it that way; serves count past and coming entries alike, a skip being the
  only thing that takes one out.

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
| `ServesMade` | a cooked entry's serves made; null: the meal's serves per batch |
| `EatsFrom` | a leftovers entry's batch (a cooked entry's id); null: whatever is left, oldest first |

`PlanTemplate.StartsOn` is the Monday the plan's week 1 began (null until it is first applied), and `PlanTemplate.Earlier`
the rotations before its last change ("2026-11-02|2026-10-05|4;..." - until, start, weeks). It is one decision for the
household, so the later edit wins; the calendar shows "Spreadsheet plan: week 3" from the rotation of the week shown. Nothing is a foreign key: an
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

Moving a plan's start and applying again is safe: from the week filled on, the old untouched entries stop being shown
(their dates no longer map to their week), the new ones are created, and nothing a planner changed is touched; the weeks
before keep their rotation (above). `CalendarBook.ApplyAsync` generates each day by the rotation in effect on it, and
finds what the calendar already holds by id, so a tombstone that never had a date (a delete of an entry the browser never
saw) still counts. Two plans applied to the same week are both shown - a separate toddler plan beside the household's is
a real use.

## Batches and serves

A cooked entry is a batch, and leftovers entries eat from it: [batches and serves](batches-and-serves.md). Two fields of the
row are theirs - `ServesMade` on a cooked entry, `EatsFrom` on a leftovers one - and setting either touches the entry, so it
stays when the plan moves. A skipped entry eats nothing, cooks nothing and costs nothing; a skipped cooked entry flags the
leftovers that would have eaten it.

## What the planner sees

**Calendar** (`/calendar`, the phone's Plan tab): "Week of Mon 26 Oct" with arrows either side, "This week" when elsewhere,
and **Fill from a plan**. Under the title, which week of each plan this is ("Spreadsheet plan: week 3"). The week is in the
address (`?week=2026-10-26`), so a reload stays on it.

- **On a desktop** (960 px and wider) the week is the plan's grid with dates across: each entry is its meal, a dot per
  person when it is not for everyone, and what a serve costs - "$15.00 a serve", "from $3.20 a serve + 1 unpriced", "1 unpriced" (never
  a low number for an incomplete cost), "no ingredients yet", "no cost"; leftovers cost a serve of the batch they eat
  ("leftovers" when they eat none). Under it, a batch's serves or what leftovers eat ([batches and serves](batches-and-serves.md)).
  Today's date is coloured, past days greyed.
- **On a phone** a day at a time: seven day buttons (Mon/26 ...), "Monday 26 October", then the slots down the screen.
- **A skipped entry** stays on its day, struck through, saying "skipped", and is listed under **To replan** below the
  week ("Noodles · Tue 27 Oct · Lunch").

**The month at a glance** (Week / Month on the band; `?month=2026-10`). Planning is mostly done a month at a time, so the
month is a view of its own, on a phone as much as a desktop. Whole weeks, Monday to Sunday, from the week of the 1st to the
week of the last day (`MonthGrid.Days`: four to six weeks; the neighbours' days faint). Each day: its date (today circled,
days gone by quieter) and a **pip per slot**, in slot order - filled where something is planned, a tomato struck ring where
only a skipped entry is, an empty ring where nothing is (`MonthGrid.Mark`; a skip beside something planned is planned). A
day whose every slot is planned has a sage wash. On a desktop each day also lists its meals, cooked ones first, three and
"+2 more". The band: "10 days to come: 4 planned, 1 with gaps, 5 with nothing yet" (`MonthGrid.ToCome` - only days from
today on, since only they can still be planned); under the sheet, "4 of 10 days to come fully planned" and the month's
Eaten. A day opens the week view on that day (`?week=...&day=...`). **Fill from a plan** from the month fills from this
week (this month) or the month's first week (a month to come) - never the days gone by, which would count meals nobody
planned as eaten - and offers the weeks left in the month. "To replan" is under the month too. The household's toddler
slots are slots like any other: a pip each.

**Fill from a plan**: the plan, "The week of Mon 26 Oct is Week [n]" (from the plan's start when it has one), and "this
week" or 2-12 weeks; Fill. It says "Rotation: added 34 entries; 2 already on the calendar, left as they are." **Planning
next week on a phone: the arrow, Fill from a plan, Fill - three taps** once the plan has a start.

**The entry sheet** (tap an entry; full screen on a phone): the date and slot, "From Spreadsheet plan · week 2"; the plan
entry's choices as chips when it has more than one (one tap chooses), a search for any other meal, "Who eats it", and
"Move to" a date and slot. **Skip** answers at once - "we didn't have it" is two taps. A skipped entry's sheet says so and
offers **Not skipped**; picking another day and Save replans it (moved and no longer skipped, one op). **Remove** deletes it
for good: applying the plan again does not bring it back. The + in a cell adds an entry by hand: search, tap, Save. A
name no meal has offers **New meal "Takeaway pizza"** (`cal-new-meal`): the meal editor opens with it filled in, and once
saved the sheet comes back with the new meal chosen and who eats it as it was - planning the week is when a new meal comes
up, and it should not cost a trip to Meals and back. Its ingredients wait; it costs "no ingredients yet" until they are added.

**Deleting something the calendar uses** follows the catalogue's rule: a meal, person or slot named by a coming entry (today
or later, not skipped) is refused, "... is planned in Rotation, the calendar on Tue 27 Oct". Past entries hold nothing back -
they show "meal removed" instead, so a meal eaten once is not undeletable forever.

## As seen

2026-10-22, `.claude/tools/chrome-calendar-check.mjs`, two headless Chromes on fresh profiles - A desktop-sized (1280 px),
B phone-sized (390 px) - against the running app holding the imported plan and earlier runs' data, every check passing: A
makes four meals (one with an unpriced ingredient, a $45 takeaway for 3) and a two-week plan, opens next week's calendar
(the grid) and fills two weeks with next week as week 1 - "added 3 entries"; Monday dinner is the usual choice; costs read
"no ingredients yet", "1 unpriced" and "$15.00 a serve"; the server's plan has `StartsOn` that Monday;
the following week says "week 2". B (the day view) chooses the other choice and A shows it. A skips Tuesday's lunch - struck
through, listed to replan on both; B replans it onto Wednesday's dinner from the list and A shows it there. A adds a takeaway
by hand and removes another entry. Both fill the same two weeks again: "added 0 entries; 3 already on the calendar", and the
server's rows are unchanged. Both go offline and each fills the week after next ("added 2 entries" each); back online the
server holds each of that week's entries once. The six rows (one a tombstone) are identical in the server's SQLite and both
profiles', outboxes empty. Screenshots of the desktop week, the phone day and the phone entry sheet.

`.claude/tools/chrome-month-check.mjs` (2026-10-22, against the demo data, A desktop, B phone): B switches Plan to Month -
the address says the month, whole weeks Monday to Sunday, today marked, a pip per slot on every day; B taps the first empty
day to come and the week view opens on it; B plans a meal in its first slot and the month shows that day partial, its first
pip planned, and one fewer day "with nothing yet"; A's desktop month lists the meal on that day; A skips it and B's month
shows the day empty, its pip skipped; A removes it from "To replan" under the month. Fill from a plan offers the 2 weeks left;
the month after shows "This month", which comes back; Week returns to the week view.

The in-process tests: `Tests/Domain/MonthGridTests.cs` (a month's grid in whole weeks - a month of exactly four, five, six,
a leap February, across a year; each slot's mark; a day full only when every slot is; what is left to plan, from today,
none of the neighbours' days), `Tests/Domain/RotationTests.cs` (Mondays across years and a leap day, week numbers wrapping forwards
and backwards, a rotation across two daylight-saving dates - which only documents that `DateOnly` has no time to shift;
the one real conversion, "today" from `DateTime.Now` in the pages, is untested - the start from "this week is week n",
generation from mid-week and before the start, the day limit, ids, applying again), `Tests/Domain/RotationPhasesTests.cs`
(the rotation of each day, a change cutting off later ones, changing back folding away, the field's spelling, what a plan
change sends) and `Tests/Domain/CalendarRulesTests.cs` (every field's one spelling, the create's fields, who eats it, and
every case of what saving the sheet sends - only what changed) and `Tests/Planning/CalendarSyncTests.cs` (through the real
host: two browsers applying offline make each entry once; a stale browser's apply undoes no skip, move, meal or removal;
a stale browser's skip keeps the meal another chose; untouched entries follow the plan's choices and drop out when the plan
loses an entry; moving the start doubles no day, hides nothing from "is it used", and keeps every week before it; a
dateless tombstone is not created again; a hand-added entry skipped and replanned). Each of the 2026-10-22 review fixes was
checked by breaking it - a rotation read from the current start only, `ComingAsync` not resolving, applying by date range,
a skip writing the meal - and each break failed its test.

---
title: People, slots and plan templates
kind: reference
status: current
module: planning
verified: 2026-10-22
code:
  - AshersLab.MealPlanner.Shared.Domain/Planning
  - AshersLab.MealPlanner.Shared.Data/Planning
  - AshersLab.MealPlanner.WASM/Features/Planning
  - AshersLab.MealPlanner.WASM/Features/Meals/MealEditor.razor
  - AshersLab.MealPlanner.WASM/Components/NameList.razor
  - AshersLab.MealPlanner.Grpc/Features/Catalogue/CatalogueSeed.cs
  - AshersLab.MealPlanner.Tests/Planning
  - AshersLab.MealPlanner.Tests/Domain/PlanningRulesTests.cs
  - .claude/tools/chrome-plans-check.mjs
---

# People, slots and plan templates

Who eats, the slots of the day, and the rotation of weeks the calendar will be filled from. Read before touching a
plan, its entries, or anything that refers to a person, a slot or a meal from a plan. Every edit is an op, as
[offline sync](../sync/offline-sync.md) describes; the meals a plan names are [meals](../meals/meals.md)'.

The calendar - a template applied to real dates, overrides, skipping - is [the calendar](calendar.md); it builds on this.

## Traps first

- **No person is ever seeded, written in a test, a doc or a fixture by a real name.** The repo is public. The planner
  adds the household on the People page; tests use made-up names.
- **An entry is one thing eaten, not one cell.** "Rice cakes, cheese, apple" in one slot is three entries, each one meal;
  "Leftovers/noodles/pizza" is one entry with three choices. A calendar entry eats one meal, so a template entry
  must be something one meal can stand for. A cell that is both ("Takeaway, salad/soup") cannot be one entry, and the
  import leaves it out and says so.
- **Choices are rows, ordered by id.** Two phones each adding a choice offline both keep theirs - a list in one field
  would lose one. The first choice is the usual one; making another the usual one deletes and re-creates the rows
  after it, because a new row always sorts last (`TemplateEntryDraft.ChangesFrom`, tested). Ids written together are
  minted a millisecond apart (`PlanPage.ChoiceCreates`): GUIDv7 orders only to the millisecond.
- **Who eats an entry is one field, set as a whole.** `People` is null for everyone - so someone added to the
  household later is in every such entry - or the sorted ids (`PlanPeople.Format`, one spelling per set). Two phones
  changing it offline: the later one wins, as a rename does. That is right for one decision; it is not a list two
  phones add to.
- **Slot names need not be unique.** The day has two snacks. They are told apart by position, everywhere: the page
  allows the same name twice (`NameList.AllowSameName`), and the import maps the nth "Snack" row to the nth Snack slot.
- **Fewer weeks hides entries, it does not delete them.** An entry's week may be beyond its template's `WeekCount`
  (the count is another row's field, and may be lowered on another device): it is kept, shown nowhere, and back if the
  plan grows again. The editor says how many entries the new count hides.

## The rows

| Row | Fields | Notes |
|---|---|---|
| `Person` | `Name`, `Colour` | `Colour` is `#rrggbb`, lower case, from `PersonRules.Palette`; a new person gets the first nobody has |
| `Slot` | `Name`, `Position` | a hand-set position like a meal group (`ListPosition`); seeded |
| `PlanTemplate` | `Name`, `WeekCount`, `StartsOn`, `Earlier` | 1 to 12 weeks; `StartsOn` is the Monday week 1 began on the calendar, set by applying it; `Earlier` the rotations before a change of start or week count, which keep the days before it as they were ([the calendar](calendar.md)) |
| `TemplateEntry` | `TemplateId`, `Week`, `Day`, `SlotId`, `People` | `Day` by name ("Monday"); `People` above |
| `TemplateChoice` | `EntryId`, `MealId` | one per meal the entry may be |

Rules in `Shared.Domain/Planning`, rows and their op mapping in `Shared.Data/Planning`. None of the references is a
foreign key ([the catalogue](../catalogue/catalogue.md)): an entry of a deleted template, or a choice of a deleted
entry, is kept and shown nowhere; a choice whose meal was deleted elsewhere shows "meal removed"; an entry in a
deleted slot is hidden. `PlanBook` (`Shared.Data/Planning/PlanBook.cs`) is how every page reads plans - live
templates, their live entries, each entry's live choices in order, in one read per table - and it is what answers
"which plans use this meal, slot, person".

**Seeds**: the slots Breakfast, Snack, Lunch, Snack, Dinner, as server ops at clock zero with fixed ids
(`CatalogueSeed.Slots`, `00000000-5eed-7000-8000-000000000401` onwards), exactly as the categories and meal groups are.

## Deleting something a plan uses

The catalogue's rule ([deleting something referenced](../catalogue/catalogue.md#deleting-something-referenced)):
refused while in use, naming what uses it; never cascaded. A coming calendar entry that names a meal, person or slot
itself counts too, and is named as "the calendar on Tue 27 Oct" ([the calendar](calendar.md)).

| Deleting | Refused while | The planner sees |
|---|---|---|
| A meal | a live choice of a live entry names it | "Noodles is planned in Spreadsheet plan. Take it out of the plan first." in the meal editor |
| A slot | a live entry is in it | "Snack is used by 1 plan: ..." on the Slots page |
| A person | a live entry names them | "... is named in Spreadsheet plan. Make those entries for everyone, or someone else, first." |
| A plan | never | its entries and their choices go with it, in one transaction, one op each |
| An entry | never | its choices go with it, in one transaction |

## What the planner sees

**People** (`/people`, "People" in the menu): a "New person" box; each person a row with their colour, tapped to
change the name or pick one of eight colours, or Delete. One tap and a name to add someone.

**Slots of the day** (`/slots`): the same list as meal groups - add, rename, arrows, delete.

**Plans** (`/plans`, "Plans", first in the menu after Home): each plan with "4 weeks · 120 entries"; "New plan" and
"Weeks" boxes, Add opens the new plan.

**A plan** (`/plans/{id}`): its name, "4 weeks, then it repeats", Edit (name, weeks, delete), and Week 1..n buttons.

- **On a desktop** (960 px and wider - MudBlazor's `Md`) the week is a grid: slots down, Mon..Sun across. Each entry is
  a grey block in its cell - "Leftovers / Noodles / Pizza", and a coloured dot per person when it is not for
  everyone - and every cell ends in a dashed **+**.
- **On a phone** the week is a day at a time: Mon..Sun buttons, then the slots down the screen, each with its entries
  (people named in their colour) and a **+**.

**The entry sheet** (full screen on a phone): "Week 2 · Tuesday · Dinner"; the search box, focused, lists meals as
letters are typed; a tap adds one and the box stays up, so a second tap makes a choice ("One of"). Each choice has an
x, and all but the first an arrow to make it the usual one. "Who eats it": Everyone, or one chip per person in their
colour. Save, Cancel, and Remove for an existing entry. **Adding a meal to a slot: +, type a few letters, tap, Save -
three taps.** Saving with no meal says "Pick a meal, or Remove the entry." An entry and its choices are written in one
transaction (`LocalDataPage.WriteAllAsync`); saving unchanged sends nothing.

## As seen

2026-10-21, `.claude/tools/chrome-plans-check.mjs`, two headless Chromes on fresh profiles - A desktop-sized (1280 px),
B phone-sized (390 px) - against the running app: a person added on A is on B with the first palette colour, and B
follows A's colour change; B has the five seeded slots with two snacks; A adds a slot and a third "Snack" and deletes
them, and B follows. A makes a two-week plan and lands on its page, which is the grid on A and the day view on B; on
week 2, Tuesday's dinner, A's + opens the sheet with the search focused and "Week 2 · Tuesday · Dinner", two taps
make two choices, the person's chip makes it theirs, and the cell shows both meals and the dot; B lists the plan as
"2 weeks · 1 entry" and shows the entry with the person named; B makes the second meal the usual one and A's grid
reorders; A cannot delete the meal or the person (each names the plan). The server's rows and both profiles' SQLite
hold the same entry (week 2, Tuesday, the Dinner seed, the person's id) and the same two live choices in the new
order, outboxes empty. B removes the entry and A's grid drops it; A deletes the plan and B's open page says it is
gone; the server keeps the plan, entry and choices as tombstones. Screenshots of the grid, the phone day view and the
phone entry sheet.

2026-10-22, the same script again, every check passing, against a server already holding the real sheet's imported
plan (287 entries) and people from earlier runs: the new person took the first colour nobody else had, and B, a fresh
profile, had the whole import within about 22 s of starting.

The in-process tests: `Tests/Domain/PlanningRulesTests.cs` (every field's one spelling, who eats it, sheet day names,
and what saving an entry sends: unchanged nothing, a removed choice, an added one last, a new usual one rewriting the
rows after it, a meal two phones both added) and `Tests/Planning/PlanSyncTests.cs` (seeds, every field by op and by
snapshot, choices added on two offline browsers both surviving in order, a deleted choice and a deleted template
dropping out of `PlanBook`, who eats it last-writer-wins).

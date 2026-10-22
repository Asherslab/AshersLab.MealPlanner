---
version: 1
slug: "asherslab-mealplanner-wasm-layout-mainlayout-razor"
primary_target: "AshersLab.MealPlanner.WASM/Layout/MainLayout.razor"
related_targets: ["AshersLab.MealPlanner.WASM/Pages/Home.razor"]
---

# The app shell and its working screens

Scope: every screen of the WASM app - Today, Plan (calendar), Shop, Meals, More, Settings and their children. Mode:
Operate. One real user, the household planner, mostly on a phone (PRODUCT.md).

Chosen: The Fridge-Door Pad (the pick) for its form, with two things carried over from The Kitchen Bench by the owner:
its colour palette (sage laminate, white enamel, bottle green, the canister set), and the pair of week tiles ("12 to
get this week", "$146 this week · 1 unpriced") - big number, small label - wherever the week's shop and spend are
summarised.

## Direction contract

THESIS: The week is the planner pad on the fridge door: a printed grid the household writes in, magnets marking who.
Refuses the category default of identical grey cards in a list; content sits on ruled paper, not in boxes.

OWN-WORLD (palette from The Kitchen Bench, by the owner): White-enamel pad paper (#FBFBF6) on a sage laminate ground
(#DDE9DF); the pad's printed ink is bottle green (#2F5D50) - head bands, rules, slot labels, the printed parts, the
app bar. Entries the household wrote are deep ink (#1D2B27, secondary #4E5F59); meal names in a marker face (Kalam),
everything else Atkinson Hyperlegible. The canister set does the marker jobs: tomato (#D9533C) for attention, the
active tab and the strike-through; butter (#F2C14E), sky (#7FB6CF), sage (#6FA27A) and tomato as person magnets.
Hairlines in chrome (#B7C1BD). Pad sheets have a perforated tear edge at the top. Hairline rules divide rows; no drop
shadows beyond the pad's 1px lift.

STORY: Open the app, see today's pad sheet: what's for each slot, whose magnets are on it, what's done (struck through
in red marker, never removed). The Plan tab is the whole week's pad; Shop is a ruled list pad torn into stores.

FIRST VIEWPORT: Bottle-green app bar (app name, sync state); on the sage ground a pad sheet filling the width with a
bottle-green head band (day, week, the household's magnets), slot labels ruled down the left in small green print, meal
names large in marker, a row's own magnets only when it is for some of the household; the two week tiles below the sheet; bottom bar Today, Plan, Shop, Meals, More on enamel with
the active tab in tomato.

FORM: The Fridge-Door Pad, my top-ranked grounded candidate (1 of 7), chosen over the dealt Kitchen Bench (7 of 7).
seed:562970b81ce3d1a3

Signature move: person magnets - round, coloured, with a bottom lip - mark who eats what on Today and Plan, and who an
item is for elsewhere. A person's colour is theirs everywhere.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

## Constraints carried from the hand

- Struck, not removed: ticked shopping lines and skipped meals stay in place, struck through in red marker.
- Unsent edits (not yet on the server) wear a dashed edge, never a new colour.
- Marker face only for names the household wrote (meals, extra shopping items), at 20px and up; never for labels,
  numbers or controls.
- Fonts self-hosted in wwwroot: the app works offline.
- Controls stay standard MudBlazor components, re-themed; no costume of a paper pad's own "interface".

## Resolved

- Person magnet colours: people already carry a colour (Person.Colour, PersonRules.Palette); the palette is now the
  canister family, and a person shows as a magnet, never as a coloured text background.
- Where the magnets go: seen in the running app on 2026-10-22, every row of a day carried all three magnets, because
  nearly every entry is for everyone - the same dots repeated down the sheet said nothing. So the household's magnets
  sit once on a day's or week's head band (Today, Plan), and an entry carries its own only when it is for some of the
  household: the magnet then means "not everyone". The same rule as PeopleDots, where everyone stays quiet.
- The week tile's cost is the week's food (every serve at its meal's cost), not what is left to buy: an empty list is
  $0.00 even when every meal is unpriced, which breaks "a cost that cannot be computed is incomplete, never zero".

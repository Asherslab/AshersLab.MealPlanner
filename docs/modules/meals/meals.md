---
title: Meals
kind: reference
status: current
module: meals
verified: 2026-10-21
code:
  - AshersLab.MealPlanner.Shared.Domain/Meals
  - AshersLab.MealPlanner.Shared.Data/Meals
  - AshersLab.MealPlanner.WASM/Features/Meals
  - AshersLab.MealPlanner.Grpc/Features/Catalogue/CatalogueSeed.cs
  - AshersLab.MealPlanner.Tests/Meals
  - .claude/tools/chrome-meals-check.mjs
---

# Meals

What the household eats, how it is grouped, and what goes into it. Read before touching meals, meal groups or a
meal's ingredients. Every edit is an op, as [offline sync](../sync/offline-sync.md) describes; the ingredients a
meal is made of are [the catalogue](../catalogue/catalogue.md)'s.

## Meal groups

`MealGroup (Id, Name, Position, Deleted)` - the columns of the planner's "meal ideas": Beef, Chicken & egg, Fish,
Other, Individual, Side, Snack. Rules in `Shared.Domain/Meals/MealGroupRules.cs`.

They behave exactly like [shopping categories](../catalogue/catalogue.md#categories): a hand-set position per group
(`ListPosition`), so two phones moving different groups offline both keep their moves; seeded as server ops at clock
zero with fixed ids (`CatalogueSeed.MealGroups`, `00000000-5eed-7000-8000-000000000301` onwards), so a renamed or
deleted seed stays that way. Sort in memory, never in SQL (`MealGroupEntity.InOrder`).

**What the planner sees** (`/meal-groups`, "Meal groups" in the menu): the groups in order under "The meals are
grouped in this order."; a "New meal group" box; per row up and down arrows, a pencil and a bin - the same list as
Stores and Categories (`Components/NameList.razor`).

## As seen

2026-10-21, `.claude/tools/chrome-meals-check.mjs`, two headless Chromes on fresh profiles against the running app:
a new profile has the seven seeded groups in the spreadsheet's order; a group added on A is last on B; moved up one on
A, second to last on B; renamed on A, renamed on B; the server and both profiles' SQLite hold the same row and the
same number of groups, outboxes empty; B deletes it and A drops it. A phone-sized screenshot of the list.

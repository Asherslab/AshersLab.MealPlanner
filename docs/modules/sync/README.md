---
title: Sync
kind: reference
status: current
module: sync
verified: 2026-10-21
code:
  - AshersLab.MealPlanner.LocalData
  - AshersLab.MealPlanner.Shared.Data
  - AshersLab.MealPlanner.Grpc/Features/Sync
---

# Sync

How each browser keeps its own copy of the data and how every copy, and the server's master, stay the same.

- [The browser database](browser-database.md) — SQLite in OPFS, starting it, sharing it across windows, one
  context at a time, local migrations.
- [Offline sync](offline-sync.md) — the op log, the clock and merge rules, push, pull and the change stream,
  the protocol version check, and what the planner sees.

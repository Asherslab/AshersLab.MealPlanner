# AshersLab.MealPlanner.Shared.Domain

Pure functions both ends run: costing, unit handling, pack rounding, serves, receipt-line arithmetic, op
merge rules. The browser computes costs offline from its local data, so these must be the same code on the
server and in WASM — never re-implemented on one side.

`Sync/` holds the op clock and merge rules (`docs/modules/sync/offline-sync.md`); each synced entity's
validation lives beside it (`Ingredients/IngredientRules.cs`). Costing arrives in slice 6 of
`docs/work/2026-10-v1-scope.md`. Applying a merge to EF tables is not here - that is `Shared.Data`, which may
reference EF Core because this project must not.

- **No I/O and no framework.** No EF Core, no gRPC, no protobuf attributes, no `HttpClient`, no clock —
  pass `DateOnly`/`DateTime` in. If a rule needs data, it takes it as arguments.
- **No package references.** Anything added here is downloaded by every phone on first load.
- Money is `long` cents; quantities are `decimal` in the ingredient's base unit (g, ml, each). Never
  `double`.
- **A cost that cannot be computed is incomplete, never zero.** Return a result that says how many
  inputs were missing alongside the partial sum.
- Everything here has tests in `AshersLab.MealPlanner.Tests`. There is no excuse for it not to: these are
  pure functions.

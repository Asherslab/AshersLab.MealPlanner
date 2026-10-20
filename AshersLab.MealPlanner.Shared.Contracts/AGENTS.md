# AshersLab.MealPlanner.Shared.Contracts

The wire contract: protobuf-net records and code-first service interfaces. Referenced by the gRPC host and
the WASM client; both are always rebuilt and shipped together, in one image.

- **Every serialised type carries `[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]`.** Without
  `ImplicitFields` the type serialises as nothing, silently.
- Implicit numbering is positional over the public surface, so **reordering or inserting a property is a
  wire change**. A computed property needs `[ProtoIgnore]` or it goes on the wire too.
- `DateTime` in UTC, never `DateTimeOffset` — protobuf-net has no surrogate for it here.
- Money is `long` cents, quantities `decimal`. Never `double`.
- A service that compiles but is not mapped with `MapGrpcService` on the host fails at the client as
  "unimplemented", not at build. A service with no client registration in `WASM/Program.cs` fails at
  runtime when a page injects it.

Layout follows the GSBC repos: `Entities/Features/<Feature>/`, `Services/Features/<Feature>/`, and
`Messages/Requests|Responses/Features/<Feature>/` when a call needs more than an entity.

Pure rules both ends must agree on — costing, unit conversion, merge rules — do **not** go here. They go in
`AshersLab.MealPlanner.Shared.Domain`.

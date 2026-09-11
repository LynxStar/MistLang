# Inventory, 2026-09-11

What v2 still needs, measured at commit `52759e1e` (the close of Stage 5), so the next inventory
has something to diff against. Counts come from the registers named; the method is under each.

## The ledgers

| Ledger | State | Counted by |
|---|---|---|
| `emitter-v2-capability-progression.yaml`, 186 capabilities | 150 done, 29 partial, 7 missing | `v2_status` per capability |
| `samples/` (76 on disk, 75 in the register) | 72 banked on v2, 4 unbanked | `samples/AllFeaturesV2/mist.toml` references |
| `analysis-v2-capability-progression.yaml`, 12 orders | all done; 54 recorded tasks | `status` and `tasks:` per order |
| `program-model-body-tasks.yaml` | 79 done, 64 partial, 113 open | `[x]`, `[~]`, `[ ]` markers |
| `general.md` | 12 sections, 3 marked critical | section headings |
| Emitter register `uncovered_constructs` | 25, of which 21 need a sample | `disposition` |

## What is needed

1. **Refined parameters are enforced by nobody on v2.** Classic checks a refined or
   `where`-constrained parameter with a runtime guard at method entry (emitter order 144, missing on
   v2) rather than at the call, and analysis order 12 followed classic by not checking the call.
   `Twice(3)` against `AdultAge` compiles and runs on v2. Either emit the guard or decide to check at
   the call; the decision is order 12's first task.

2. **The seven missing emitter capabilities.** Library output type (120) and the `mist test` command
   (122) block the `Testing` sample. Per-member readonly inference (135) blocks the readonly-context
   rules deferred on analysis order 11. Operator overload declarations (185) and the `intrinsic`
   modifier (186) are stdlib-only. Non-public type accessibility (40) and `sealed` (41) are marked
   orphaned because no sample can observe them; analysis order 6 now refuses their misuse, so both
   may only need re-measuring.

3. **The four unbanked samples.** `CSharpInteropV2` and `DelegateFuncConversion` are green
   standalone and blocked by one composition wall: a sample referenced from `AllFeaturesV2` cannot
   see its own `[packages]` and `[framework_references]`. `Testing` waits on item 2.
   `ProjectReferences` is not in the register at all.

4. **The 29 partial emitter capabilities.** The heavier ones: overload and constructor resolution
   (26, 27), type-argument inference and substitution (53, 55), CLR projection (126, 127),
   user-defined conversion insertion (46), retroactive conformance (73), optional operators (79),
   struct inheritance flattening (183), ownership specifiers (154) and implicit clone (157).

5. **The binder's 113 open body tasks**, concentrated in conversions (13), emitter lowering
   annotations (8), context statements (6), member bodies (6), verification (6), extensions (5),
   async/await (5) and operators (5). Two found during Stage 5 are on the analysis register: a
   generic type in element position (`new Pair<int>[3]`) does not resolve, and `Span<T>` erases its
   length to `unknown` rather than an unknown int value, so `int n = span.Length` is refused.

6. **The decisions waiting on the language's owner**, each a `tasks:` entry on its analysis order
   and each a place where a spec is stricter than classic and v2 does what classic does: the
   definite-assignment spec's any-read and `this`-escape rules (order 10); mutable-method hiding on
   owned bindings, field provenance, the cooperation contract and the readonly-context base rules
   (order 11); call-site refinement checks and unknown-length arrays held in fields (order 12).

7. **The descriptor audit.** Classic declares roughly 320 diagnostic ids across its descriptor
   files; v2 declares 71 analysis ids and 104 binder ids. Nobody has sorted classic's into
   binder-refuses, v2-rule-refuses, quirk, or uncovered. Stage 5's coverage is by the register's 91
   tasks, not by that catalogue.

8. **`general.md`'s three criticals.** Array upper bounds deferred to runtime for unknown-length
   types -- order 12 now discharges known-length arrays and any array a local holds, and field-held
   ones still fall to the runtime, as they do on classic; expression-lambda return types not
   validated against the target delegate; and the driver's AST and compilation-unit arrays able to
   go out of sync.

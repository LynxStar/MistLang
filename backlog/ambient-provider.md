> **Reconciliation authority (2026-10-03):** [issues.yaml](issues.yaml) owns
> outstanding work. This document retains contracts and historical evidence; its old
> tasks/statuses are not another current queue. See the [dated reconciliation](inventory-2026-10-03.md).
> Reconciled baseline: `da1aaa8fd416bd3bdf93e496281e7b2cbf52594c`.

# Ambient Provider - Known Gaps

## Extension Method Integration

The ambient provider scans assemblies for extension methods (`GetExtensionMethodsInNamespace`) and caches them, but nothing in the construction pipeline consumes them or wires them into `MistNamespace.Extensions`. When extension method resolution is implemented in the v2 pipeline, the lookup code needs to query both source extensions (from the namespace) and ambient extensions (from the provider).

## Attribute Projection

The provider currently projects CLR custom attributes only onto parameters (`ProjectParameterAttributes`). It does not:

- Project method-level attributes
- Project type-level attributes
- Project property/field-level attributes
- Project `ref`/`out`/`in` parameter modifiers as attributes

### Goal

All C# attributes that have a valid destination in Mist should be projected onto the corresponding model element. The ProgramModel must remain target-agnostic -- no backend-specific enums like `RefKind`.

For CLR metadata that isn't represented as a custom attribute but still needs to travel through the model (e.g., `ref`/`out`/`in` parameter modifiers, `readonly` struct semantics), introduce sentinel attributes. These are synthetic `AttributeData` entries the provider creates during projection to carry that metadata in a uniform way. The emitter knows to look for them and emit the correct target-specific calling convention or modifier.

This means:
- `RefKind` is removed from the ProgramModel and replaced by sentinel attributes
- Any future target-specific metadata follows the same pattern (sentinel attributes, not model fields)
- The attribute system is the single unified mechanism for annotating projected types with backend-relevant information


## Missing Standard Library Types

Union types in Mist have no corresponding Standard Library type that the provider can resolve to.

- `System.ValueTuple`N → done 2026-09-25, and not through the alias map: a closed ValueTuple`2..`8 projects as the structural Mist tuple with its Rest flattened and its `TupleElementNames` applied. Evidence and residue in `provider-tasks.yaml` under `missing_stdlib_types`.
- Union types need a Standard Library representation that the provider can reference when projecting APIs that use discriminated unions or similar patterns


## Extract Factory from AmbientTypeProviderV2

The constructor still builds the namespace index and creates `TypeProjectorV2`. All setup logic (including namespace index building) should live in the factory. The constructor becomes trivial -- takes all pre-computed state as parameters.

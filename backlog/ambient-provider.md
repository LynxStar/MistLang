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

The ambient provider's alias map needs Standard Library types for `ValueTuple` and `UnionType`. Currently when CLR APIs return or accept `ValueTuple<T1, T2, ...>` types, the provider has no alias entry to map them to the Mist tuple representation. Similarly, union types in Mist have no corresponding Standard Library type that the provider can resolve to.

- `System.ValueTuple`N → Standard tuple type (needs alias entry or special-case projection like Func/Action)
- Union types need a Standard Library representation that the provider can reference when projecting APIs that use discriminated unions or similar patterns


## Extract Factory from AmbientTypeProviderV2

The constructor still builds the namespace index and creates `TypeProjectorV2`. All setup logic (including namespace index building) should live in the factory. The constructor becomes trivial -- takes all pre-computed state as parameters.

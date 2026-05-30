# Implementation Plan: Ref Structs

## Overview

This plan implements ref struct support in the Mist compiler, enabling declaration of stack-only value types and consumption of C# ref struct types (e.g., `Span<T>`, `ReadOnlySpan<T>`) from ambient .NET assemblies. The implementation proceeds bottom-up through the compiler pipeline: grammar modifier → semantic model flag → visibility filter lift → type projector → IEnumerable synthesis → type checker restrictions → C# emitter → tooling (TextMate, book, sample). All property tests use in-process compilation via the shared `CompilerDriver` — no per-iteration process spawning.

## Tasks

- [x] 1. Grammar: Add `REF` as a modifier variant
  - [x] 1.1 Add `REF as RefModifier` alternative to the `Modifier` rule in `Source/Mist/Grammar/Mist.storm`
    - Add `| REF as RefModifier` after the existing `ASYNC as AsyncModifier` line
    - This allows `ref struct Foo { ... }` to parse with `ref` captured in the modifiers list
    - _Requirements: 1.1, 1.4, 1.6_

- [x] 2. Semantic model: Register ref struct declarations
  - [x] 2.1 Add `IsRefStruct` property to `StructType` in the semantic model
    - Add `public bool IsRefStruct { get; internal set; }` to the `StructType` class
    - _Requirements: 1.3, 2.1_

  - [x] 2.2 Update `SemanticModelBuilder` to set `IsRefStruct` flag during struct registration
    - In `RegisterStructDeclaration`, check if the modifier list contains a `RefModifier` node; if so, set `IsRefStruct = true`
    - Handle `readonly ref struct` by checking for both `ReadonlyModifier` and `RefModifier`
    - _Requirements: 1.3, 1.6_

  - [x] 2.3 Add validation that `ref` modifier only appears on struct declarations
    - In the type registration pass, if a `RefModifier` appears on a class, interface, trait, or enum declaration, emit diagnostic `MIST2001: The 'ref' modifier is only valid on struct declarations`
    - _Requirements: 1.5_

  - [x] 2.4 Write property test for ref struct parsing round-trip (Property 1)
    - **Property 1: Ref struct parsing round-trip**
    - For any valid identifier and set of struct members, parsing a `ref struct` declaration SHALL produce a `StructType` with `IsRefStruct = true` retaining all declared members
    - Place in `Test/Mist.Tests/Semantics/RefStructParsingPropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 1.1, 1.2, 1.3**

  - [x] 2.5 Write property test for ref modifier on non-struct types (Property 10)
    - **Property 10: Ref modifier on non-struct types produces diagnostic**
    - For any class declaration preceded by the `ref` modifier, the compiler SHALL emit diagnostic `MIST2001`
    - Place in `Test/Mist.Tests/Semantics/RefStructParsingPropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 1.5**

- [x] 3. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Visibility filter: Lift ref struct exclusion
  - [x] 4.1 Remove the `IsByRefLike` exclusion from `IsTypeExcluded` in the visibility filter
    - Remove the check that excludes ref struct types from projection
    - Ref struct types that are public SHALL now be projected like regular struct types
    - _Requirements: 2.2, 2.5_

  - [x] 4.2 Remove the ref struct member exclusion from `IsMemberExcluded`
    - Remove step 3 (`IsRefStruct(member.DeclaringType)` check) so members of ref struct types are projected
    - The `ReferencesExcludedType` check will naturally stop excluding members referencing ref struct types
    - _Requirements: 2.3, 2.5_

- [x] 5. Type projector: Set `IsRefStruct` on projected CLR ref struct types
  - [x] 5.1 Update `ProjectStruct` to set `IsRefStruct = true` when the CLR type `IsByRefLike`
    - After creating the `StructType`, check `clrType.IsByRefLike` and set the flag accordingly
    - _Requirements: 2.1, 2.4_

- [x] 6. IEnumerable synthesis: Duck-type detection for ambient types
  - [x] 6.1 Implement `SynthesizeDuckTypedIEnumerable` in the type projector
    - For types that do NOT already implement `IEnumerable<T>`, check for public parameterless `GetEnumerator()` method
    - Verify the enumerator return type has public `bool MoveNext()` and public `Current` property
    - Resolve `TElement` from `Current`'s return type
    - Synthesize `IEnumerable<TElement>` in the type's interface list
    - Reuse existing `SynthesizeEnumeratorSlots` logic for the enumerator associated type slot
    - _Requirements: 10.1, 10.2, 10.3, 10.6_

  - [x] 6.2 Ensure synthesized interface is not emitted to C# output
    - Since synthesis only applies to ambient types (never emitted), verify no emission path references synthesized interfaces
    - The emitter's foreach lowering calls `GetEnumerator()` on the concrete type, which C#'s duck-typing handles
    - _Requirements: 10.4, 10.5_

- [x] 7. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Type checker: Enforce stack-only usage restrictions
  - [x] 8.1 Add diagnostic for ref struct as field type in non-ref-struct
    - When validating field declarations, if the field type is a ref struct and the containing type is NOT a ref struct, emit `MIST2002`
    - _Requirements: 3.5_

  - [x] 8.2 Add diagnostic for ref struct as property type in non-ref-struct
    - When validating property declarations, if the property type is a ref struct and the containing type is NOT a ref struct, emit `MIST2003`
    - _Requirements: 3.6_

  - [x] 8.3 Add diagnostic for ref struct as generic type argument
    - When resolving type arguments, if any type argument is a ref struct, emit `MIST2004`
    - _Requirements: 3.7_

  - [x] 8.4 Add diagnostic for ref struct in union type
    - When resolving union type constituents, if any constituent is a ref struct, emit `MIST2005`
    - _Requirements: 3.8_

  - [x] 8.5 Add diagnostic for ref struct with optional suffix
    - When resolving optional types (`T?`), if T is a ref struct, emit `MIST2006`
    - _Requirements: 3.9_

  - [x] 8.6 Add diagnostic for ref struct assigned to interface variable
    - When validating assignments and argument passing, if a ref struct value is assigned to an interface-typed target, emit `MIST2007`
    - _Requirements: 3.11, 3.12_

  - [x] 8.7 Write property test for permitted usage sites (Property 2)
    - **Property 2: Ref struct permitted usage sites**
    - For any ref struct type R, using R as local variable, parameter, return type, or field in another ref struct SHALL produce zero diagnostics
    - Place in `Test/Mist.Tests/Analysis/RefStructUsagePropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4**

  - [x] 8.8 Write property test for field/property rejection (Property 3)
    - **Property 3: Ref struct field/property rejection in non-ref-struct types**
    - For any ref struct type R and non-ref-struct type T, declaring a field or property of type R inside T SHALL emit a diagnostic error
    - Place in `Test/Mist.Tests/Analysis/RefStructUsagePropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 3.5, 3.6**

  - [x] 8.9 Write property test for generic type argument rejection (Property 4)
    - **Property 4: Ref struct generic type argument rejection**
    - For any ref struct type R and generic type G<T>, using R as a type argument SHALL emit a diagnostic error
    - Place in `Test/Mist.Tests/Analysis/RefStructUsagePropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 3.7**

  - [x] 8.10 Write property test for union type rejection (Property 5)
    - **Property 5: Ref struct union type rejection**
    - For any ref struct type R and other type U, forming `R | U` SHALL emit a diagnostic error
    - Place in `Test/Mist.Tests/Analysis/RefStructUsagePropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 3.8**

  - [x] 8.11 Write property test for optional type rejection (Property 6)
    - **Property 6: Ref struct optional type rejection**
    - For any ref struct type R, applying `R?` SHALL emit a diagnostic error
    - Place in `Test/Mist.Tests/Analysis/RefStructUsagePropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 3.9**

  - [x] 8.12 Write property test for interface assignment rejection (Property 7)
    - **Property 7: Ref struct interface assignment rejection**
    - For any ref struct type R implementing interface I, assigning R to an I-typed variable SHALL emit a diagnostic error
    - Place in `Test/Mist.Tests/Analysis/RefStructUsagePropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 3.11, 3.12**

  - [x] 8.13 Write property test for member access equivalence (Property 8)
    - **Property 8: Ref struct member access equivalence**
    - For any ref struct type R with public members, accessing those members SHALL resolve successfully using the same rules as regular struct member access
    - Place in `Test/Mist.Tests/Analysis/RefStructUsagePropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

- [x] 9. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. C# emitter: Emit `ref struct` keyword
  - [x] 10.1 Update `EmitStructDeclaration` to emit `ref struct` when `IsRefStruct` is true
    - Check `IsRefStruct` on the resolved `StructType`; if true, emit `ref struct` instead of `struct`
    - All other emission (fields, methods, properties, indexers, constructors) remains unchanged
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

  - [x] 10.2 Write property test for ref struct C# emission (Property 9)
    - **Property 9: Ref struct C# emission**
    - For any Mist-declared ref struct with fields, methods, and properties, the emitted C# SHALL contain `ref struct` in the declaration
    - Place in `Test/Mist.Tests/Backend/RefStructEmissionPropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6**

- [x] 11. TextMate grammar: Add `ref struct` patterns
  - [x] 11.1 Add `ref struct` and generic `ref struct` patterns to `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add non-generic pattern: `\\b(ref)\\s+(struct)\\s+([a-zA-Z_]\\w*)` with captures for `storage.modifier.mist`, `keyword.declaration.mist`, `entity.name.type.struct.mist`
    - Add generic pattern: `\\b(ref)\\s+(struct)\\s+([a-zA-Z_]\\w*)\\s*(<)` with captures for `storage.modifier.mist`, `keyword.declaration.mist`, `entity.name.type.mist`, `punctuation.definition.typeparameters.begin.mist`
    - Place both patterns BEFORE the existing `struct` patterns to ensure `ref struct` is matched as a unit
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

- [x] 12. Sample application
  - [x] 12.1 Create `samples/RefStructs/Program.mist` using the proof code from the requirements verbatim
    - Include the `IntPair` ref struct, `NestedRefStruct` ref struct, and `Program` class with `RunDemo()` exactly as specified
    - Use C#-style (Allman) braces throughout
    - The sample exercises: ref struct declaration, fields, constructors, methods, properties, nested ref structs, ref struct as parameter/return type, `Span<T>` from ambient assembly, `ReadOnlySpan<T>`, indexer write, `Slice()`, and foreach via synthesized IEnumerable
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.6_

  - [x] 12.2 Create `samples/RefStructs/mist.toml` project file
    - Set `name = "RefStructs"` in the project configuration
    - _Requirements: 9.1_

  - [x] 12.3 Register the sample in `samples/AllFeatures/Program.mist` and `samples/AllFeatures/mist.toml`
    - Add `RefStructs.Program.RunDemo()` call to AllFeatures
    - Add project reference `RefStructs = { path = "../RefStructs" }` in AllFeatures mist.toml
    - _Requirements: 9.5_

- [x] 13. Documentation: Book and CSHARP_FEATURES.md updates
  - [x] 13.1 Update `CSHARP_FEATURES.md` with ref struct rows in the "Unsafe / Low-Level" table
    - Add row: Feature `ref struct`, Status `✅`, Notes `Declaration and consumption; stack-only restrictions enforced`
    - Add row: Feature `` `Span<T>` / `Memory<T>` ``, Status `✅`, Notes `Projected from ambient assemblies as ref struct types`
    - Retain all other rows unchanged
    - _Requirements: 8.1, 8.2, 8.3_

  - [x] 13.2 Update `book/src/advanced/dotnet-interop.md` to reflect ref struct projection
    - Replace the `ref struct` row in the "What Gets Hidden" table with information indicating ref structs are now projected as `StructType` with stack-only restrictions
    - Add cross-reference link to the ref struct subsection in the Object Model chapter
    - _Requirements: 7.1_

  - [x] 13.3 Add "Ref Structs" subsection to `book/src/object-model/structs.md`
    - Place after the "When to Use Structs vs Classes" section
    - Cover: `ref struct` declaration syntax, stack-only lifetime semantics, permitted operations, prohibited operations
    - Use tabbed include pattern with `{{#include}}` references to snippet files
    - _Requirements: 7.2, 7.3_

  - [x] 13.4 Create book snippet files for ref struct examples
    - Create `book/snippets/object-model/structs/ref-struct.mist` as a complete compilable program demonstrating a ref struct with at least 1 field and 1 method
    - Run `dotnet run --project tools/SnippetEmitter` to generate the corresponding `.cs` file at `book/snippets/object-model/structs/ref-struct.cs`
    - _Requirements: 7.3, 7.4_

- [x] 14. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- All property tests use in-process compilation via the shared `CompilerDriver` — zero per-iteration process spawns
- The sample app is verified through the existing AllFeatures integration test (no separate process-spawning test needed)
- The SnippetEmitter run in task 13.4 is a one-time tool invocation, not a per-iteration test
- The visibility filter changes (task 4) and type projector changes (task 5) must be done together — lifting the exclusion without setting `IsRefStruct` would project ref structs without the flag

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["2.1", "2.2", "2.3"] },
    { "id": 2, "tasks": ["2.4", "2.5", "4.1", "4.2"] },
    { "id": 3, "tasks": ["5.1"] },
    { "id": 4, "tasks": ["6.1", "6.2"] },
    { "id": 5, "tasks": ["8.1", "8.2", "8.3", "8.4", "8.5", "8.6"] },
    { "id": 6, "tasks": ["8.7", "8.8", "8.9", "8.10", "8.11", "8.12", "8.13"] },
    { "id": 7, "tasks": ["10.1"] },
    { "id": 8, "tasks": ["10.2", "11.1"] },
    { "id": 9, "tasks": ["12.1", "12.2", "13.1", "13.2"] },
    { "id": 10, "tasks": ["12.3", "13.3", "13.4"] }
  ]
}
```

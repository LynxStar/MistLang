# Implementation Plan: Struct Inheritance

## Overview

This plan implements struct inheritance for Mist — a structural composition mechanism for value types. A derived struct contains all fields from its base struct plus its own. The compiler flattens the inheritance at emit time (since the CLR doesn't support struct inheritance) and synthesizes implicit conversion operators for derived-to-base assignment.

No grammar changes are required — the existing `StructDeclaration` rule already parses `: BaseList`.

## Tasks

- [x] 1. Add BaseType property to StructType
  - [x] 1.1 Add `BaseType` property and `GetAncestorChain()` helper to `StructType`
    - Open `Source/Mist/Semantics/Model/Types/StructType.cs`
    - Add `public IType? BaseType { get; internal set; }` property (mirrors `ClassType.BaseType`)
    - Add `public IReadOnlyList<StructType> GetAncestorChain()` method that walks `BaseType` chain collecting ancestors
    - Verify the project builds without warnings
    - _Requirements: 2.1, 2.5_

- [x] 2. Implement struct base type resolution in SemanticModelBuilder
  - [x] 2.1 Add `Pass1_ResolveStructBaseTypes` method
    - Open `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - Add new diagnostic IDs: `MIST_STRUCT_CIRCULAR_INHERITANCE (historical)`, `MIST_STRUCT_MAX_DEPTH (historical)`, `MIST_STRUCT_MULTIPLE_INHERITANCE (historical)`, `MIST_STRUCT_FIELD_HIDING (historical)`, `MIST_STRUCT_INVALID_BASE (historical)`, `MIST0147`, `MIST_STRUCT_BASE_CTOR_MISMATCH (historical)`
    - Implement `Pass1_ResolveStructBaseTypes` that iterates struct symbols, checks if first entry in `interfaceList` resolves to a `StructType`, and sets `structType.BaseType` via `ResolveGenericTypeReference`
    - Emit `MIST_STRUCT_INVALID_BASE (historical)` if base resolves to a class or non-type
    - Call this method from the `Build` method after `Pass1_ResolveBaseTypes` (classes) and before `Pass1_ResolveInterfaceLists`
    - _Requirements: 1.2, 2.1, 2.2, 2.3_

  - [x] 2.2 Add cycle detection and max depth check
    - Implement `DetectStructInheritanceCycles` that walks each struct's ancestor chain with a visited set
    - Emit `MIST_STRUCT_CIRCULAR_INHERITANCE (historical)` on cycle, set `BaseType = null`
    - Emit `MIST_STRUCT_MAX_DEPTH (historical)` if chain exceeds 64 levels, set `BaseType = null`
    - Call from end of `Pass1_ResolveStructBaseTypes`
    - _Requirements: 2.4, 2.5, 2.6_

  - [x] 2.3 Modify `ResolveStructInterfaceList` to skip base struct entry
    - If `structType.BaseType is not null`, set `startIndex = 1` to skip the first entry (already consumed as base struct)
    - Check remaining entries for additional struct types and emit `MIST_STRUCT_MULTIPLE_INHERITANCE (historical)` if found
    - _Requirements: 1.3, 1.5_

  - [x] 2.4 Populate inherited members into derived struct's MemberScope
    - After base type resolution, implement `PopulateInheritedStructMembers` that walks the ancestor chain and injects public fields, methods, and properties into the derived struct's scope
    - Only inject public members; private members remain inaccessible
    - _Requirements: 2.7, 3.2, 3.4, 3.5, 3.6_

  - [x] 2.5 Add field hiding detection
    - During field resolution for structs, check if the field name collides with any ancestor field
    - Emit `MIST_STRUCT_FIELD_HIDING (historical)` diagnostic if collision detected
    - _Requirements: 3.3_

- [x] 3. Implement struct subtype assignability in TypeChecker
  - [x] 3.1 Add struct base-type chain walk in `IsAssignable`
    - Open `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - After the existing class subtype check, add a struct subtype check that walks `sourceStruct.BaseType` chain comparing against `target`
    - Handle `ConstructedType` wrapping `StructType` by unwrapping and checking the underlying definition's ancestor chain with matching type arguments
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 5.3, 5.4_

  - [x] 3.2 Extend `ImplementsInterface` to walk struct ancestor chain
    - In the `StructType` case of `ImplementsInterface`, after checking the struct's own interfaces, walk `structType.BaseType` chain checking each ancestor's interfaces
    - _Requirements: 3.6_

  - [x] 3.3 Add constructor chain validation for struct `: base(...)`
    - When validating a derived struct constructor with a `BaseChain`, resolve the base struct's constructors and verify argument count/types match
    - Emit `MIST_STRUCT_BASE_CTOR_MISMATCH (historical)` if no matching constructor found
    - Emit `MIST0147` if no `: base(...)` and base has no parameterless constructor
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

- [x] 4. Implement C# emission with field flattening
  - [x] 4.1 Emit flattened ancestor fields in `EmitStructDeclaration`
    - Open `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - Before emitting the struct's own members, check if `structType.BaseType is not null`
    - If so, collect all ancestor fields via `GetAncestorChain()` (reversed to root-first order) and emit each field declaration
    - Ensure the emitter does NOT emit `: BaseStruct` in the C# struct header
    - _Requirements: 6.1, 6.4, 6.5_

  - [x] 4.2 Synthesize implicit conversion operators
    - After emitting struct members, call a new `EmitStructImplicitConversions` method
    - For each ancestor in the chain, emit a `public static implicit operator AncestorName(DerivedName value)` that constructs the ancestor by copying field values
    - _Requirements: 6.2, 6.3_

  - [x] 4.3 Inline base constructor assignments
    - When emitting a derived struct constructor that has a `BaseChain`, do NOT emit `: base(...)` (invalid for C# structs)
    - Instead, inline the base constructor's field assignments at the start of the constructor body
    - Map base constructor parameters to the corresponding field assignments
    - _Requirements: 7.5_

- [x] 5. Relax constraint system for struct base-type constraints
  - [x] 5.1 Update `MIST_CONSTRAINT_0005 (historical)` validation
    - Open `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - Modify the constraint validation that emits `MIST_CONSTRAINT_0005 (historical)` to allow a struct as a base-type constraint when the type parameter also has a `struct` constraint
    - Update `ConstraintResolutionTests.StructAsBaseClass_EmitsMIST_CONSTRAINT_0005` to reflect the new behavior
    - _Requirements: 4.4_

- [x] 6. Verify pretty printer round-trip
  - [x] 6.1 Write a round-trip test for struct inheritance syntax
    - Since the grammar and pretty printer don't change, write a test that parses `struct Point3D : Point { int Z; }`, pretty-prints it, re-parses, and asserts the output is identical
    - Test with: single base, base + interfaces, base with type arguments, no base
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

- [x] 7. Write unit tests for semantic model and type checker
  - [x] 7.1 Write `StructInheritanceResolutionTests`
    - Test `BaseType` is populated for single-level inheritance
    - Test `BaseType` chain for multi-level inheritance (A : B : C)
    - Test generic struct inheritance (`Derived<T> : Base<T>`)
    - Test `MIST_STRUCT_CIRCULAR_INHERITANCE (historical)` fires for cycles
    - Test `MIST_STRUCT_MAX_DEPTH (historical)` fires for deep chains
    - Test `MIST_STRUCT_INVALID_BASE (historical)` fires for class/interface as base
    - Test `MIST_STRUCT_MULTIPLE_INHERITANCE (historical)` fires for multiple structs in list
    - Test `MIST_STRUCT_FIELD_HIDING (historical)` fires for duplicate field names
    - _Requirements: 1.2, 1.3, 1.5, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.3_

  - [x] 7.2 Write `StructInheritanceAssignabilityTests`
    - Test derived→base assignment accepted (direct)
    - Test derived→base accepted transitively (C→A through B)
    - Test base→derived rejected
    - Test base→derived rejected transitively
    - Test unrelated struct→struct rejected
    - Test generic struct assignability with matching type args
    - Test interface implementability through ancestor chain
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 5.3, 5.4_

  - [x] 7.3 Write `StructInheritanceConstructorTests`
    - Test `: base(args)` accepted with matching constructor
    - Test `: base(args)` rejected with wrong argument types
    - Test missing `: base(...)` when base has no parameterless constructor
    - Test `: base()` accepted when base has parameterless constructor
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

- [x] 8. Write emitter snapshot tests
  - [x] 8.1 Write `StructInheritanceEmitterTests`
    - Snapshot test: single-level inheritance emits flattened fields + implicit operator
    - Snapshot test: multi-level inheritance emits all ancestor fields + operators for each level
    - Snapshot test: constructor with `: base(args)` emits inlined field assignments
    - Snapshot test: no `: BaseStruct` appears in emitted C# struct header
    - Snapshot test: generic struct inheritance emits correct type arguments in operator
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 7.5_

- [x] 9. Create sample application
  - [x] 9.1 Create `samples/StructInheritance/` project
    - Create `samples/StructInheritance/mist.toml` with `name = "StructInheritance"`
    - Create `samples/StructInheritance/Program.mist` with `RunDemo()` method demonstrating:
      - Single-level inheritance (`Point3D : Point`)
      - Multi-level inheritance (`ColorPoint3D : Point3D`)
      - Base constructor chaining (`: base(x, y)`)
      - Derived-to-base implicit conversion (assignment, parameter passing, return)
      - Field access on derived instances
      - Method access on derived instances
      - Generic struct inheritance
    - Each permutation prints labeled output to stdout
    - _Requirements: 9.1, 9.2, 9.3, 9.5_

  - [x] 9.2 Integrate into AllFeatures orchestrator
    - Add `StructInheritance.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Add `[[references]]` entry pointing to `../StructInheritance` in `samples/AllFeatures/mist.toml`
    - _Requirements: 9.4_

- [x] 10. Update book documentation
  - [x] 10.1 Update `book/src/object-model/structs.md`
    - Update the comparison table: change "No inheritance" → "Single inheritance (field composition, no virtual dispatch)"
    - Add a new section explaining struct inheritance
    - Explain that both class and struct inheritance use static dispatch — no virtual methods
    - Note the behavioral difference: class = runtime subtype (reference), struct = compile-time composition (value copy)
    - Include a Tab_Block showing a struct extending another struct with the emitted C# demonstrating field flattening
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

- [x] 11. Verify TextMate grammar
  - [x] 11.1 Verify struct inheritance highlighting
    - Open `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Verify the existing struct declaration pattern already highlights type names after `:` correctly (since the syntax is unchanged)
    - If needed, add a test file `Editor/mist-syntax-highlighting/tests/struct-inheritance.mist` with struct inheritance examples
    - _Requirements: 11.1, 11.2, 11.3_

- [x] 12. Final verification
  - [x] 12.1 Run full test suite and verify no regressions
    - Run `dotnet test` on the full solution
    - Verify `samples/StructInheritance/` compiles and runs with expected output
    - Verify `samples/AllFeatures/` still compiles and runs (includes StructInheritance)
    - Verify the build produces zero warnings
    - _Requirements: all_

## Notes

- No grammar changes are needed — the existing `StructDeclaration` rule already parses `: BaseList` via the `interfaceList` slot
- The pretty printer already handles struct base lists correctly since the AST structure is unchanged
- The constraint system update (task 5) is needed to avoid breaking existing tests that assert `MIST_CONSTRAINT_0005 (historical)` fires for struct-as-constraint
- Field flattening means the emitted C# struct has no inheritance relationship — all composition is resolved at compile time
- Private members from base structs are NOT accessible in derived structs (handled by existing "undefined member" diagnostics)

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["2.1", "2.2", "2.3", "2.4", "2.5"] },
    { "id": 2, "tasks": ["3.1", "3.2", "3.3", "5.1"] },
    { "id": 3, "tasks": ["4.1", "4.2", "4.3"] },
    { "id": 4, "tasks": ["6.1", "7.1", "7.2", "7.3"] },
    { "id": 5, "tasks": ["8.1"] },
    { "id": 6, "tasks": ["9.1", "9.2"] },
    { "id": 7, "tasks": ["10.1", "11.1"] },
    { "id": 8, "tasks": ["12.1"] }
  ]
}
```

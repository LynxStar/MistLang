# Implementation Plan: Generic Traits

## Overview

Extend the Mist trait system with type parameters (generic traits) and trait inheritance (trait composition). The implementation proceeds through the compiler pipeline: grammar extensions first, then pretty-printing, semantic model construction (type parameter registration, parent trait resolution, cycle detection, with-block resolution for generic traits, satisfaction computation with effective members), type checker extensions, C# emitter extensions, syntax highlighting, and a sample application. Each task builds on the previous ones so the compiler remains buildable at every step.

## Tasks

- [x] 1. Extend grammar for generic traits and trait inheritance
  - [x] 1.1 Modify `TraitDeclaration` and `WithDeclaration` rules in `Mist.storm`
    - Extend `TraitDeclaration` to include optional `typeParams:TypeParameterList?` after the trait name
    - Extend `TraitDeclaration` to include optional `parentTraits:(COLON parents:BaseList)?` after the type parameter list and before the left brace
    - Extend `WithDeclaration` to include optional `traitTypeArgs:TypeArgumentList?` after the trait name identifier
    - The final `TraitDeclaration` rule: `TRAIT name:IDENTIFIER typeParams:TypeParameterList? parentTraits:(COLON parents:BaseList)? LEFT_BRACE members:TraitMemberDeclaration* RIGHT_BRACE`
    - The final `WithDeclaration` rule: `WITH typeName:IDENTIFIER COLON traitName:IDENTIFIER traitTypeArgs:TypeArgumentList? LEFT_BRACE mappings:MemberMapping+ RIGHT_BRACE`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 8.1, 8.2_

  - [x] 1.2 Verify the project builds with the extended grammar
    - Run `dotnet build` to confirm the Storm source generator produces updated AST node types with the new optional fields (`TypeParameterListNode?` on `TraitDeclarationNode`, `BaseListNode?` on `TraitDeclarationNode`, `TypeArgumentListNode?` on `WithDeclarationNode`)
    - Verify existing trait tests still pass (grammar is backward-compatible)
    - _Requirements: 1.3, 2.6_

- [x] 2. Extend pretty-printer for generic traits and trait inheritance
  - [x] 2.1 Add type parameter list and inheritance clause formatting to `MistPrettyPrinter`
    - Extend `EmitTraitDeclaration` to emit optional type parameter list after the trait name (e.g., `trait Comparable<T>`)
    - Extend `EmitTraitDeclaration` to emit optional inheritance clause after the type parameter list (e.g., `trait Sortable<T> : Comparable<T>, OtherTrait`)
    - Format parent trait references with type arguments as `ParentTrait<Arg1, Arg2>` with no spaces inside angle brackets
    - _Requirements: 6.1, 6.2, 6.3, 9.1, 9.2_

  - [x] 2.2 Add type argument list formatting for `with` blocks and trait type references
    - Extend `EmitWithDeclaration` to emit optional type argument list on the trait reference (e.g., `with Product : Comparable<int>`)
    - Ensure constructed trait type references in type positions format as `TraitName<Arg1, Arg2>`
    - _Requirements: 9.3, 9.4_

  - [x] 2.3 Write property test for generic trait and inheritance syntax round-trip
    - **Property 1: Generic trait and inheritance syntax round-trip**
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 6.1, 6.2, 6.3, 7.1, 7.2, 7.5, 8.1, 8.2, 9.1, 9.2, 9.3, 9.4, 9.5**

- [x] 3. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Register trait type parameters in the semantic model
  - [x] 4.1 Extend `SemanticModelBuilder` Pass 1 to register type parameters on traits
    - When a `TraitDeclaration` has a `TypeParameterList`, create a `TypeParameterType(name, ordinal)` for each type parameter
    - Store the list on `traitType.TypeParameters`
    - Register each `TypeParameterType` as a symbol in the trait's member scope so trait member signatures can reference them
    - Check for duplicate type parameter names and emit `MIST_DUPLICATE_TYPE_PARAMETER (historical)` if found
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

  - [x] 4.2 Write property test for trait type parameter registration
    - **Property 2: Trait type parameter registration**
    - **Validates: Requirements 10.1, 10.2, 10.3, 10.4**

- [x] 5. Implement trait inheritance resolution and cycle detection
  - [x] 5.1 Resolve parent trait references in Pass 1
    - After all types are registered, resolve each `BaseTypeRef` in the trait's `parents` list
    - If the name does not resolve to a `TraitType`, emit `MIST_TRAIT_PARENT_NOT_TRAIT (historical)`
    - If the parent trait is generic and no type arguments are provided, emit `MIST_MISSING_TYPE_ARGUMENTS (historical)`
    - If type arguments are provided but arity doesn't match, emit `MIST_ARITY_MISMATCH (historical)`
    - If the parent trait is generic with correct type arguments, construct a `ConstructedTraitType`
    - If the parent trait is non-generic and no type arguments are provided, use the `TraitType` directly
    - Check for duplicate parents (same trait with same type arguments) and emit `MIST_TRAIT_DUPLICATE_PARENT (historical)`
    - Store the resolved list on `traitType.ParentTraits`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.6, 3.7_

  - [x] 5.2 Implement trait inheritance cycle detection
    - Add a dedicated sub-pass `Pass1_DetectTraitCycles` that performs DFS on the trait inheritance graph
    - Build adjacency list from each trait to its parent traits (using `GenericTraitDefinition` for constructed parents)
    - Run DFS with coloring (white/gray/black); when a gray node is revisited, a cycle is detected
    - Emit `MIST_TRAIT_INHERITANCE_CYCLE (historical)` for each trait involved in a cycle
    - Mark cyclic traits so they are excluded from satisfaction computation
    - _Requirements: 3.5_

  - [x] 5.3 Write property test for trait inheritance validation diagnostics
    - **Property 3: Trait inheritance validation diagnostics**
    - **Validates: Requirements 3.2, 3.5, 3.6, 17.6, 17.7, 17.8**

  - [x] 5.4 Write property test for arity and missing type argument diagnostics
    - **Property 4: Arity and missing type argument diagnostics for trait references**
    - **Validates: Requirements 3.3, 3.4, 7.3, 7.4, 8.3, 8.4, 17.1, 17.2, 17.4**

- [x] 6. Implement `ConstructedTraitType` and extend type resolution
  - [x] 6.1 Create `ConstructedTraitType` class
    - Create `Source/Mist/Semantics/Model/Types/ConstructedTraitType.cs` implementing `IType`
    - Properties: `GenericTraitDefinition` (TraitType), `TypeArguments` (IReadOnlyList<IType>), `MemberScope` (delegates to GenericTraitDefinition.MemberScope), `DisplayName` (formatted with type args), `Kind => TypeKind.Trait`
    - _Requirements: 11.1_

  - [x] 6.2 Extend `TraitSatisfactionMap` to key on `IType`
    - Change the trait key from `TraitType` to `IType` to support both `TraitType` (non-generic) and `ConstructedTraitType` (generic)
    - Update all existing call sites to use the new key type
    - _Requirements: 11.1, 14.6_

  - [x] 6.3 Extend `WithBlockRegistry` to key on `IType`
    - Change the trait key from `TraitType` to `IType` to support `ConstructedTraitType`
    - Update all existing call sites
    - _Requirements: 13.1_

  - [x] 6.4 Extend Pass 2 to resolve trait type references to `ConstructedTraitType`
    - When a `NamedTypeRef` resolves to a generic `TraitType` with type arguments provided and arity matches, construct a `ConstructedTraitType`
    - When type arguments are missing for a generic trait, emit `MIST_MISSING_TYPE_ARGUMENTS (historical)`
    - When arity doesn't match, emit `MIST_ARITY_MISMATCH (historical)`
    - When type arguments are provided to a non-generic trait, emit `MIST_NOT_GENERIC (historical)`
    - Handle nested generics in trait type arguments (e.g., `Comparable<List<int>>`)
    - Handle type parameter references as type arguments (e.g., `Comparable<T>` inside a generic class)
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 11.1, 11.2, 11.3, 11.4_

- [x] 7. Extend `with` block resolution for generic traits
  - [x] 7.1 Extend `Pass1_ResolveWithBlocks` to handle `ConstructedTraitType`
    - Resolve `traitName` + `traitTypeArgs` to either a `TraitType` (non-generic) or `ConstructedTraitType` (generic)
    - If the trait is generic and no type arguments are provided, emit `MIST_MISSING_TYPE_ARGUMENTS (historical)`
    - If type arguments are provided but arity doesn't match, emit `MIST_ARITY_MISMATCH (historical)`
    - When validating member mappings, substitute type parameters with type arguments before checking type compatibility
    - Store validated mappings in `WithBlockRegistry` using `ConstructedTraitType` as the key
    - _Requirements: 8.3, 8.4, 13.1, 13.2, 13.3, 13.4, 17.4, 17.5_

  - [x] 7.2 Write property test for with block validation for generic traits
    - **Property 7: With block validation for generic traits**
    - **Validates: Requirements 13.1, 13.2, 13.3, 13.4, 17.5**

- [x] 8. Implement structural match with type argument substitution and effective members
  - [x] 8.1 Compute effective members for traits with inheritance
    - Implement effective member computation: start with the trait's own declared members, walk the parent chain collecting each parent's members (with appropriate type argument substitution for constructed parents), deduplicate by name and compatible signature
    - For `ConstructedTraitType`, substitute type parameters in member signatures before comparing
    - _Requirements: 3.7, 4.1, 4.4_

  - [x] 8.2 Extend `Pass1_ResolveTraitSatisfaction` for generic traits
    - When computing satisfaction against a `ConstructedTraitType`, substitute type parameters with type arguments in trait member signatures before structural matching
    - Method parameter types, return types, and field types are all substituted
    - Reuse the existing `SubstituteType` helper extended for `ConstructedTraitType`
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.6_

  - [x] 8.3 Implement transitive parent satisfaction
    - When a type satisfies a child trait, also register satisfaction for each parent trait with appropriate type argument substitution
    - E.g., satisfying `Sortable<int>` implies satisfying `Comparable<int>` (from `trait Sortable<T> : Comparable<T>`)
    - Handle diamond inheritance: same member from two paths counts once
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 8.4 Write property test for trait inheritance resolution and effective members
    - **Property 5: Trait inheritance resolution and effective members**
    - **Validates: Requirements 3.1, 3.7, 4.1, 4.4**

  - [x] 8.5 Write property test for structural match with type argument substitution
    - **Property 6: Structural match with type argument substitution**
    - **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6**

- [x] 9. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Extend type checker for constructed trait assignability and member access
  - [x] 10.1 Extend `IsAssignable` for `ConstructedTraitType`
    - Add `ConstructedTraitType` case: query `TraitSatisfactionMap` using the constructed trait as key
    - Ensure `Comparable<int>` and `Comparable<string>` are treated as distinct non-assignable types
    - Emit `MIST_TRAIT_UNSATISFIED (historical)` diagnostic with constructed trait name (including type args), type name, and unsatisfied members with substituted types
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 17.3_

  - [x] 10.2 Extend member access for `ConstructedTraitType`
    - When resolving member access on a `ConstructedTraitType`-typed variable, look up the member in the trait's `MemberScope`
    - Substitute type parameters using the constructed trait's type arguments (reuse `SubstituteType` helper)
    - Validate method call arguments against substituted parameter types
    - Emit `MIST_UNDEFINED_MEMBER (historical)` when member name is not found
    - _Requirements: 15.1, 15.2, 15.3_

  - [x] 10.3 Write property test for trait assignability with generic traits
    - **Property 8: Trait assignability with generic traits**
    - **Validates: Requirements 4.2, 4.3, 4.5, 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 17.3**

  - [x] 10.4 Write property test for member access on constructed trait types
    - **Property 9: Member access on constructed trait types with substitution**
    - **Validates: Requirements 15.1, 15.2**

- [x] 11. Extend C# emitter for generic traits
  - [x] 11.1 Emit generic trait as C# generic interface
    - Extend `EmitTraitDeclaration` to emit type parameter list after the interface name (e.g., `interface Comparable<T>`)
    - Trait member signatures use type parameter names directly (no substitution at the interface level)
    - _Requirements: 16.1, 16.2_

  - [x] 11.2 Emit trait inheritance as interface inheritance
    - When a trait extends parent traits, emit parent trait interfaces in the child interface's inheritance list
    - Handle both non-generic parents and constructed generic parents (e.g., `interface Sortable<T> : Comparable<T>`)
    - _Requirements: 5.1, 5.2_

  - [x] 11.3 Emit constructed trait interfaces on satisfying types
    - Extend class/struct emission to include constructed trait interfaces in the inheritance list (e.g., `class IntSorter : Comparable<int>`)
    - When a type satisfies a child trait, emit only the child trait interface (C# handles parent transitivity)
    - Handle open generic trait interfaces (e.g., `class Wrapper<T> : Comparable<T>`)
    - _Requirements: 5.3, 16.3, 16.5_

  - [x] 11.4 Emit explicit implementations with substituted types for `with` mappings
    - For `with`-mapped members on constructed traits, emit explicit interface implementations with substituted types
    - E.g., `int Comparable<int>.CompareTo(int other) => this.Compare(other);`
    - Extend `CSharpTypeMap.GetTypeName` to handle `ConstructedTraitType`
    - Emit constructed trait type references in C# syntax wherever they appear (parameter types, return types, field types)
    - _Requirements: 16.4, 16.6_

  - [x] 11.5 Write property test for C# emission correctness for generic traits
    - **Property 10: C# emission correctness for generic traits**
    - **Validates: Requirements 5.1, 5.2, 5.3, 16.1, 16.2, 16.3, 16.4, 16.5, 16.6**

- [x] 12. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 13. Update TextMate grammar for generic traits
  - [x] 13.1 Add `trait` to the generic declaration-names pattern
    - In `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`, add `trait` to the existing generic declaration-names pattern alongside `class`, `struct`, and `interface`
    - The pattern should match `trait Name<T>` and highlight: `trait` as `keyword.declaration.mist`, `Name` as `entity.name.type.mist`, `<T>` with type parameter scoping
    - Ensure the existing non-generic `trait` declaration-name pattern remains as a fallback for `trait Printable { ... }`
    - _Requirements: 18.1, 18.2, 18.3_

- [x] 14. Create sample application
  - [x] 14.1 Create `samples/GenericTraits/Program.mist`
    - Demonstrate declaring a generic trait with a single type parameter (e.g., `trait Comparable<T> { int CompareTo(T other); }`)
    - Demonstrate declaring a generic trait with multiple type parameters (e.g., `trait Converter<TIn, TOut> { TOut Convert(TIn input); }`)
    - Demonstrate a trait extending another generic trait (e.g., `trait Sortable<T> : Comparable<T> { int Priority(); }`)
    - Demonstrate a trait extending multiple parent traits (e.g., `trait ReadWriteStream : Readable, Writable { ... }`)
    - Demonstrate a type that structurally satisfies a constructed generic trait without explicit declaration
    - Demonstrate a type satisfying a child trait and being assignable to a parent trait-typed variable
    - Demonstrate a `with` block mapping members to a constructed generic trait
    - Demonstrate passing a structurally matched type to a function accepting a constructed trait-typed parameter
    - Demonstrate passing a `with`-mapped type to a function accepting a constructed trait-typed parameter
    - Demonstrate a generic class satisfying a generic trait using the class's own type parameter
    - Demonstrate nested generic type arguments in trait type references (e.g., `Comparable<List<int>>`)
    - Demonstrate trait member access on a constructed trait-typed variable with substituted types
    - Demonstrate `Comparable<int>` and `Comparable<string>` are distinct types (commented-out error case)
    - Demonstrate a compile-time error case (commented out) showing a type satisfying child trait's own members but not inherited parent trait members
    - Each permutation prints labeled output to stdout
    - Include a `public static void RunDemo()` entry point
    - _Requirements: 19.1, 19.2, 19.3, 19.4, 19.5, 19.6, 19.7, 19.8, 19.9, 19.10, 19.11, 19.12, 19.13, 19.14, 19.15, 19.16, 19.17, 19.18_

  - [x] 14.2 Integrate into AllFeatures sample
    - Add `GenericTraits.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Verify AllFeatures continues to compile and run successfully
    - _Requirements: 20.1, 20.2_

- [x] 15. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- All tasks are required (no optional tasks)
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- The implementation language is C#, matching the existing codebase and design document
- The Storm source generator auto-generates AST node types from grammar changes — a successful build after task 1 confirms the new fields are available
- The `TraitSatisfactionMap` and `WithBlockRegistry` key type changes from `TraitType` to `IType` are the central refactoring that enables generic trait support — existing non-generic trait behavior is preserved since `TraitType` implements `IType`
- `ConstructedTraitType` parallels `ConstructedType` for classes/structs/interfaces — the same substitution patterns apply
- Effective members are computed (not stored) to handle diamond inheritance naturally
- The test process spawning policy applies: property-based tests that compile and run Mist programs must use batching (1 compile, 1 run, N output lines)

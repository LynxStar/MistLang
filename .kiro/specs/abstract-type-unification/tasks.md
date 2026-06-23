# Implementation Plan: Abstract Type Unification

## Overview

This plan implements the unification of `interface` and `trait` keywords into a single `abstract` keyword across the entire Mist compiler pipeline. Tasks are ordered by dependency: lexer/grammar first, then semantic model, then SemanticModelBuilder passes, then emitter, then ambient type provider, then samples/book/editor. Each task is atomic and builds on prior work.

## Tasks

- [x] 1. Lexer and Grammar Foundation
  - [x] 1.1 Add ABSTRACT token to keywords.storm and create AbstractDeclaration rule in Mist.storm
    - Add `token ABSTRACT = "abstract";` to `keywords.storm`
    - Add `AbstractDeclaration` rule with modifiers, name, type params, base clause, constraints, and braced body
    - Add `AbstractMemberDeclaration` alternation rule (method slot, property slot, default method, associated types, nested types)
    - Add `AbstractMethodDeclaration` rule (signature terminated by semicolon)
    - Update `TypeDeclaration` rule to include `abstractDeclaration:AbstractDeclaration as AbstractType`
    - Update `WithDeclaration` rule to rename `traitName` field to `abstractName`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9_

  - [x] 1.2 Add error recovery for removed INTERFACE and TRAIT tokens
    - Keep `INTERFACE` and `TRAIT` tokens in `keywords.storm` for recognition
    - In parser error recovery (at declaration positions), detect `INTERFACE` token and emit: "The 'interface' keyword has been removed. Use 'abstract' instead."
    - Detect `TRAIT` token and emit: "The 'trait' keyword has been removed. Use 'abstract' with nominal typing (': AbstractType') or a 'with' block for retroactive conformance."
    - Remove `InterfaceDeclaration` and `TraitDeclaration` rules from valid productions
    - Attempt to parse the body for maximum diagnostic coverage, but do NOT register in semantic model
    - _Requirements: 2.1, 2.2, 2.3, 3.1, 3.2, 3.5_

  - [x] 1.3 Add parser error for property slots with bodies
    - When a property signature in an abstract type body is followed by a braced body, emit: "Property slots cannot have default implementations. Only methods can have defaults."
    - Treat as a slot (discard body) and continue parsing
    - _Requirements: 1.10_

- [x] 2. Semantic Model Types
  - [x] 2.1 Create AbstractType class replacing InterfaceType and TraitType
    - Create `AbstractType : IType` with `DisplayName`, `Kind => TypeKind.Abstract`, `MemberScope`, `ContainingNamespace`, `ParentAbstractTypes`, `TypeParameters`, `IsReadonly`, `IsCyclic`
    - Add `DefaultMethodNames` (`HashSet<string>`) for tracking which methods have defaults
    - Add `DefaultMethods` (`Dictionary<string, SyntaxNode>`) for storing default method bodies
    - Add `TypeKind.Abstract` to the `TypeKind` enum
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 2.2 Create ConstructedAbstractType for generic abstract types
    - Create `ConstructedAbstractType : IType` with `GenericAbstractDefinition`, `TypeArguments`, `DisplayName`, `Kind => TypeKind.Abstract`
    - Replace `ConstructedTraitType` usages with `ConstructedAbstractType`
    - _Requirements: 1.5_

  - [x] 2.3 Create ConformanceRecord and ConformanceMap replacing TraitSatisfactionMap
    - Create `ConformanceRecord` with `FilledSlots`, `WithMappings`, `DefaultsUsed`
    - Create `ConformanceMap` with `Register`, `Conforms`, `GetConformance`, `All` methods
    - Replace `TraitSatisfactionMap` references throughout the codebase
    - _Requirements: 4.1, 4.4, 4.5_

- [x] 3. SemanticModelBuilder — Registration and Resolution
  - [x] 3.1 Implement RegisterAbstractDeclaration replacing RegisterInterfaceDeclaration and RegisterTraitDeclaration
    - Create `AbstractType` instance from `AbstractDeclarationNode`
    - Register methods with bodies in `AbstractType.DefaultMethods` and `DefaultMethodNames`
    - Register methods without bodies (semicolon-terminated) as slots
    - Register property signatures as slots
    - Handle associated type members
    - Remove `RegisterInterfaceDeclaration` and `RegisterTraitDeclaration`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.8_

  - [x] 3.2 Implement Pass1_ResolveAbstractParents and Pass1_DetectAbstractCycles
    - Resolve parent abstract types from the base clause
    - Merge `Pass1_ResolveTraitParents` and `Pass1_DetectInterfaceCycles` into unified logic
    - Detect cyclic abstract type inheritance and emit: "Abstract type '{Name}' is part of a circular inheritance chain"
    - Validate that abstract types only inherit from other abstract types
    - Remove old `Pass1_ResolveTraitParents`, `Pass1_DetectTraitCycles`, `Pass1_DetectInterfaceCycles`
    - _Requirements: 1.7, 4.3_

  - [x] 3.3 Implement Pass1_ValidateAbstractSatisfaction replacing Pass1_ValidateInterfaceSatisfaction
    - For each concrete type declaring `: AbstractType`, verify all slots are filled or have defaults
    - Emit error for each unfilled slot: "Type '{TypeName}' does not implement abstract member '{AbstractType}.{MemberName}({Signature})'"
    - Handle diamond inheritance (single implementation satisfies multiple parents with same slot)
    - Check exact signature matching (name, return type, parameter types)
    - Methods with defaults don't require implementation but accept one (slot filling takes precedence)
    - Remove `Pass1_ValidateInterfaceSatisfaction` and `Pass1_ResolveTraitSatisfaction`
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7_

  - [x] 3.4 Remove structural matching code paths
    - Remove `TryStructuralMatch`, `TryStructuralMatchWithSubstitution`
    - Remove `TryMatchTraitField`, `TryMatchTraitMethod`, `TryMatchTraitProperty` (and substitution variants)
    - Remove all auto-satisfaction logic in `Pass1_ResolveTraitSatisfaction`
    - Ensure no code path attempts to establish conformance without explicit declaration or `with` block
    - _Requirements: 3.3, 3.4_

  - [x] 3.5 Adapt Pass1_ResolveWithBlocks for AbstractType
    - Resolve `with` block target as `AbstractType` instead of `TraitType`
    - Only require mappings for slots without defaults
    - Mapping overrides defaults (if `with` block maps a member to a slot with a default, the mapping wins)
    - Emit error if `with` block references undefined type/abstract type
    - Emit error if member type signature is incompatible with slot
    - Emit error if `with` block establishes redundant conformance for a type that already declares `: AbstractType`
    - No structural pre-matching — all mappings must be explicit
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.7, 6.8, 6.9_

  - [x] 3.6 Implement ResolveAbstractDeclaration for Pass 2 type resolution
    - Resolve method bodies in default methods using the abstract type's full member scope for `this`
    - Resolve associated type constraints
    - Merge logic from `ResolveInterfaceDeclaration` and `ResolveTraitDeclaration`
    - Remove old resolution methods
    - _Requirements: 5.4, 5.6_

- [x] 4. Type Checker — Assignability Rules
  - [x] 4.1 Update assignability rules for abstract types (nominal-only conformance)
    - Assignment to abstract-typed variable: only types with `: AbstractType` or `with` block are assignable
    - No structural conformance — matching public members alone is NOT sufficient
    - Member access on abstract-typed expressions resolves against the abstract type's member scope (slots + defaults visible)
    - Transitive assignability: if `IChild : IParent`, implementer of `IChild` is assignable to `IParent`
    - Emit: "Cannot assign type '{SourceType}' to '{AbstractType}': type does not implement the abstract type"
    - _Requirements: 3.3, 3.4, 9.1, 9.2, 9.3, 9.4_

- [x] 5. Checkpoint — Core pipeline compiles and passes unit tests
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Emitter — C# Code Generation
  - [x] 6.1 Implement EmitAbstractDeclaration replacing EmitInterfaceDeclaration and EmitTraitDeclaration
    - Emit abstract type as C# `interface` declaration
    - Slots (no body) → C# interface method/property signatures
    - Default methods (with body) → C# default interface methods (DIM bodies)
    - Emit associated types using existing mechanism
    - Emit generic type parameters and constraints
    - Remove `EmitInterfaceDeclaration` and `EmitTraitDeclaration`
    - _Requirements: 7.1, 7.2, 7.3, 7.5, 7.7_

  - [x] 6.2 Implement forwarding method emission for default implementations
    - When a concrete type does NOT override a default method, emit a forwarding stub: `public string Default() => ((IAbstract)this).Default();`
    - This ensures Rust-style semantics: calling via concrete type and via abstract reference produces identical behavior
    - Only emit forwarding for methods the concrete type does not provide its own implementation for
    - _Requirements: 5.1, 5.2, 5.3, 5.5, 7.6_

  - [x] 6.3 Implement three-way emission decision logic (slot filling / CLR override / Mist hiding)
    - Slot from abstract type in base clause → implicit interface implementation (no keyword)
    - CLR virtual/abstract method on base class → emit `override` keyword (preserve existing `FindBaseOverrideSlot` logic)
    - Mist concrete base class method → emit `new` keyword (hiding semantics)
    - Emit warning if `new` is missing on a hiding member: "'{TypeName}.{MemberName}' hides inherited member '{BaseTypeName}.{MemberName}'. Use 'new' if intentional."
    - Emit warning if `new` is on a non-hiding member: "The member '{TypeName}.{MemberName}' does not hide an inherited member. The 'new' keyword is not required."
    - _Requirements: 8.4, 8.5, 8.6, 10.1, 10.2, 10.3, 10.4, 10.5_

  - [x] 6.4 Adapt EmitExplicitTraitImplementations to EmitExplicitAbstractImplementations
    - With-block mappings emit as explicit interface implementations: `string INamed.Name() => this.GetLabel();`
    - Handle `with` block mappings that override defaults (mapped member is used instead of default)
    - Rename method and update all call sites
    - _Requirements: 6.2, 6.7_

- [x] 7. Ambient Type Provider — CLR Interop
  - [x] 7.1 Update AssemblyTypeProvider to map CLR types to AbstractType
    - CLR `interface` → `AbstractType` with all members as required slots
    - CLR class with `abstract` methods → required slots on the Mist type representation
    - CLR class with `virtual` methods (with body) → slots with defaults (fillable but not required)
    - Update `IsVirtualOrAbstract` property handling to drive emitter behavior
    - Map `TypeKind.Abstract` directly for CLR interfaces (not old `TypeKind.Interface`)
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

- [x] 8. Checkpoint — Full compiler pipeline compiles and core tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Sample Application
  - [x] 9.1 Create samples/AbstractTypes/Program.mist with proof code
    - Write the proof code verbatim from the requirements document (immutable — do NOT modify)
    - Demonstrates: abstract type with slots only, property slots, default implementations, concrete types filling slots, concrete types taking defaults, overriding defaults, abstract type inheritance, polymorphic variable assignment, polymorphic parameter passing, late binding via `with` blocks, `with` block filling a slot with a default
    - Create `samples/AbstractTypes/AbstractTypes.mistproj` project file
    - Verify compilation and exact expected output match
    - _Requirements: 11.1, 11.2, 11.3, 11.5_

  - [x] 9.2 Integrate AbstractTypes sample into AllFeatures orchestrator
    - Add `AbstractTypes.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Verify AllFeatures compiles and runs with the new sample included
    - _Requirements: 11.4_

  - [x] 9.3 Migrate existing samples (interface → abstract, trait → abstract with nominal conformance)
    - Migrate `samples/Interfaces/` — `interface` → `abstract`
    - Migrate `samples/ShapeInterfaces/` — `interface` → `abstract`
    - Migrate `samples/GenericTypes/` — `interface` → `abstract`
    - Migrate `samples/VoidType/` — `interface` → `abstract`
    - Migrate `samples/Properties/` — `interface` → `abstract`
    - Migrate `samples/GenericConstraints/` — `interface`/`trait` → `abstract` + add `: AbstractType` conformance
    - Migrate `samples/AssociatedTypes/` — `interface`/`trait` → `abstract` + add `: AbstractType` conformance
    - Migrate `samples/DependentTypes/` — `interface`/`trait` → `abstract` + add `: AbstractType` conformance
    - Migrate `samples/NestedScoping/` — `interface`/`trait` → `abstract`
    - Migrate `samples/StructuredContext/` — `interface`/`trait` → `abstract` + add `: AbstractType` conformance
    - Migrate `samples/Enums/` — `interface` → `abstract`
    - Migrate `samples/GenericTraits/` — `trait` → `abstract`; add `: AbstractType` or retain `with` blocks for structural cases
    - Verify each migrated sample compiles and produces unchanged output
    - _Requirements: 11.2 (migration plan)_

  - [x] 9.4 Remove samples/Traits/ directory
    - Delete `samples/Traits/` entirely (structural typing sunsetted)
    - Remove any AllFeatures reference to `Traits.Program.RunDemo()`
    - _Requirements: 11.2 (migration plan — removal)_

- [x] 10. Checkpoint — All samples compile and run with correct output
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Concrete Type Hiding Semantics
  - [x] 11.1 Create samples/HidingDemo proof code and verify hiding behavior
    - Write the proof code from Requirement 10 verbatim into `samples/AbstractTypes/Program.mist` or a dedicated section
    - Verify `new` keyword required, hiding semantics preserved through base-typed reference
    - Verify exact expected output: "via Derived: derived\n  via Base: base"
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

- [x] 12. TextMate Grammar and Book Highlighting
  - [x] 12.1 Update TextMate grammar for abstract keyword
    - Add `abstract` as a declaration keyword with scope `keyword.declaration.mist`
    - Add pattern to highlight identifier following `abstract` as `entity.name.type.abstract.mist`
    - Remove `trait` from keyword list
    - Remove `interface` from keyword list
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

  - [x] 12.2 Update book syntax highlighting files
    - Update `book/theme/highlight.js` — add `abstract` to keyword list, remove `trait` and `interface`
    - Update `book/theme/mist-lang.js` — add `abstract` to keyword list, remove `trait` and `interface`
    - _Requirements: 12.5_

- [x] 13. Book Documentation
  - [x] 13.1 Write "Abstract Types" book chapter
    - Create chapter covering: declaration syntax, slots, default implementations, property slots, abstract type inheritance, implementation by concrete types, polymorphic usage, late binding via `with` blocks
    - Explain Rust-style default semantics (no lens behavior)
    - Explicitly state only methods can have defaults; properties must always be filled
    - Include comparison table: Mist `abstract` vs C# `interface` vs Rust `trait`
    - Document diamond inheritance behavior
    - _Requirements: 13.1, 13.2, 13.3, 13.8, 13.9_

  - [x] 13.2 Create compiled snippets for book chapter
    - Create `.mist` snippet files in `book/snippets/` for: simple abstract with slots, property slots, method defaults, concrete type filling slots, concrete type taking defaults, overriding a default, abstract type inheritance, polymorphic usage, `with` block late binding
    - Generate corresponding `.cs` files via SnippetEmitter
    - _Requirements: 13.4, 13.5_

  - [x] 13.3 Update book SUMMARY.md and remove Traits chapter
    - Add "Abstract Types" chapter reference in the Object Model section
    - Remove or replace "Traits" chapter with a brief sunset note
    - _Requirements: 13.6, 13.7_

- [x] 14. Testing
  - [x] 14.1 Write property test: Parse round-trip (Property 1)
    - **Property 1: Abstract declaration parse round-trip**
    - Generate random abstract type sources (varying identifiers, generic params, constraints, member combinations), verify AST structure
    - Use in-process `CompilerDriver` — no process spawning
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.6, 1.7**

  - [x] 14.2 Write property test: No structural conformance (Property 2)
    - **Property 2: No structural conformance**
    - Generate types with matching members but no `: AbstractType` declaration and no `with` block, verify rejection
    - **Validates: Requirements 3.3, 3.4**

  - [x] 14.3 Write property test: Complete slot filling accepted (Property 3)
    - **Property 3: Complete slot filling accepted**
    - Generate valid implementations matching all slot signatures, verify acceptance
    - **Validates: Requirements 4.1, 4.3**

  - [x] 14.4 Write property test: Missing slot filling rejected (Property 4)
    - **Property 4: Missing slot filling rejected**
    - Generate incomplete implementations, verify error diagnostics list each unfilled slot
    - **Validates: Requirements 4.2, 4.3**

  - [x] 14.5 Write property test: Default methods do not require implementation (Property 5)
    - **Property 5: Default methods do not require implementation**
    - Generate types relying on defaults for all default methods, verify acceptance
    - **Validates: Requirements 4.5, 5.1**

  - [x] 14.6 Write property test: Slot signature matching is exact (Property 6)
    - **Property 6: Slot signature matching is exact**
    - Generate near-miss signatures (wrong return type, wrong parameter type), verify rejection
    - **Validates: Requirements 4.6**

  - [x] 14.7 Write property test: Method dispatch is reference-type-independent (Property 7)
    - **Property 7: Method dispatch is reference-type-independent**
    - Batch-compile programs calling methods via concrete and abstract references, verify identical output
    - Uses batched process spawning (1 compile, 1 run per batch)
    - **Validates: Requirements 5.2, 5.3**

  - [x] 14.8 Write property test: Forwarding methods emitted for defaults (Property 8)
    - **Property 8: Forwarding methods emitted for defaults**
    - Generate concrete types without overrides, inspect emitted C# for forwarding stubs and DIM bodies
    - **Validates: Requirements 5.5, 7.6**

  - [x] 14.9 Write property test: With-block establishes assignability (Property 9)
    - **Property 9: With-block establishes assignability**
    - Generate valid `with` blocks mapping all required slots, verify assignment acceptance
    - **Validates: Requirements 6.1, 6.6**

  - [x] 14.10 Write property test: With-block emits explicit interface implementation (Property 10)
    - **Property 10: With-block emits explicit interface implementation**
    - Inspect emitted C# for explicit interface implementation delegates
    - **Validates: Requirements 6.2**

  - [x] 14.11 Write property test: With-block defaults don't require mappings (Property 11)
    - **Property 11: With-block defaults don't require mappings**
    - Generate `with` blocks omitting default method mappings, verify acceptance
    - **Validates: Requirements 6.3**

  - [x] 14.12 Write property test: Abstract type emits as C# interface (Property 12)
    - **Property 12: Abstract type emits as C# interface**
    - Inspect emitted C# interface structure (slots as signatures, defaults as DIM)
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.5**

  - [x] 14.13 Write property test: CLR ambient type mapping (Property 13)
    - **Property 13: CLR ambient type mapping**
    - Load test assemblies, verify CLR interfaces become `AbstractType` with correct slot/default classification
    - **Validates: Requirements 8.1, 8.2, 8.3**

  - [x] 14.14 Write property test: CLR slot filling emits override (Property 14)
    - **Property 14: CLR slot filling emits override**
    - Generate Mist types filling CLR slots, verify `override` in emitted C#
    - **Validates: Requirements 8.4**

  - [x] 14.15 Write property test: Unfilled CLR virtual slots emit nothing (Property 15)
    - **Property 15: Unfilled CLR virtual slots emit nothing**
    - Generate types not filling virtual slots, verify no method emitted for those slots
    - **Validates: Requirements 8.5**

  - [x] 14.16 Write property test: Abstract-typed assignability (Property 16)
    - **Property 16: Abstract-typed assignability**
    - Generate conforming and non-conforming types, verify correct acceptance/rejection
    - **Validates: Requirements 9.1, 9.2**

  - [x] 14.17 Write property test: Transitive assignability through abstract hierarchy (Property 17)
    - **Property 17: Transitive assignability through abstract hierarchy**
    - Generate abstract type hierarchies, verify parent-type assignment works
    - **Validates: Requirements 9.4**

  - [x] 14.18 Write property test: Concrete class hiding requires new (Property 18)
    - **Property 18: Concrete class hiding requires new and preserves base behavior**
    - Generate class hierarchies with hiding, verify `new` keyword emission and hiding behavior
    - **Validates: Requirements 10.1, 10.2, 10.3**

- [x] 15. Final Checkpoint — Full test suite green, all samples pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties using in-process compilation (no process spawning per iteration)
- The proof code in samples is immutable — the compiler must be made to compile it, not the other way around
- All property tests use FsCheck with minimum 100 iterations and the shared in-process `CompilerDriver`
- Integration tests that spawn processes use batched compilation (1 compile + 1 run per sample)

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "1.3", "2.1"] },
    { "id": 2, "tasks": ["2.2", "2.3"] },
    { "id": 3, "tasks": ["3.1", "12.1", "12.2"] },
    { "id": 4, "tasks": ["3.2", "3.4"] },
    { "id": 5, "tasks": ["3.3", "3.5"] },
    { "id": 6, "tasks": ["3.6", "4.1"] },
    { "id": 7, "tasks": ["6.1"] },
    { "id": 8, "tasks": ["6.2", "6.4"] },
    { "id": 9, "tasks": ["6.3", "7.1"] },
    { "id": 10, "tasks": ["9.1", "9.3", "9.4", "11.1"] },
    { "id": 11, "tasks": ["9.2", "13.1"] },
    { "id": 12, "tasks": ["13.2", "13.3"] },
    { "id": 13, "tasks": ["14.1", "14.2", "14.3", "14.4", "14.5", "14.6"] },
    { "id": 14, "tasks": ["14.7", "14.8", "14.9", "14.10", "14.11", "14.12"] },
    { "id": 15, "tasks": ["14.13", "14.14", "14.15", "14.16", "14.17", "14.18"] }
  ]
}
```

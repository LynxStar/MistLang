# Implementation Plan: Abstract Type Unification

## Overview

This implementation unifies Mist's `interface` and `trait` keywords into a single `abstract` keyword across four sequential phases. Each phase gates on "all tests pass, all samples compile" before proceeding. The compiler is written in C#; generated output also targets C#.

## Tasks

- [ ] 1. Phase 1 — Pure Rename (interface → abstract)

  - [ ] 1.1 Rename grammar layer (keywords.storm and Mist.storm)
    - In `keywords.storm`: rename `INTERFACE` token to `ABSTRACT` with value `"abstract"`
    - In `Mist.storm`: rename `InterfaceDeclaration` rule → `AbstractDeclaration`
    - Rename `InterfaceType` variant in `TypeDeclaration` → `AbstractType`
    - Rename `InterfaceMemberDeclaration` → `AbstractMemberDeclaration`
    - Rename `InterfaceMethodDeclaration` → `AbstractMethodDeclaration`
    - Rename `InterfacePropertyDeclaration` → `AbstractPropertyDeclaration`
    - Rename `InterfaceAccessorDeclaration` → `AbstractAccessorDeclaration`
    - Rename all member variants (`InterfaceMethodMember` → `AbstractMethodMember`, etc.)
    - Add error diagnostic rule: when parser encounters `interface` keyword, emit error
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2.1_

  - [ ] 1.2 Rename semantic model layer
    - Rename `InterfaceType.cs` → `AbstractType.cs`, class `InterfaceType` → `AbstractType`
    - Rename `ConstructedInterfaceType` → `ConstructedAbstractType` (file and class)
    - Update `TypeKind.Interface` → `TypeKind.Abstract`
    - Update `SymbolKind.Interface` → `SymbolKind.Abstract` (if exists)
    - Update all references to these types throughout the semantic model
    - _Requirements: 2.2, 2.5_

  - [ ] 1.3 Rename SemanticModelBuilder passes
    - Rename `RegisterInterfaceDeclaration` → `RegisterAbstractDeclaration`
    - Rename `ResolveInterfaceDeclaration` → `ResolveAbstractDeclaration`
    - Rename `Pass1_ValidateInterfaceSatisfaction` → `Pass1_ValidateAbstractSatisfaction`
    - Rename `Pass1_DetectInterfaceCycles` → `Pass1_DetectAbstractCycles`
    - Rename `Pass1_ResolveInterfaceLists` → `Pass1_ResolveAbstractLists`
    - Rename `ResolveInterfaceInterfaceList` → `ResolveAbstractParentList`
    - Rename `ResolveInterfaceNames` → `ResolveAbstractNames`
    - Rename `RegisterInterfaceMethodDeclaration` → `RegisterAbstractMethodDeclaration`
    - Rename `RegisterInterfacePropertyDeclaration` → `RegisterAbstractPropertyDeclaration`
    - Rename all internal helper methods referencing "Interface" in the Mist-type sense
    - _Requirements: 2.3_

  - [ ] 1.4 Rename CSharpEmitter methods
    - Rename `EmitInterfaceDeclaration` → `EmitAbstractDeclaration`
    - Rename `EmitInterfaceMethodDeclaration` → `EmitAbstractMethodDeclaration`
    - Rename `EmitInterfacePropertyDeclaration` → `EmitAbstractPropertyDeclaration`
    - Verify emitted C# output still produces `interface` keyword (unchanged output)
    - _Requirements: 2.4_

  - [ ] 1.5 Rename test files and test classes
    - Rename `InterfaceSatisfactionPropertyTests.cs` → `AbstractSatisfactionPropertyTests.cs`
    - Rename `InterfaceSignatureMismatchPropertyTests.cs` → `AbstractSignatureMismatchPropertyTests.cs`
    - Rename `InterfaceRoundTripPropertyTests.cs` → `AbstractRoundTripPropertyTests.cs`
    - Update all test class names, method names, and comments referencing Mist "interface"
    - Do NOT rename C# interface declarations (`IType`, `IBackend`, etc.)
    - _Requirements: 2.6_

  - [ ] 1.6 Migrate samples (interface → abstract keyword swap)
    - Rename `samples/Interfaces/` → `samples/AbstractTypes/`
    - Rename `samples/ShapeInterfaces/` → `samples/ShapeAbstracts/`
    - Replace `interface` keyword with `abstract` in all `.mist` files in: `samples/AbstractTypes/`, `samples/ShapeAbstracts/`, `samples/GenericTypes/`, `samples/VoidType/`, `samples/Properties/`, `samples/Enums/`
    - Replace `interface` → `abstract` in mixed samples (defer trait changes to Phase 2): `samples/GenericConstraints/`, `samples/AssociatedTypes/`, `samples/DependentTypes/`, `samples/NestedScoping/`, `samples/StructuredContext/`
    - Update `samples/AllFeatures/Program.mist` to reflect renamed entry points
    - Verify all migrated samples compile and produce identical output
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [ ] 1.7 Update TextMate grammar for Phase 1
    - Remove `interface` from declaration keyword list in `mist.tmLanguage.json`
    - Add `abstract` to declaration keyword scope (`keyword.declaration.mist`)
    - Update type name scope: `entity.name.type.interface.mist` → `entity.name.type.abstract.mist`
    - Update generic declaration pattern: `class|struct|interface|trait` → `class|struct|abstract|trait`
    - Update book's syntax highlighting files (`book/theme/highlight.js`, `book/theme/mist-lang.js`): replace `interface` with `abstract` in Mist keyword list
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

  - [ ] 1.8 Update book documentation for Phase 1
    - Rename `book/src/object-model/interfaces.md` → `book/src/object-model/abstract-types.md`
    - Update chapter title to "Abstract Types" and all content to use `abstract` syntax
    - Update `book/SUMMARY.md` to reference "Abstract Types" in place of "Interfaces"
    - Update all code examples and snippets in `book/snippets/` to use `abstract` keyword
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_

  - [ ] 1.9 Write property test: Abstract Declaration Round-Trip (Property 1)
    - **Property 1: Abstract Declaration Round-Trip**
    - For any valid abstract type declaration, parsing produces an AST that can be printed and re-parsed to an equivalent AST
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7**

  - [ ] 1.10 Phase 1 verification gate — grep for "interface" remnants
    - Run grep for "interface" (case-insensitive) across entire codebase
    - Confirm it ONLY appears in: C# source code (C# `interface` declarations like `interface IType`), ambient type provider CLR handling, C# emission output, third-party/generated code (`obj/`, `bin/`, NuGet), spec files (`.kiro/specs/`)
    - Confirm "interface" does NOT appear in: `.mist` files, `.storm` grammar files, TextMate grammar JSON, book markdown, book highlighting JS (Mist keyword section), test names/comments referring to the Mist concept
    - Run full test suite — all tests must pass with zero failures
    - Compile and run all samples — verify expected output
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

  - [ ] 1.11 Commit Phase 1
    - Commit all Phase 1 changes with message: "Phase 1: Rename interface → abstract (zero semantic change)"
    - _Gate: All tests pass, all samples produce expected output, grep verification clean_

- [ ] 2. Phase 2 — Purge Traits (Pure Deletion)

  - [ ] 2.1 Delete trait grammar and add error diagnostic
    - Remove `TRAIT` token from `keywords.storm`
    - Remove `TraitDeclaration` rule from `Mist.storm`
    - Remove `TraitMemberDeclaration`, `TraitFieldDeclaration`, `TraitMethodDeclaration`, `TraitPropertyDeclaration` rules
    - Remove `TraitType` variant from `TypeDeclaration` rule
    - Remove `WithDeclaration` rule and `MemberMapping` rule from `Mist.storm`
    - Remove `WithItem` variant from `TopLevelItem` rule
    - Add parser error: when `trait` keyword encountered, emit "The 'trait' keyword has been removed. Use 'abstract' with nominal typing (': AbstractType') or a 'with' block for retroactive conformance."
    - Add parser error: when `with` in with-block position encountered, emit error diagnostic
    - NOTE: `WITH` token, `AS` token remain in grammar (used by `WithClause` and `with` statements)
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 8.1, 8.5_

  - [ ] 2.2 Delete trait semantic model classes
    - Delete `TraitType.cs` (class `TraitType`)
    - Delete `ConstructedTraitType.cs` (class `ConstructedTraitType`)
    - Delete `TraitSatisfactionMap.cs` (classes `TraitSatisfaction`, `TraitSatisfactionMap`)
    - Delete `WithBlockRegistry.cs` (classes `ValidatedMemberMapping`, `WithBlockRegistry`)
    - Remove `TypeKind.Trait` from the enum
    - Remove all references to deleted types throughout the codebase
    - _Requirements: 7.5, 8.3_

  - [ ] 2.3 Delete trait SemanticModelBuilder passes
    - Remove `RegisterTraitDeclaration`, `ResolveTraitDeclaration`
    - Remove `Pass1_ResolveTraitParents`, `Pass1_DetectTraitCycles`, `DetectTraitCyclesDfsVisit`
    - Remove `Pass1_ResolveWithBlocks` and all helper methods (`ResolveWithBlockTypeName`, `ResolveWithBlockTraitName`, `FindTraitMember`, etc.)
    - Remove `Pass1_ResolveTraitSatisfaction` and structural matching logic (`TryStructuralMatch`, `TryStructuralMatchWithSubstitution`, `TryMatchTraitField`, `TryMatchTraitMethod`, `TryMatchTraitProperty`)
    - Remove `ResolveExplicitMemberTypesForTrait`, `GetEffectiveMembers` (trait-specific), `CollectEffectiveMembers` (trait-specific)
    - Remove `HasTraitInBaseList`, `HasConstructedTraitInBaseList`, `CollectConstructedTraitsFromBaseList`, `CollectConstructedTraitsFromParents`, `RegisterTransitiveParentSatisfaction`
    - Clean up constraint system: trait constraints become abstract type constraints
    - Remove `ConcreteTypeSatisfiesTrait` from constraint satisfaction
    - _Requirements: 7.5, 7.6, 8.2_

  - [ ] 2.4 Delete trait CSharpEmitter logic
    - Remove `EmitTraitDeclaration` (and `EmitTraitMethodDeclaration`, `EmitTraitPropertyDeclaration`, `EmitTraitFieldAsProperty`)
    - Remove `EmitExplicitTraitImplementations` (with-block emission logic)
    - Remove all references to `TraitSatisfactionMap` and `WithBlockRegistry` from the emitter
    - _Requirements: 7.7, 8.4_

  - [ ] 2.5 Delete trait-related test files
    - Delete `TraitAssignabilityPropertyTests.cs`
    - Delete `TraitMemberAccessPropertyTests.cs`
    - Delete `TraitMemberAccessGenericPropertyTests.cs`
    - Delete `TraitStructuralMatchSubstitutionPropertyTests.cs`
    - Delete `TraitInheritanceValidationPropertyTests.cs`
    - Delete `TraitInterfaceCoexistencePropertyTests.cs`
    - Delete `WithBlockGenericTraitPropertyTests.cs`
    - Delete any other test files whose primary purpose is testing trait behavior
    - _Requirements: 9.2_

  - [ ] 2.6 Migrate and remove trait-related samples
    - Delete `samples/Traits/` directory entirely
    - Update `samples/GenericConstraints/` — replace `trait` with `abstract`, add `: AbstractType` to conforming types
    - Update `samples/AssociatedTypes/` — replace `trait` with `abstract`, add `: AbstractType` conformance
    - Update `samples/DependentTypes/` — replace `trait` with `abstract`, add `: AbstractType` conformance
    - Update `samples/NestedScoping/` — replace `trait` with `abstract`
    - Update `samples/StructuredContext/` — replace `trait` with `abstract`, add `: AbstractType` conformance
    - Rename `samples/GenericTraits/` → `samples/GenericAbstracts/`, replace `trait` → `abstract`, add `: AbstractType` to types using structural satisfaction
    - Update `samples/AllFeatures/Program.mist` — remove `Traits.Program.RunDemo()` call, reflect renamed entry points
    - _Requirements: 9.1, 9.3, 9.4, 9.5_

  - [ ] 2.7 Update TextMate grammar for Phase 2
    - Remove `trait` from declaration keyword list
    - Update generic declaration pattern: `class|struct|abstract|trait` → `class|struct|abstract`
    - Remove `entity.name.type.trait.mist` scope/rule
    - Remove `trait` from book's syntax highlighting files (`book/theme/mist-lang.js`)
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

  - [ ] 2.8 Update book documentation for Phase 2
    - Remove `book/src/object-model/traits.md` (or replace with brief migration note)
    - Remove "Traits" entry from `book/SUMMARY.md`
    - Update or remove cross-references to traits from other chapters
    - _Requirements: 11.1, 11.2, 11.3_

  - [ ] 2.9 Write property test: Nominal Typing Enforcement (Property 8)
    - **Property 8: Nominal Typing Enforcement**
    - For any type that structurally matches all members of an abstract type but does NOT declare `: AbstractType` and has no `with` block, the type SHALL NOT be assignable to the abstract type
    - **Validates: Requirements 7.8**

  - [ ] 2.10 Phase 2 verification gate — grep for "trait" remnants
    - Run grep for "trait" (case-insensitive) across entire codebase
    - Confirm it ONLY appears in: spec files (`.kiro/specs/`), third-party/generated code (`obj/`, `bin/`, NuGet)
    - Confirm "trait" does NOT appear in: `.mist` files, `.storm` grammar files, TextMate grammar JSON, book markdown, book highlighting JS, test file names, test class names, or any active production code paths
    - Run full test suite — all tests must pass with zero failures
    - Compile and run all samples — verify expected output
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

  - [ ] 2.11 Commit Phase 2
    - Commit all Phase 2 changes with message: "Phase 2: Purge traits entirely"
    - _Gate: All tests pass, all samples produce expected output, grep verification clean_

- [ ] 3. Phase 3 — Default Implementations (New Functionality)

  - [ ] 3.1 Extend grammar for default implementations
    - Extend `AbstractMemberDeclaration` rule to accept `defaultMethod:MethodDeclaration as AbstractDefaultMethodMember`
    - Parser must distinguish slots (semicolon-terminated `AbstractMethodDeclaration`) from defaults (body-present `MethodDeclaration`)
    - Add error diagnostic: when a property declaration has a body inside an abstract type, emit "Property slots cannot have default implementations. Only methods can have defaults."
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5_

  - [ ] 3.2 Extend semantic model for default methods
    - Add `DefaultMethods` dictionary to `AbstractType` class: `public Dictionary<string, ISymbol> DefaultMethods { get; } = new();`
    - Extend `SemanticModelBuilder` registration pass: when processing `AbstractDefaultMethodMember` nodes, register the method symbol in both `MemberScope` and `DefaultMethods`
    - _Requirements: 13.1, 14.3_

  - [ ] 3.3 Extend type checker for slot satisfaction with defaults
    - Modify `Pass1_ValidateAbstractSatisfaction`: exclude members in `DefaultMethods` from required slots
    - When a concrete type provides its own implementation matching a default method signature, accept it (filling takes precedence)
    - When signatures don't match exactly for an override attempt, emit signature mismatch error
    - Propagate defaults through abstract type inheritance: implementers of child abstract type are not required to fill inherited defaults
    - Signature matching: verify name, return type, and all parameter types match exactly
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5, 14.6_

  - [ ] 3.4 Extend CSharpEmitter for default interface methods (DIM)
    - For `AbstractDefaultMethodMember` nodes: emit C# default interface method (method with body on the interface)
    - Emit forwarding stub on concrete types that do NOT override a default: `public string Greet() => ((IGreeting)this).Greet();`
    - When a concrete type provides its own implementation, emit only the concrete implementation (no forwarding)
    - Ensure `this` references in default bodies resolve correctly against the abstract type's full member set
    - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5, 16.1, 16.2, 16.3, 16.4, 16.5_

  - [ ] 3.5 Add default implementation samples
    - Update `samples/AbstractTypes/Program.mist` to add: abstract type with default method, concrete type relying on default, concrete type overriding default, calling through both concrete and abstract references
    - Verify Rust-style dispatch semantics: calling default through concrete type reference and abstract type reference produces identical behavior
    - _Requirements: 15.1, 15.2, 15.3, 15.6, 29.2_

  - [ ] 3.6 Update book documentation for Phase 3
    - Add section to "Abstract Types" chapter covering: default implementation syntax, Rust-style semantics, restriction (only methods, not properties), overriding defaults, interaction with abstract type inheritance
    - Include compiled snippet examples demonstrating each scenario
    - _Requirements: 17.1, 17.2, 17.3_

  - [ ] 3.7 Write property test: Slot vs Default Classification (Property 2)
    - **Property 2: Slot vs Default Classification by Body Presence**
    - For any method in an abstract type body, classified as slot iff no body, default iff braced body
    - **Validates: Requirements 13.1, 13.2, 13.5**

  - [ ] 3.8 Write property test: Slot Satisfaction Completeness (Property 3)
    - **Property 3: Slot Satisfaction Completeness**
    - For any abstract type with N slots and M defaults, a concrete type is accepted iff it fills all N slots; M defaults are not required
    - **Validates: Requirements 14.1, 14.2, 14.3, 28.1, 28.2**

  - [ ] 3.9 Write property test: Signature Matching Exactness (Property 4)
    - **Property 4: Signature Matching Exactness**
    - For any slot signature and candidate implementation, accepted iff name, return type, and all parameter types match exactly
    - **Validates: Requirements 14.5, 28.4**

  - [ ] 3.10 Write property test: Inherited Slot Propagation (Property 5)
    - **Property 5: Inherited Slot Propagation**
    - For any abstract type hierarchy, implementers fill all non-default slots from entire inheritance chain
    - **Validates: Requirements 14.6, 28.3**

  - [ ] 3.11 Write property test: Default Dispatch Consistency (Property 6)
    - **Property 6: Default Dispatch Consistency**
    - Calling a default method through concrete-type ref and abstract-type ref produces identical behavior
    - **Validates: Requirements 15.1, 15.2**

  - [ ] 3.12 Write property test: Override Takes Precedence (Property 7)
    - **Property 7: Override Takes Precedence**
    - When a concrete type provides its own implementation for a default, that implementation is used regardless of reference type
    - **Validates: Requirements 15.3**

  - [ ] 3.13 Phase 3 verification gate
    - Run full test suite — all tests must pass with zero failures
    - Compile and run all samples — verify expected output
    - Verify default implementation samples: default taken as-is, default overridden, calling through both reference types produces identical results
    - _Requirements: 18.1, 18.2, 18.3_

  - [ ] 3.14 Commit Phase 3
    - Commit all Phase 3 changes with message: "Phase 3: Add default implementations for abstract types"
    - _Gate: All tests pass, all samples produce expected output_

- [ ] 4. Phase 4 — Late Binding via With Blocks (New Functionality)

  - [ ] 4.1 Re-add with block grammar
    - Re-add `WithDeclaration` rule to `Mist.storm`: `with TypeName : AbstractType { mappings }`
    - Re-add `MemberMapping` rule: `sourceName:IDENTIFIER AS targetName:IDENTIFIER SEMICOLON`
    - Re-add `WithItem` variant to `TopLevelItem`
    - The `WITH` and `AS` tokens already exist (retained in Phase 2 for with-clauses and with-statements)
    - _Requirements: 19.1, 19.2, 19.3, 19.4, 19.5_

  - [ ] 4.2 Implement ConformanceMap semantic model
    - Create `ConformanceMapping` class: `SourceMember`, `TargetSlot`, `SourceName`, `TargetName`
    - Create `ConformanceMap` class with: `Register(IType, IType, ConformanceMapping)`, `HasConformance(IType, IType)`, `GetMappings(IType, IType)`, `All()` enumeration
    - Wire `ConformanceMap` into the semantic model (accessible from `SemanticModelBuilder` and `CSharpEmitter`)
    - _Requirements: 20.1, 20.2_

  - [ ] 4.3 Implement with-block resolution pass in SemanticModelBuilder
    - Add new `Pass1_ResolveWithBlocks` pass processing `WithDeclaration` nodes
    - Resolve `typeName` to a concrete type; emit error if undefined
    - Resolve `abstractName` to an `AbstractType`; emit error if undefined
    - For each `MemberMapping`: find `sourceName` on concrete type, find `targetName` as slot on abstract type
    - Validate signature compatibility (same return type, same parameter types); emit type mismatch error if incompatible
    - Register validated mappings in `ConformanceMap`
    - Validate all required slots (not in `DefaultMethods`) have mappings; emit error listing unmapped slots
    - Error if type already declares `: AbstractType` in base clause (redundant conformance)
    - Error if `typeName` resolves to a CLR/ambient type (not defined in Mist source): emit "Cannot use 'with' to establish conformance for CLR type '{TypeName}'. Only types defined in Mist source can be adapted via 'with' blocks."
    - _Requirements: 20.1, 20.3, 20.4, 20.5, 20.6, 20.7, 20.8, 20.9_

  - [ ] 4.4 Extend type checker for with-block assignability
    - Modify `Pass1_ValidateAbstractSatisfaction`: also consult `ConformanceMap` for assignability
    - A type with with-block conformance is assignable to the abstract type
    - Multiple with-blocks for different abstract types work independently
    - _Requirements: 20.1, 20.2, 20.8, 27.1, 27.2_

  - [ ] 4.5 Implement with-block C# emission
    - For each entry in `ConformanceMap`: add abstract type's interface name to concrete type's C# inheritance list
    - Emit explicit interface implementations delegating to mapped source members (e.g., `string INamed.Name() => this.GetLabel();`)
    - For default methods without mappings: emit forwarding stub (same as Phase 3)
    - For mapped slots that have defaults: mapping takes precedence — emit explicit implementation, no forwarding
    - _Requirements: 21.1, 21.2, 21.3, 21.4_

  - [ ] 4.6 Add with-block samples
    - Update `samples/AbstractTypes/Program.mist` to add: `with` block establishing conformance, `as` mapping syntax, polymorphic usage after with-block conformance, with-block filling a slot that has a default (overriding default via mapping)
    - Ensure final proof code from Requirement 29 is present verbatim in sample
    - Verify sample compiles and produces exact expected output
    - _Requirements: 29.1, 29.2, 29.3, 29.4, 29.5_

  - [ ] 4.7 Update book documentation for Phase 4
    - Add section to "Abstract Types" chapter covering: `with` block syntax, retroactive conformance, `as` mapping keyword, interaction with defaults (only required slots need mappings), multiple `with` blocks on same type
    - Include compiled snippet examples demonstrating each scenario
    - _Requirements: 22.1, 22.2, 22.3_

  - [ ] 4.8 Write property test: With-Block Establishes Assignability (Property 9)
    - **Property 9: With-Block Establishes Assignability**
    - For any valid with-block where all required slot mappings have compatible signatures, the type becomes assignable to the abstract type
    - **Validates: Requirements 20.1, 20.2**

  - [ ] 4.9 Write property test: With-Block Only Requires Non-Default Slot Mappings (Property 10)
    - **Property 10: With-Block Only Requires Non-Default Slot Mappings**
    - A with-block is accepted iff it provides mappings for all N slots; mappings for M defaults are optional
    - **Validates: Requirements 20.3**

  - [ ] 4.10 Write property test: With-Block Signature Validation (Property 11)
    - **Property 11: With-Block Signature Validation**
    - If source member's type signature is incompatible with target slot, compiler emits type mismatch error
    - **Validates: Requirements 20.6**

  - [ ] 4.11 Write property test: Multiple With-Blocks Independence (Property 12)
    - **Property 12: Multiple With-Blocks Independence**
    - Each with-block is validated independently; type is assignable to each abstract type independently
    - **Validates: Requirements 20.8**

  - [ ] 4.12 Write property test: Abstract Type Emission as C# Interface (Property 13)
    - **Property 13: Abstract Type Emission as C# Interface**
    - For any abstract type, emitter produces valid C# `interface`; slots emit as signatures, defaults as DIM
    - **Validates: Requirements 24.1, 24.2, 24.3, 24.5**

  - [ ] 4.13 Write property test: Diamond Inheritance (Property 14)
    - **Property 14: Diamond Inheritance — Single Implementation Satisfies Both**
    - For diamond hierarchy where two parents declare same-signature slot, single implementation satisfies both
    - **Validates: Requirements 28.5**

  - [ ] 4.14 Write property test: Transitive Assignability (Property 15)
    - **Property 15: Transitive Assignability Through Hierarchy**
    - Implementer of child abstract type is assignable to any ancestor abstract type
    - **Validates: Requirements 27.4**

  - [ ] 4.15 Phase 4 verification gate
    - Run full test suite — all tests must pass with zero failures
    - Compile and run all samples — verify expected output
    - Verify with-block samples: basic conformance, `as` mapping, polymorphic usage, interaction with defaults
    - _Requirements: 23.1, 23.2, 23.3_

  - [ ] 4.16 Commit Phase 4
    - Commit all Phase 4 changes with message: "Phase 4: Add late binding via with blocks"
    - _Gate: All tests pass, all samples produce expected output_

- [ ] 5. Final comprehensive verification

  - [ ] 5.1 Final verification gate
    - Grep for "interface" (case-insensitive): confirm only in C# source, ambient type provider, C# emission output, third-party code, spec files
    - Grep for "trait" (case-insensitive): confirm exists NOWHERE except spec files and third-party code
    - Run full test suite — all tests pass with zero failures
    - Compile and run ALL samples — verify expected output
    - Verify `samples/AbstractTypes/Program.mist` proof code compiles and produces exact expected output from Requirement 29
    - Verify `samples/HidingDemo/Program.mist` proof code compiles and produces exact expected output from Requirement 26
    - _Requirements: 30.1, 30.2, 30.3, 30.4, 30.5, 30.6_

## Notes

- All tasks are required
- Each phase gates on "all tests pass, all samples compile" before proceeding to the next phase
- Each phase ends with a commit after passing its verification gate
- Phase 1 is zero semantic change — purely mechanical rename
- Phase 2 is pure deletion — nothing new is added
- Phase 3 and Phase 4 are additive — new functionality
- The C# emitter always emits `interface` in generated C# code (Mist concept is "abstract type" but CLR interop bridge is unchanged)
- Property tests use FsCheck with batched compilation strategy (1 compile, 1 run, N output lines)
- Verification gates ensure no broken state persists between phases

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2"] },
    { "id": 2, "tasks": ["1.3", "1.4"] },
    { "id": 3, "tasks": ["1.5", "1.6", "1.7", "1.8"] },
    { "id": 4, "tasks": ["1.9"] },
    { "id": 5, "tasks": ["1.10"] },
    { "id": 6, "tasks": ["1.11"] },
    { "id": 7, "tasks": ["2.1"] },
    { "id": 8, "tasks": ["2.2"] },
    { "id": 9, "tasks": ["2.3", "2.4"] },
    { "id": 10, "tasks": ["2.5", "2.6", "2.7", "2.8"] },
    { "id": 11, "tasks": ["2.9"] },
    { "id": 12, "tasks": ["2.10"] },
    { "id": 13, "tasks": ["2.11"] },
    { "id": 14, "tasks": ["3.1"] },
    { "id": 15, "tasks": ["3.2"] },
    { "id": 16, "tasks": ["3.3"] },
    { "id": 17, "tasks": ["3.4"] },
    { "id": 18, "tasks": ["3.5", "3.6"] },
    { "id": 19, "tasks": ["3.7", "3.8", "3.9", "3.10", "3.11", "3.12"] },
    { "id": 20, "tasks": ["3.13"] },
    { "id": 21, "tasks": ["3.14"] },
    { "id": 22, "tasks": ["4.1"] },
    { "id": 23, "tasks": ["4.2"] },
    { "id": 24, "tasks": ["4.3"] },
    { "id": 25, "tasks": ["4.4", "4.5"] },
    { "id": 26, "tasks": ["4.6", "4.7"] },
    { "id": 27, "tasks": ["4.8", "4.9", "4.10", "4.11", "4.12", "4.13", "4.14"] },
    { "id": 28, "tasks": ["4.15"] },
    { "id": 29, "tasks": ["4.16"] },
    { "id": 30, "tasks": ["5.1"] }
  ]
}
```

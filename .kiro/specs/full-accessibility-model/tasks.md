# Implementation Plan: Full Accessibility Model

## Overview

This plan wires a C#-style accessibility model (`public`/`private`/`protected`/`internal` plus the `sealed` type modifier) through both Mist pipelines. The classic pipeline gains **full enforcement** (lex -> parse -> resolve defaults in the Semantic Model -> enforce in the Type Checker -> emit explicit C#); the experimental pipeline gains **representation only** (Program Model enum + first-class sealed flag + CLR projection, no enforcement, no emitter changes).

Tasks are sequenced to respect the pipeline order and build incrementally: grammar/lexer keywords -> parser modifier reduction + combination validation -> classic Semantic Model enum, storage, and scope-based default resolution -> Type Checker refactor + full enforcement rule set -> C# backend emission + struct lowering -> experimental Program Model migration -> Ambient Type Provider CLR projection -> existing-violation cleanup -> highlighting -> sample app -> book chapter -> whole-codebase verification. Each step builds on the previous ones and ends wired into the pipeline, with no orphaned code.

Two independent `Accessibility` enums are defined (one per pipeline, per Req 3.5/4.5); `sealed` is always a separate boolean flag, never an enum value. All new diagnostics are added **only** as `DiagnosticDescriptor` fields in the existing registry classes (`ParserDiagnostics`, `TypeCheckDiagnostics`), use IDs unique across every registry, and are emitted only via `DiagnosticBag.Report`/`ReportAtNode` (no inline `new DiagnosticId(` at call sites), keeping this feature consistent with the in-flight `diagnostic-descriptor-system` spec.

## Tasks

- [x] 1. Lexer and grammar keyword tokens
  - [x] 1.1 Add `PROTECTED`, `INTERNAL`, and `SEALED` keyword tokens to the grammar and regenerate the lexer
    - Add `token PROTECTED = "protected";`, `token INTERNAL = "internal";`, `token SEALED = "sealed";` to `Source/Mist/Grammar/tokens/keywords.storm`
    - Regenerate the Storm-derived lexer so `Source/Mist/Lexing/MistLexer.cs` recognizes the new keywords (no hand-written lexer change needed)
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 1.2 Write example tests for keyword lexing
    - Assert each of `public`, `private`, `protected`, `internal`, `sealed` lexes to its keyword token (five example tests) in `Test/Mist.Tests/`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

- [x] 2. Parser modifier rule, reduction, and combination validation
  - [x] 2.1 Extend the shared `Modifier` rule and regenerate the parser
    - Add `PROTECTED as ProtectedModifier`, `INTERNAL as InternalModifier`, `SEALED as SealedModifier` to the `Modifier` rule in `Source/Mist/Grammar/Mist.storm` (rule stays permissive `Modifier*`)
    - Regenerate so the AST gains `ProtectedModifierModifierNode`, `InternalModifierModifierNode`, `SealedModifierModifierNode`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

  - [x] 2.2 Create the classic `Accessibility` enum
    - Add `public enum Accessibility { Public, Private, Protected, Internal }` in `Source/Mist/Semantics/Model/Accessibility.cs` (namespace `Mist.Semantics.Model`)
    - This is the single classic accessibility type; no shared/third cross-pipeline type
    - _Requirements: 3.1, 3.4, 3.5_

  - [x] 2.3 Create the `AccessibilityFacts` reduction helper
    - Add `Source/Mist/Semantics/Model/AccessibilityFacts.cs` with `TryReduceExplicit(IReadOnlyList<ModifierNode>, out int accessModifierCount)` returning the single explicit `Accessibility` (or null when none present) and `HasSealed(IReadOnlyList<ModifierNode>)`
    - `sealed` never counts toward `accessModifierCount`; this helper is the single source of truth for the explicit-modifier reduction
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_

  - [x] 2.4 Add the multiple-access-modifier diagnostic and wire parse-stage combination validation
    - Add a `MultipleAccessModifiers` `DiagnosticDescriptor` (`MIST_PARSE_*`, Error, `PipelineStage.Parsing`) to `Source/Mist/Common/Diagnostics/Descriptors/ParserDiagnostics.cs` with a unique ID; message explains Mist has four standalone levels and no combined levels
    - In `Source/Mist/Parsing/MistParser.cs` post-parse validation, call `AccessibilityFacts.TryReduceExplicit` and, when `accessModifierCount > 1`, emit via `DiagnosticBag.Report`/`ReportAtNode` (no inline `new DiagnosticId(`)
    - _Requirements: 2.6, 14.1, 14.3_

  - [x] 2.5 Write property test for single-modifier reduction
    - **Feature: full-accessibility-model, Property 1: Single access modifier reduces to that accessibility**
    - Pure `AccessibilityFacts` reduction over generated single-access-modifier lists (in-process, no process spawn), min 100 iterations
    - _Validates: Requirements 2.1, 2.2, 2.3, 2.4 (Property 1)_

  - [x] 2.6 Write property test for sealed orthogonality
    - **Feature: full-accessibility-model, Property 2: Sealed is orthogonal to accessibility**
    - Assert adding/removing `sealed` in a generated modifier list leaves the reduced `Accessibility` unchanged and toggles the sealed flag only
    - _Validates: Requirements 2.5 (Property 2)_

  - [x] 2.7 Write property test for multiple-access-modifier rejection
    - **Feature: full-accessibility-model, Property 3: Multiple access modifiers are rejected**
    - Generate lists with two or more distinct access modifiers; assert a multiple-access-modifier error is reported
    - _Validates: Requirements 2.6 (Property 3)_

- [x] 3. Classic Semantic Model: accessibility storage and default resolution
  - [x] 3.1 Add resolved `Accessibility` to the member symbol
    - Add `Accessibility Accessibility { get; }` to `Source/Mist/Semantics/Contracts/ISymbol.cs` and an internally-settable implementation on `Symbol` in `Source/Mist/Semantics/Model/SymbolTable.cs` (fields, methods, constructors, properties, indexers, and type-declaration symbols)
    - _Requirements: 3.1, 3.3_

  - [x] 3.2 Add `Accessibility` and `IsSealed` to the type model objects
    - Add a resolved `Accessibility { get; internal set; }` to `ClassType`, `StructType`, `AbstractType`, `EnumType`, `TypeAliasType`, `NewtypeType` under `Source/Mist/Semantics/Model/Types/`
    - Add `bool IsSealed { get; internal set; }` to `StructType` to mirror the existing `ClassType.IsSealed`; only `ClassType`/`StructType` carry `IsSealed`
    - _Requirements: 3.1, 3.2_

  - [x] 3.3 Resolve scope-based defaults during Semantic Model construction
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, for each declaration call `AccessibilityFacts.TryReduceExplicit`; when no explicit modifier, resolve the scope-kind default (namespace/compilation -> `Internal`; class/struct -> `Private`; interface/enum -> `Public`; nested type -> `Private`) and store the concrete value; set `IsSealed` from `AccessibilityFacts.HasSealed` on `ClassType`/`StructType`
    - Remove reliance on the old "struct members default to public" behavior so struct members default to `Private` exactly as C# (the struct-member flip)
    - After construction every applicable declaration carries a concrete `Accessibility` and every class/struct a concrete `IsSealed`
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.8, 5.10_

  - [x] 3.4 Write property test for scope-kind default resolution
    - **Feature: full-accessibility-model, Property 4: Default accessibility follows the enclosing scope kind**
    - Generate modifier-free declarations in each scope kind; build the model in-process and assert the resolved value matches the scope-kind default table
    - _Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6 (Property 4)_

  - [x] 3.5 Write property test for resolution completeness
    - **Feature: full-accessibility-model, Property 5: Every declaration carries a concrete resolved accessibility**
    - Build the Semantic Model for generated programs in-process; assert no declaration retains an unspecified/default-pending accessibility
    - _Validates: Requirements 3.1, 5.8, 5.10 (Property 5)_

  - [x] 3.6 Write example tests for the sealed flag structure
    - Assert a `sealed` vs non-`sealed` class/struct sets `IsSealed` independently of its `Accessibility` value
    - _Requirements: 3.2_

- [x] 4. Classic Type Checker: refactor and full enforcement
  - [x] 4.1 Add the accessibility `TypeCheckDiagnostics` descriptor entries
    - Add `DiagnosticDescriptor` fields to `Source/Mist/Common/Diagnostics/Descriptors/TypeCheckDiagnostics.cs` for: private-outside-domain, protected-non-derived, internal-cross-assembly, inconsistent-accessibility, base/interface-less-accessible, override/interface-impl violation, nested-type-outside-domain, sealed-inheritance, newtype-over-sealed, sealed-on-alias/non-type (Error), and protected-in-sealed (**Warning**)
    - Assign next-free `MIST_TC_*` IDs unique across all registries, `PipelineStage.Analysis`
    - _Requirements: 14.1, 14.2, 14.3_

  - [x] 4.2 Add the `AccessibilityOrder` partial-order helper
    - Add `Source/Mist/Semantics/Model/AccessibilityOrder.cs` with `IsAtLeastAsAccessibleAs(Accessibility a, Accessibility b)` implementing the C# partial order (`Private < Protected < Public`, `Private < Internal < Public`, `Protected` and `Internal` incomparable)
    - _Requirements: 8.6, 9.4_

  - [x] 4.3 Refactor `TypeCheckerAnalyzer` to consume resolved accessibility
    - In `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`, replace the private `MemberModifiers(IsPublic, IsPrivate, IsStatic)` accessibility reads and the commented-out `AccessLevel` enum with reads of `symbol.Accessibility`; retain only staticness from the AST helper
    - Delete the struct-member-defaults-to-public branch in `CheckMemberAccessModifiers`; the checker never computes a default
    - _Requirements: 5.3, 5.11_

  - [x] 4.4 Implement member-access enforcement and the compilation-as-assembly boundary
    - Add a helper that answers "is this reference cross-assembly?" by treating same-compilation as same-assembly (external = `SymbolOrigin.Backend` or referenced project units)
    - Reject `Private` references from outside the declaring type's domain (permitting nested types of the same declaring type), `Protected` references from non-declaring/non-derived locations, and `Internal` references from an External_Assembly; permit in-domain references with no diagnostic
    - Emit via the `TypeCheckDiagnostics` descriptors from 4.1
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 7.1, 7.2, 7.3, 7.4, 11.3_

  - [x] 4.5 Implement signature-consistency, base/interface, and override/interface-impl enforcement
    - Using `AccessibilityOrder`, report inconsistent-accessibility when any type exposed in a signature (return, parameter, field, property/indexer, generic constraint) is less accessible than the declaration
    - Report when a base class, implemented interface, or base interface is less accessible than the deriving/implementing type
    - Report override and interface-implementation accessibility violations per the C# rule for the position; permit conforming cases
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 9.1, 9.2, 9.3, 9.4, 10.1, 10.2, 10.3_

  - [x] 4.6 Implement nested-type accessibility-domain enforcement
    - Compute a nested type's domain as the intersection of its declared `Accessibility` and its enclosing type's domain; reject references from outside the computed domain; permit references from within
    - _Requirements: 11.1, 11.2_

  - [x] 4.7 Implement sealed enforcement and protected-in-sealed handling
    - Reject deriving from a `sealed` class or `sealed` struct (user-declared or External_Assembly-projected, reading `IsSealed`); permit non-sealed bases and accept `sealed` class/struct declarations
    - Accept a type alias of a sealed type and a refinement over a sealed type (or an alias of one); reject a newtype wrapping a sealed type; reject `sealed` on a type alias or any non-type member (placement check)
    - Fully enforce `Protected` on non-sealed types with no warning; for a `Protected` member inside a `sealed` type, compile and emit exactly one warning-severity diagnostic (the CS0628 analog, the single Allow_But_Warn case)
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8, 12.9, 13.1, 13.2, 13.3_

  - [x] 4.8 Write property test for private member domains
    - **Feature: full-accessibility-model, Property 7: Private members are reachable only within the declaring type's domain**
    - Generate `Private` members plus reference sites (including nested types of the declaring type); compile in-process and assert accept/reject via the DiagnosticBag
    - _Validates: Requirements 7.1, 7.4, 11.3 (Property 7)_

  - [x] 4.9 Write property test for protected member domains
    - **Feature: full-accessibility-model, Property 8: Protected members are reachable only from the declaring or derived types**
    - Generate `Protected` members plus declaring/derived/unrelated reference sites; assert reject outside declaring-or-derived and accept (no warning) within
    - _Validates: Requirements 7.2, 7.4, 13.1 (Property 8)_

  - [x] 4.10 Write property test for the internal compilation boundary
    - **Feature: full-accessibility-model, Property 9: Internal members respect the compilation-as-assembly boundary**
    - Generate `Internal` members with same-compilation and external-marked reference sites; assert accept within and reject across
    - _Validates: Requirements 6.3, 6.4, 7.3 (Property 9)_

  - [x] 4.11 Write property test for accessibility consistency
    - **Feature: full-accessibility-model, Property 10: A declaration is never more accessible than the types in its signature**
    - Generate signatures with randomized accessibilities; assert an inconsistent-accessibility error iff some exposed type is not at least as accessible as the declaration (against the ordering oracle)
    - _Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6 (Property 10)_

  - [x] 4.12 Write property test for base-type and interface accessibility
    - **Feature: full-accessibility-model, Property 11: Base types and interfaces are at least as accessible as the deriving type**
    - Generate type hierarchies; assert a base/interface error iff a base class, implemented interface, or base interface is not at least as accessible as the type
    - _Validates: Requirements 9.1, 9.2, 9.3, 9.4 (Property 11)_

  - [x] 4.13 Write property test for override and interface-implementation rules
    - **Feature: full-accessibility-model, Property 12: Overrides and interface implementations obey position accessibility rules**
    - Generate overriding/implementing members with randomized accessibilities; assert error iff the position's C# rule is violated
    - _Validates: Requirements 10.1, 10.2, 10.3 (Property 12)_

  - [x] 4.14 Write property test for nested-type domains
    - **Feature: full-accessibility-model, Property 13: A nested type's domain is the intersection of its own accessibility and its enclosing type's domain**
    - Generate nested types and reference sites; assert reject outside the computed domain and accept within
    - _Validates: Requirements 11.1, 11.2 (Property 13)_

  - [x] 4.15 Write property test for sealed inheritance
    - **Feature: full-accessibility-model, Property 14: Deriving from a sealed type is rejected, from a non-sealed type is accepted**
    - Generate sealed/non-sealed bases (user-declared and external-marked); assert reject sealed-base derivation, accept non-sealed base and `sealed` declarations
    - _Validates: Requirements 12.1, 12.2, 12.3, 12.4 (Property 14)_

  - [x] 4.16 Write property test for sealed interactions with Mist constructs
    - **Feature: full-accessibility-model, Property 15: Sealed interacts correctly with Mist-specific type constructs**
    - Generate alias/refinement/newtype over sealed types; assert alias and refinement accepted, newtype rejected
    - _Validates: Requirements 12.5, 12.6, 12.7 (Property 15)_

  - [x] 4.17 Write property test for sealed placement
    - **Feature: full-accessibility-model, Property 16: Sealed is valid only on class and struct declarations**
    - Generate `sealed` on aliases, methods, and other non-type members; assert an error is reported
    - _Validates: Requirements 12.9, 13.3 (Property 16)_

  - [x] 4.18 Write property test for protected-in-sealed warning
    - **Feature: full-accessibility-model, Property 17: A protected member in a sealed type compiles with exactly one warning**
    - Generate sealed and non-sealed types with `Protected` members; assert exactly one warning in the sealed case and none in the non-sealed case
    - _Validates: Requirements 13.1, 13.2 (Property 17)_

  - [x] 4.19 Write cross-assembly internal integration test
    - Compile in-process a program that references an External_Assembly (projected/backend-origin) `internal` member and one that references it within the same compilation; assert reject across the boundary and permit within
    - _Requirements: 6.4_

- [x] 5. C# backend emission and struct lowering
  - [x] 5.1 Emit explicit accessibility and `sealed` keywords
    - Extend `EmitModifiers` in `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs` to map `protected`/`internal`/`sealed` and to emit the keyword sequence from the **resolved** `Accessibility` value (emitting explicit `private` rather than relying on the C# default); stop prepending a blanket `public ` on class/struct/enum
    - Emit `sealed` on an emitted class whose sealed flag is set
    - _Requirements: 15.1, 15.2, 15.3_

  - [x] 5.2 Implement struct-to-class lowering for non-value-type structs
    - In `CSharpEmitter.cs`, lower a Mist struct to a C# `class` when and only when it cannot be a legal C# `struct` (declares a base struct, is the base of another struct, or declares a `protected` member), emitting normal inheritance, `protected` members, and `sealed` when set; keep a plain struct as a C# `struct` with `sealed` omitted (C# structs are implicitly sealed)
    - _Requirements: 15.4, 15.5_

  - [x] 5.3 Write property test for explicit emission
    - **Feature: full-accessibility-model, Property 18: Emitted C# carries the explicit accessibility and sealed keywords**
    - Emit C# for generated declarations in-process; assert the emitted keyword sequence matches the resolved `Accessibility` (including explicit `private`) and `sealed` on classes
    - _Validates: Requirements 15.1, 15.2, 15.3 (Property 18)_

  - [x] 5.4 Write round-trip property test for valid C#
    - **Feature: full-accessibility-model, Property 19: Accepted programs emit valid, compilable C#**
    - Emit a generated corpus of accepted programs (including inheritable structs, sealed structs, and `protected` struct members) and compile the C# in-process via Roslyn (`CSharpCompilationDriver`); assert zero C# accessibility/compile errors (no `dotnet` process spawn)
    - _Validates: Requirements 15.4, 15.5 (Property 19)_

- [ ] 6. Checkpoint - Classic pipeline builds and enforces end-to-end
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Experimental Program Model: enum, sealed flag, and default resolution
  - [x] 7.1 Create the experimental `Accessibility` enum
    - Add `public enum Accessibility { Public, Private, Protected, Internal }` in `Source/Mist/ProgramModel/Model/Accessibility.cs` (namespace `Mist.ProgramModel.Model`) as a separate definition from the classic enum (obeys the Program Model isolation rule; no shared/third type)
    - _Requirements: 4.1, 4.4, 4.5_

  - [x] 7.2 Migrate `MemberModifiers` to the single `Accessibility` field
    - In `Source/Mist/ProgramModel/Model/MemberModifiers.cs`, replace `IsPublic`/`IsPrivate`/`IsProtected` with `Accessibility Accessibility { get; init; }` (non-accessibility modifiers unchanged); migrate `PropertyAccessor.IsPrivate` in `Source/Mist/ProgramModel/Model/PropertyAccessor.cs` and every reader (`Source/Mist/ProgramModel/Construction/ModifierBuilder.cs` and consumers, including the Ambient Type Provider projectors) to the enum
    - _Requirements: 4.1, 4.3_

  - [x] 7.3 Add a first-class sealed flag to Program Model type declarations
    - Add a `bool` sealed flag on the Program Model type declarations under `Source/Mist/ProgramModel/Model/Types/`, separate from `MemberModifiers`
    - _Requirements: 4.2, 16.1_

  - [x] 7.4 Resolve scope-based defaults during Program Model construction
    - In the Program Model builders under `Source/Mist/ProgramModel/Construction/`, resolve the same scope-kind default as the classic pipeline and store a concrete `Accessibility` on every declaration plus the sealed flag; add **no** enforcement analyzer and **no** emitter change
    - _Requirements: 4.1, 5.7, 5.9, 5.10, 16.1, 16.3, 16.4_

  - [x] 7.5 Write property test for cross-pipeline default agreement
    - **Feature: full-accessibility-model, Property 6: Both pipelines resolve the same default for the same position**
    - Run the classic and experimental default resolvers on the same generated modifier-free position; assert equal `Accessibility`
    - _Validates: Requirements 5.7 (Property 6)_

- [x] 8. Ambient Type Provider: CLR projection and sealed flag
  - [x] 8.1 Map CLR accessibility onto the four-level enum without broadening
    - Add a shared CLR-accessibility -> Mist `Accessibility` helper (public -> `Public`; family -> `Protected`; family-or-assembly -> `Protected`; private/assembly/family-and-assembly -> `Private`; never `Internal`) and apply it in `FieldProjector`, `MethodProjector`, `PropertyProjector`, `ConstructorProjector`, `IndexerProjector`, `OperatorProjector`, `TypeProjector`, and nested-type projection under `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/`
    - _Requirements: 16.2, 16.6_

  - [x] 8.2 Populate the first-class sealed flag from CLR and reconcile with the sentinel
    - In `AmbientProvider/TypeProjector.cs` (`CreateShell`), set the first-class sealed flag from `clrType.IsSealed`, treating a CLR static class (abstract+sealed) as neither sealed nor abstract; keep applying the `Mist.Sentinel.Sealed` attribute in `AmbientProvider/SentinelAttributes.cs` for emitter compatibility, deriving both representations from the same source so they always agree
    - _Requirements: 16.2, 16.5_

  - [x] 8.3 Write property test for CLR accessibility projection
    - **Feature: full-accessibility-model, Property 20: CLR accessibility projects to the four-level enum without broadening**
    - Pure projection helper over generated CLR accessibility shapes; assert the mapping table and that `Internal` is never produced
    - _Validates: Requirements 16.6 (Property 20)_

  - [x] 8.4 Write property test for CLR sealed projection
    - **Feature: full-accessibility-model, Property 21: CLR sealed state projects to the first-class sealed flag with the static-class exception**
    - Generate CLR sealed/abstract shapes; assert the flag equals `IsSealed` except a static class (abstract+sealed) projects as neither
    - _Validates: Requirements 16.5 (Property 21)_

  - [x] 8.5 Write experimental-representation integration test
    - Build the Program Model and project representative CLR types in-process; assert both the `Accessibility` value and the sealed flag are populated on declarations and projected types/members
    - _Requirements: 4.1, 4.2, 5.9, 16.1, 16.2_

- [x] 9. Existing-violation cleanup under the new rules
  - [x] 9.1 Clean up compiler Mist sources and the standard library
    - Audit compiler-bundled Mist sources and `Source/Standard/*.mist`; apply the struct-member flip fixes with the narrowest sufficient modifier (least privilege), and upgrade unspecified standard-library types/members to explicit `public` (intended-public upgrade so they stay visible across the compilation-as-assembly boundary)
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.5, 18.6, 18.7_

  - [x] 9.2 Clean up samples, snippets, and test `.mist` fixtures
    - Audit existing `samples/**`, `book/snippets/**`, and `Test/**` `.mist` fixtures; add the narrowest explicit modifiers required so they build clean under the new rules (no gratuitous broadening); halt and report if any genuine cross-boundary usage cannot be satisfied without broadening
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.5, 18.6_

- [x] 10. TextMate grammar and book highlighter sync
  - [x] 10.1 Add the new modifiers to the TextMate grammar
    - Add `protected`, `internal`, and `sealed` to the `storage.modifier.mist` pattern in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - _Requirements: 19.1, 19.2, 19.3_

  - [x] 10.2 Sync the book highlighter modifier lists
    - Add the same three keywords to the `MODIFIERS` lists in `book/theme/mist-lang.js` and `book/theme/highlight.js`
    - _Requirements: 19.1, 19.2, 19.3_

  - [x] 10.3 Write grammar scope tests for the new modifiers
    - Assert `protected`, `internal`, and `sealed` scope as `storage.modifier.mist` (example/snapshot tests under `Editor/mist-syntax-highlighting/tests/`)
    - _Requirements: 19.1, 19.2, 19.3_

- [ ] 11. Sample application
  - [ ] 11.1 Create the `FullAccessibilityModel` sample with the locked proof code
    - Create `samples/FullAccessibilityModel/` with `Shape.mist`, `Square.mist`, `Ledger.mist`, `Vector.mist`, `UnitVector.mist`, `Frozen.mist`, and `Program.mist` containing the proof code **verbatim and immutable**, plus `mist.toml` (`name = "FullAccessibilityModel"`)
    - Build and run the sample and confirm the exact locked stdout with zero warnings; if the proof code or its output appears inconsistent with a requirement, halt and request user intervention rather than weakening the implementation or editing the proof code
    - _Requirements: 20.1, 20.2, 20.3, 20.4, 20.6, 20.7_

  - [ ] 11.2 Integrate the sample into AllFeatures
    - Register `FullAccessibilityModel` under `[references]` in `samples/AllFeatures/mist.toml` and add `FullAccessibilityModel.Program.RunDemo();` to `samples/AllFeatures/Program.mist`
    - _Requirements: 20.5_

  - [ ] 11.3 Write the sample proof-code integration test (approved process-spawning test)
    - Compile the sample in-process, then run the built output a small fixed number of times (directly and via `AllFeatures`) and assert the exact locked stdout with zero warnings
    - This is the single approved process-spawning test (compile in-process, spawn only to run the built output, well under the forbidden per-iteration threshold)
    - _Requirements: 20.1, 20.2, 20.3, 20.4, 20.5, 20.6_

- [ ] 12. Book chapter and analog-mapping record
  - [ ] 12.1 Add the Accessibility chapter with compiled snippets
    - Add an Accessibility chapter under the Object Model section of `book/src/` with paired `.mist`/`.cs` snippets under `book/snippets/object-model/accessibility/` (SnippetEmitter-generated); cover the four levels, Mist's omission of `protected internal`/`private protected` and their collapse on projection, scope-based defaults, the compilation-as-assembly boundary for `internal`, `sealed` classes and structs, and the single Allow_But_Warn case; record the analog-mapping decisions
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 21.1, 21.2, 21.3_

  - [ ] 12.2 Reference the chapter in SUMMARY.md
    - Add the new chapter to `book/src/SUMMARY.md` in the Object Model section
    - _Requirements: 21.4_

  - [ ] 12.3 Verify book snippets compile
    - Run the SnippetEmitter over the accessibility snippets and confirm every paired `.mist`/`.cs` snippet compiles
    - _Requirements: 21.3_

- [ ] 13. Whole-codebase verification
  - [ ] 13.1 Build the whole codebase with zero errors and zero warnings
    - Build compiler sources, samples, snippets, and test `.mist` fixtures under the new rules; confirm zero errors and zero warnings (the smoke gate confirming the cleanup and struct-member flip were applied completely)
    - _Requirements: 18.8_

  - [ ] 13.2 Run `dotnet format --verify-no-changes`
    - Run `dotnet format --verify-no-changes` for touched `Source/` files and fix any violations
    - _Requirements: 18.8_

- [ ] 14. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional test sub-tasks and can be skipped for a faster MVP; core implementation sub-tasks are never optional.
- Two independent `Accessibility` enums are defined (classic in `Mist.Semantics.Model`, experimental in `Mist.ProgramModel.Model`); `sealed` is always a separate boolean flag, never an enum value.
- The Type Checker never computes a default: after Semantic Model construction every declaration carries a concrete `Accessibility` (the central invariant enforced by Property 5).
- All new diagnostics are added only as `DiagnosticDescriptor` fields in the existing `ParserDiagnostics`/`TypeCheckDiagnostics` registry classes, use IDs unique across every registry, and are emitted only via `DiagnosticBag.Report`/`ReportAtNode` (no inline `new DiagnosticId(`), keeping this feature consistent with the in-flight `diagnostic-descriptor-system` spec.
- Enforcement, reduction, and default property tests (Properties 1-17) compile Mist in-process via a shared `CompilerDriver` and inspect the `DiagnosticBag`; the round-trip test (Property 19) compiles emitted C# in-process via Roslyn; CLR-projection tests (Properties 20-21) exercise pure projection helpers. None spawns a process per iteration.
- The only process-spawning test is the approved sample proof-code integration test (task 11.3), which compiles in-process and spawns only to run the built output a small fixed number of times.
- The locked proof code is immutable during implementation (Req 20.7): if it or its expected output appears inconsistent with a requirement, halt and request user intervention rather than weakening the implementation or the proof code.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.2", "4.1", "4.2", "7.1", "10.1", "10.2"] },
    { "id": 1, "tasks": ["2.1", "3.1", "3.2", "7.2", "7.3", "1.2", "10.3"] },
    { "id": 2, "tasks": ["2.3", "7.4", "8.1"] },
    { "id": 3, "tasks": ["2.4", "3.3", "8.2", "2.5", "2.6", "2.7", "8.3"] },
    { "id": 4, "tasks": ["4.3", "5.1", "3.4", "3.5", "3.6", "7.5", "8.4", "8.5"] },
    { "id": 5, "tasks": ["4.4", "5.2", "5.3"] },
    { "id": 6, "tasks": ["4.5", "5.4", "4.8", "4.9", "4.10", "4.19"] },
    { "id": 7, "tasks": ["4.6", "4.11", "4.12", "4.13"] },
    { "id": 8, "tasks": ["4.7", "4.14"] },
    { "id": 9, "tasks": ["9.1", "9.2", "4.15", "4.16", "4.17", "4.18"] },
    { "id": 10, "tasks": ["11.1", "12.1"] },
    { "id": 11, "tasks": ["11.2", "12.2", "12.3"] },
    { "id": 12, "tasks": ["11.3", "13.1"] },
    { "id": 13, "tasks": ["13.2"] }
  ]
}
```

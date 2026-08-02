# Requirements Document

## Introduction

This feature unifies Mist's `interface` and `trait` keywords into a single `abstract` keyword, executed in four sequential phases. Each phase has a clear gate (everything passes) before the next begins.

**Phase 1 — Pure Rename:** Mechanical rename of `interface` → `abstract` throughout the entire codebase. Zero semantic change — abstract types behave identically to interfaces. All compiler internals, tests, samples, book, and syntax highlighting are migrated.

**Phase 2 — Purge Traits:** Complete removal of traits, structural typing, and the `with` block mechanism. Nothing new is added — this is purely deletion.

**Phase 3 — Default Implementations:** Add the ability for abstract type members to have method bodies (default implementations) with Rust-style semantics.

**Phase 4 — Late Binding via With Blocks:** Re-add `with` block support targeting abstract types instead of traits, with `as` mapping syntax for retroactive conformance.

**C# Interop Exception:** Throughout all phases, the C# source code the compiler itself is written in may declare C# interfaces (that's fine). The ambient type provider continues to handle CLR interfaces. The C# emitter continues to emit `interface` in the generated code. Mist thinks in terms of "abstract types" but the interop layer bridges to CLR interfaces.

## Glossary

- **Abstract_Type**: A type declared with the `abstract` keyword that defines a contract of slots and optional default implementations. Emits as a C# interface.
- **Slot**: A member declaration without a body in an abstract type. Concrete types implementing the abstract type must provide an implementation for every slot. Property slots cannot have default implementations.
- **Default_Implementation**: A method declaration with a body in an abstract type. Concrete types may provide their own implementation, but are not required to — the default is used if they don't. Only methods can have defaults; properties cannot.
- **Compiler**: The Mist compiler pipeline including lexing, parsing, semantic analysis, and C# transpilation.
- **Parser**: The Mist parser subsystem responsible for converting token streams into syntax tree nodes.
- **Emitter**: The C# transpiler backend that generates C# source code from the Mist semantic model.
- **With_Block**: A top-level declaration using `with Type : AbstractType { mappings }` syntax that establishes retroactive conformance between an existing type and an abstract type via explicit member mapping. Removed in Phase 2 and re-added in Phase 4.
- **Nominal_Typing**: A type system where conformance is established by explicit declaration (`: AbstractType`) rather than structural member matching.
- **Ambient_Type_Provider**: The subsystem that exposes CLR types (from referenced assemblies) as Mist types, mapping C# virtual/abstract members to fillable slots.
- **Implementer**: A concrete class or struct that declares conformance to an abstract type via its base clause.
- **Type_Checker**: The analysis pass that verifies all slots are filled by implementers and validates type compatibility.
- **Slot_Filling**: The act of providing a concrete implementation for a slot, either via a method/property on the implementing type or via a `with` block mapping. Slot filling applies regardless of whether the slot has a default — filling always takes precedence over any default.
- **Structural_Typing**: The type system mechanism (from traits) where conformance is established by matching public members rather than explicit declaration. Removed in Phase 2.

## Requirements

---

## Phase 1 — Pure Rename (interface → abstract)

*This phase is a mechanical rename/refactor of `interface` to `abstract` throughout the entire codebase. Abstract types behave IDENTICALLY to interfaces at this stage — zero semantic change.*

---

### Requirement 1: Abstract Type Declaration Parsing (Phase 1)

**User Story:** As a Mist developer, I want to declare abstract types using the `abstract` keyword, so that I have a clear contract mechanism for defining required slots.

#### Acceptance Criteria

1. WHEN the Parser encounters the `abstract` keyword followed by a type name and a braced body, THE Parser SHALL produce an abstract type declaration syntax node.
2. WHEN an abstract type body contains a member signature without a body (no braces, terminated by semicolon), THE Parser SHALL classify that member as a slot.
3. THE Parser SHALL support method signatures and property signatures (`Type Name { get; }`, `Type Name { get; set; }`) as slot declarations within abstract type bodies. Field signatures are NOT supported — only properties.
4. WHEN an abstract type declaration includes generic type parameters (e.g., `abstract IContainer<T>`), THE Parser SHALL parse them identically to how generic parameters are parsed on class and struct declarations.
5. WHEN an abstract type declaration includes generic constraints (e.g., `where T : class`), THE Parser SHALL parse them identically to how they are parsed on class and struct declarations.
6. WHEN an abstract type declaration includes a base clause with other abstract types (e.g., `abstract IChild : IParent`), THE Parser SHALL parse it as abstract type inheritance.
7. WHEN an abstract type declaration includes associated type members (e.g., `type Output : IStringable;`), THE Parser SHALL parse them identically to how they are parsed in current interface declarations.
8. THE Parser SHALL allow an empty abstract type body (`abstract IMarker { }`) for marker type patterns.
9. THE `interface` keyword SHALL be removed from valid Mist syntax. WHEN the Parser encounters the `interface` keyword, THE Compiler SHALL emit an error diagnostic with the message: "The 'interface' keyword has been removed. Use 'abstract' instead."

### Requirement 2: Compiler Internals Rename (Phase 1)

**User Story:** As a compiler maintainer, I want all internal types, methods, and file names to reflect the `abstract` terminology, so that the codebase is consistent with the language surface.

#### Acceptance Criteria

1. THE Grammar SHALL define an `ABSTRACT` token (in `keywords.storm`) and replace the `InterfaceDeclaration` rule with an `AbstractDeclaration` rule (in `Mist.storm`).
2. THE semantic model SHALL introduce an `AbstractType` class (replacing `InterfaceType`) with `TypeKind.Abstract`.
3. THE `SemanticModelBuilder` SHALL rename all interface-related registration and resolution passes: `RegisterInterfaceDeclaration` → `RegisterAbstractDeclaration`, `ResolveInterfaceDeclaration` → `ResolveAbstractDeclaration`, `Pass1_ValidateInterfaceSatisfaction` → `Pass1_ValidateAbstractSatisfaction`, `Pass1_DetectInterfaceCycles` → `Pass1_DetectAbstractCycles`.
4. THE `CSharpEmitter` SHALL rename `EmitInterfaceDeclaration` → `EmitAbstractDeclaration`. The emitted C# code continues to produce `interface` declarations (unchanged output).
5. All C# source files whose names reference "Interface" in the context of Mist interface types SHALL be renamed to use "Abstract" (e.g., `InterfaceType.cs` → `AbstractType.cs`).
6. All test files and test classes referencing Mist "interface" concepts SHALL be renamed to use "abstract" terminology.

#### Comprehensive Inventory of Rename Targets

**Grammar files:**
- `Source/Mist/Grammar/keywords.storm`: `INTERFACE` token → `ABSTRACT` token
- `Source/Mist/Grammar/Mist.storm`: `InterfaceDeclaration` rule → `AbstractDeclaration` rule; `InterfaceType` variant in `TypeDeclaration` → `AbstractType`; `InterfacePropertyDeclaration` and `InterfaceMemberDeclaration` rules renamed

**Semantic model (Source/Mist/Semantics/):**
- `InterfaceType.cs` → `AbstractType.cs` (class `InterfaceType` → `AbstractType`)
- `ConstructedInterfaceType` → `ConstructedAbstractType`
- `TypeKind.Interface` → `TypeKind.Abstract`
- `SymbolKind.Interface` → `SymbolKind.Abstract`

**SemanticModelBuilder passes:**
- `RegisterInterfaceDeclaration` → `RegisterAbstractDeclaration`
- `ResolveInterfaceDeclaration` → `ResolveAbstractDeclaration`
- `Pass1_ValidateInterfaceSatisfaction` → `Pass1_ValidateAbstractSatisfaction`
- `Pass1_DetectInterfaceCycles` → `Pass1_DetectAbstractCycles`
- `Pass1_ResolveInterfaceParents` (if exists) → `Pass1_ResolveAbstractParents`

**Emitter (Source/Mist/Backends/CSharpTranspiler/):**
- `EmitInterfaceDeclaration` → `EmitAbstractDeclaration`

**Tests (Test/Mist.Tests/):**
- `InterfaceSatisfactionPropertyTests.cs` → `AbstractSatisfactionPropertyTests.cs`
- `InterfaceSignatureMismatchPropertyTests.cs` → `AbstractSignatureMismatchPropertyTests.cs`
- `InterfaceRoundTripPropertyTests.cs` → `AbstractRoundTripPropertyTests.cs`
- All test class names, method names, and comments referencing "interface" (in the context of Mist interfaces) renamed to "abstract"
- `TraitInterfaceCoexistencePropertyTests.cs` → repurposed or removed (coexistence no longer applies)

**NOTE:** C# source code declaring actual C# interfaces (e.g., `IType`, `IBackend`, `IAnalyzer`) is NOT renamed. Only references to the Mist language concept of "interface" are renamed.

### Requirement 3: Sample Migration (Phase 1)

**User Story:** As a Mist developer, I want all existing samples that use the `interface` keyword migrated to `abstract`, so that the sample code reflects the current language syntax.

#### Acceptance Criteria

1. THE following samples SHALL have `interface` keyword replaced with `abstract` (pure keyword swap, no semantic change):
   - `samples/Interfaces/` (rename directory to `samples/AbstractTypes/` — this becomes the main sample after all phases)
   - `samples/ShapeInterfaces/` (rename directory to `samples/ShapeAbstracts/`)
   - `samples/GenericTypes/`
   - `samples/VoidType/`
   - `samples/Properties/`
   - `samples/Enums/`
2. THE following samples that use BOTH `interface` and `trait` SHALL have `interface` → `abstract` in Phase 1 (trait changes deferred to Phase 2):
   - `samples/GenericConstraints/`
   - `samples/AssociatedTypes/`
   - `samples/DependentTypes/`
   - `samples/NestedScoping/`
   - `samples/StructuredContext/`
3. `samples/AllFeatures/Program.mist` SHALL be updated to reflect any renamed sample entry points.
4. All migrated samples SHALL compile and run producing identical output to their pre-migration versions.

### Requirement 4: TextMate Grammar Update — Phase 1 (Phase 1)

**User Story:** As a Mist developer using VS Code, I want `abstract` declarations to be syntax-highlighted correctly, so that I get the same visual cues as other type declarations.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `abstract` as a declaration keyword (scope `keyword.declaration.mist`).
2. THE TextMate grammar SHALL highlight the identifier following `abstract` as a type name (scope `entity.name.type.abstract.mist`).
3. THE TextMate grammar SHALL remove `interface` from the declaration keyword list since the keyword is removed from the language.
4. THE generic declaration pattern (`class|struct|interface|trait` + name + `<`) SHALL be updated to `class|struct|abstract|trait` + name + `<`.
5. THE book's syntax highlighting files (`book/theme/highlight.js` and `book/theme/mist-lang.js`) SHALL replace `interface` with `abstract` in the Mist keyword list.

### Requirement 5: Book Documentation — Phase 1 (Phase 1)

**User Story:** As a Mist developer, I want the language book to rename the "Interfaces" chapter to "Abstract Types", so that the documentation reflects the current language syntax.

#### Acceptance Criteria

1. THE book SHALL rename `book/src/object-model/interfaces.md` to `book/src/object-model/abstract-types.md`.
2. THE chapter title SHALL be "Abstract Types" and all content SHALL use `abstract` syntax instead of `interface`.
3. THE book's `SUMMARY.md` SHALL reference "Abstract Types" in place of "Interfaces" in the Object Model section.
4. All code examples within the chapter SHALL be updated to use `abstract` keyword.
5. All code snippets in `book/snippets/` that demonstrate interface usage SHALL be updated to use `abstract`.

### Requirement 6: Phase 1 Verification Gate (Phase 1)

**User Story:** As a compiler maintainer, I want a verification step confirming that "interface" only remains in permitted locations, so that the rename is complete.

#### Acceptance Criteria

1. A grep of the entire codebase for the string "interface" (case-insensitive) SHALL confirm it ONLY appears in:
   - C# source code the compiler itself is written in (C# `interface` declarations like `interface IType`)
   - The ambient type provider's handling of CLR interfaces (loading interface metadata from assemblies)
   - C# emission output (the emitter generates `interface` in the C# code it produces)
   - Third-party/generated code (NuGet packages, `obj/`, `bin/`)
   - The spec files themselves (`.kiro/specs/`)
2. THE string "interface" SHALL NOT appear in: Mist source files (`.mist`), Storm grammar files (`.storm`), TextMate grammar JSON, book markdown, book highlighting JS (in the Mist keyword section), test names/comments referring to the Mist concept.
3. All tests SHALL pass with zero failures.
4. All samples SHALL compile and produce their expected output.

---

## Phase 2 — Purge Traits

*This phase deletes traits in their entirety: grammar, lexing, parsing, semantic model, type checker logic, all tests, book content, samples. The `with` block mechanism is ALSO removed in this phase. Nothing new is added — this is purely deletion.*

---

### Requirement 7: Trait Keyword and Structural Typing Removal (Phase 2)

**User Story:** As a Mist developer, I want the `trait` keyword, structural typing, and all associated mechanisms removed from the language, so that there is a single clear contract system using nominal typing.

#### Acceptance Criteria

1. THE `TRAIT` token SHALL be removed from `keywords.storm`.
2. THE `TraitDeclaration` rule SHALL be removed from `Mist.storm`.
3. THE `TraitType` variant SHALL be removed from the `TypeDeclaration` rule.
4. WHEN the Parser encounters the `trait` keyword in source code, THE Compiler SHALL emit an error diagnostic with the message: "The 'trait' keyword has been removed. Use 'abstract' with nominal typing (': AbstractType') or a 'with' block for retroactive conformance."
5. THE semantic model SHALL remove: `TraitType`, `ConstructedTraitType`, `TraitSatisfactionMap`, and all structural matching logic (`TryStructuralMatch`, `TryStructuralMatchWithSubstitution`, `TryMatchTraitField`, `TryMatchTraitMethod`, `TryMatchTraitProperty`).
6. THE `SemanticModelBuilder` SHALL remove: `RegisterTraitDeclaration`, `ResolveTraitDeclaration`, `Pass1_ResolveTraitParents`, `Pass1_DetectTraitCycles`, `Pass1_ResolveTraitSatisfaction`.
7. THE `CSharpEmitter` SHALL remove `EmitTraitDeclaration` and all trait-related emission logic.
8. THE Type_Checker SHALL require explicit nominal declaration (`: AbstractType`) for a type to implement an abstract type. Structural matching SHALL NOT establish conformance.

### Requirement 8: With Block Removal (Phase 2)

**User Story:** As a Mist developer, I understand that `with` blocks are temporarily removed as part of the trait purge, and will be re-added in Phase 4 targeting abstract types.

#### Acceptance Criteria

1. THE `WITH` token, `AS` token, `WithDeclaration` rule, and `MemberMapping` rule SHALL be removed from the grammar.
2. THE `SemanticModelBuilder` SHALL remove `Pass1_ResolveWithBlocks` and all with-block registration logic.
3. THE `WithBlockRegistry` (or equivalent) SHALL be removed from the semantic model.
4. THE `CSharpEmitter` SHALL remove `EmitExplicitTraitImplementations` and all with-block emission logic.
5. WHEN the Parser encounters the `with` keyword in a with-block position, THE Compiler SHALL emit an error diagnostic. (The `with` keyword may still be valid in other positions such as `with` clauses on methods — only the top-level `with Type : Trait { }` form is removed.)

### Requirement 9: Trait-Related Test and Sample Removal (Phase 2)

**User Story:** As a compiler maintainer, I want all trait-related tests and samples removed, so that the test suite reflects the current language capabilities.

#### Acceptance Criteria

1. THE `samples/Traits/` directory SHALL be removed entirely (it exists solely to demonstrate structural typing).
2. THE following test files SHALL be removed entirely:
   - `TraitAssignabilityPropertyTests.cs`
   - `TraitMemberAccessPropertyTests.cs`
   - `TraitMemberAccessGenericPropertyTests.cs`
   - `TraitStructuralMatchSubstitutionPropertyTests.cs`
   - `TraitInheritanceValidationPropertyTests.cs`
   - `TraitInterfaceCoexistencePropertyTests.cs`
   - `WithBlockGenericTraitPropertyTests.cs`
   - Any other test files whose primary purpose is testing trait behavior
3. THE following samples SHALL be updated to remove trait usage and use only nominal conformance (`: AbstractType`):
   - `samples/GenericConstraints/` — replace `trait` with `abstract`, add `: AbstractType` to conforming types
   - `samples/AssociatedTypes/` — replace `trait` with `abstract`, add `: AbstractType` conformance
   - `samples/DependentTypes/` — replace `trait` with `abstract`, add `: AbstractType` conformance
   - `samples/NestedScoping/` — replace `trait` with `abstract`
   - `samples/StructuredContext/` — replace `trait` with `abstract`, add `: AbstractType` conformance
4. `samples/GenericTraits/` SHALL be migrated: `trait` → `abstract`, types using structural satisfaction SHALL have `: AbstractType` added to their class declarations. The directory SHALL be renamed to reflect the updated content (e.g., `samples/GenericAbstracts/`).
5. `samples/AllFeatures/Program.mist` SHALL be updated to remove the `Traits.Program.RunDemo()` call and reflect any renamed sample entry points.

### Requirement 10: TextMate Grammar Update — Phase 2 (Phase 2)

**User Story:** As a Mist developer using VS Code, I want the `trait` keyword removed from syntax highlighting since it is no longer valid Mist syntax.

#### Acceptance Criteria

1. THE TextMate grammar SHALL remove `trait` from the declaration keyword list.
2. THE generic declaration pattern SHALL be updated from `class|struct|abstract|trait` to `class|struct|abstract`.
3. THE `trait`-specific name highlighting rule (scope `entity.name.type.trait.mist`) SHALL be removed.
4. THE book's syntax highlighting files (`book/theme/mist-lang.js`) SHALL remove `trait` from the Mist keyword list.

### Requirement 11: Book Documentation — Phase 2 (Phase 2)

**User Story:** As a Mist developer, I want the "Traits" chapter removed from the book since the feature no longer exists.

#### Acceptance Criteria

1. THE book SHALL remove `book/src/object-model/traits.md` (or replace it with a brief migration note directing users to use `abstract` with nominal typing).
2. THE book's `SUMMARY.md` SHALL remove the "Traits" entry.
3. Any cross-references to traits from other chapters SHALL be updated or removed.

### Requirement 12: Phase 2 Verification Gate (Phase 2)

**User Story:** As a compiler maintainer, I want a verification step confirming that "trait" appears nowhere except spec files, so that the purge is complete.

#### Acceptance Criteria

1. A grep of the entire codebase for the string "trait" (case-insensitive) SHALL confirm it ONLY appears in:
   - The spec files themselves (`.kiro/specs/`)
   - Third-party/generated code (NuGet packages, `obj/`, `bin/`)
   - Comments in C# source referencing the old system for historical context (if any remain, they should be minimal)
2. THE string "trait" SHALL NOT appear in: Mist source files (`.mist`), Storm grammar files (`.storm`), TextMate grammar JSON, book markdown, book highlighting JS, test file names, test class names, or any active production code paths.
3. All tests SHALL pass with zero failures.
4. All samples SHALL compile and produce their expected output.

---

## Phase 3 — Default Implementations

*This phase adds the ability for abstract type members to have method bodies (default implementations). Rust-style semantics: the default IS the implementation regardless of reference type (no lens behavior).*

---

### Requirement 13: Default Implementation Parsing (Phase 3)

**User Story:** As a Mist developer, I want to provide default method implementations in abstract types, so that implementers don't have to repeat common behavior.

#### Acceptance Criteria

1. WHEN an abstract type body contains a method declaration with a braced body, THE Parser SHALL classify that method as a default implementation.
2. THE Parser SHALL distinguish between slots (semicolon-terminated) and defaults (body-present) using the presence or absence of a method body.
3. WHEN a property signature appears with a body (attempting to provide a default for a property), THE Parser SHALL emit an error diagnostic: "Property slots cannot have default implementations. Only methods can have defaults."
4. THE Grammar SHALL support the `AbstractDeclaration` rule having members that are either `AbstractMethodDeclaration` (slot — no body) or `MethodDeclaration` (default — with body).
5. THE Parser SHALL allow abstract types containing any mix of slots and defaults in any order.

### Requirement 14: Slot Satisfaction with Defaults (Phase 3)

**User Story:** As a Mist developer, I want the compiler to accept my concrete type even if it doesn't implement methods that have defaults, so that I only need to fill required slots.

#### Acceptance Criteria

1. WHEN a concrete type declares `: AbstractType` and provides public method/property implementations matching every slot signature (members without defaults), THE Type_Checker SHALL accept the type as a valid implementer.
2. WHEN a concrete type declares `: AbstractType` and is missing one or more slot implementations (members without defaults), THE Type_Checker SHALL emit an error diagnostic listing each unfilled slot by name and signature.
3. WHEN a concrete type does NOT provide an implementation for a method that has a default implementation, THE Type_Checker SHALL accept the type as valid (the default fills the method slot).
4. WHEN a concrete type provides its own implementation for a member that has a default implementation in the abstract type AND the signatures match exactly, THE Type_Checker SHALL accept the concrete type's implementation and use it instead of the default (slot filling takes precedence over defaults). IF the signatures do not match exactly, THE Type_Checker SHALL reject the implementation with a signature mismatch error.
5. WHEN matching slot signatures, THE Type_Checker SHALL verify name, return type, and parameter types all match exactly.
6. WHEN an abstract type inherits from another abstract type that has default methods, THE Type_Checker SHALL propagate those defaults — implementers of the child abstract type are not required to fill them either.
7. WHEN a parent abstract type declares a SLOT and a child abstract type inheriting from it declares a member of the same name and signature WITH a default implementation, THE Type_Checker SHALL treat the child's member as HIDING the parent's slot rather than filling it, and SHALL still require implementers of the child to fill the parent's slot. This is the converse of 14.6 and does not follow from it: 14.6 propagates a default declared on the PARENT down to implementers of the child, whereas a default declared on the CHILD does not reach back up to answer the parent's slot. The rule matches C#, where a derived interface's member hides rather than implements a base interface member of the same signature.

### Requirement 15: Default Implementation Semantics — Rust-style (Phase 3)

**User Story:** As a Mist developer, I want default implementations to behave consistently regardless of how I reference a value, so that there is no "lens" behavior.

#### Acceptance Criteria

1. WHEN a concrete type does not provide its own implementation for a default method, THE method SHALL be callable on the concrete type directly (not only through the abstract type reference).
2. WHEN a concrete type does not provide its own implementation for a default method, calling that method through the concrete type reference and through the abstract type reference SHALL produce identical behavior.
3. WHEN a concrete type provides its own implementation for a member with a default, THE concrete type's implementation SHALL be used regardless of whether the value is accessed through the concrete type or the abstract type reference.
4. WHEN a default implementation body references `this`, THE Compiler SHALL resolve member access on `this` against the abstract type's full member set (its own slots/defaults plus all inherited members from parent abstract types).
5. WHEN a default implementation calls another default method on `this`, THE Compiler SHALL resolve the call against the full abstract type hierarchy and THE Emitter SHALL emit the call correctly through the forwarding mechanism.
6. Only methods can have default implementations. Property slots MUST always be filled by the implementer.

### Requirement 16: Default Implementation C# Emission (Phase 3)

**User Story:** As a Mist developer, I want default implementations to emit as C# default interface methods and to be restated on each conformer, so that Rust-style dispatch semantics are preserved in the generated code.

#### Acceptance Criteria

1. THE Emitter SHALL emit default implementations as C# default interface methods (DIM) in the generated interface.
2. WHEN a concrete type does NOT override a default implementation, THE Emitter SHALL RESTATE the default's body as a public method on the concrete type, emitting the same body the DIM carries.
3. THE restated method SHALL ensure the default is accessible on the concrete type without casting to the interface type.
4. WHEN a concrete type provides its own implementation for a method with a default, THE Emitter SHALL emit only the concrete type's implementation (no restatement, no DIM invocation).
5. THE Emitter SHALL NOT emit a forwarding stub that invokes the DIM through an explicit interface cast. Such a stub does not terminate: the public method on the concrete type is the implicit implementation of the interface member, so `((IGreeting)this).Greet()` dispatches back into the method it sits in. Compiled and run as written, `public string Greet() => ((IGreeting)this).Greet();` dies with a stack overflow.
6. WHEN a default's signature or body names a type parameter of a generic abstract type, THE Emitter SHALL apply that conformer's type arguments to the restated member. IF the type arguments cannot be applied, THE Emitter SHALL NOT restate the member, rather than emit a member naming an unbound type parameter.

> **Amendment note.** Criteria 2 and 5 originally prescribed a forwarding stub using an explicit
> interface cast, spelled `public string Greet() => ((IGreeting)this).Greet();`. That construct was
> measured to recurse infinitely (stack overflow at ~261k frames), so it was never implementable and
> neither pipeline ever implemented it — both restate the body. Criterion 5 now forbids what it used
> to require, and 6 records the generic case the original criteria did not consider.

### Requirement 17: Book Documentation — Phase 3 (Phase 3)

**User Story:** As a Mist developer, I want the Abstract Types chapter to document default implementations with compiled examples.

#### Acceptance Criteria

1. THE "Abstract Types" chapter SHALL be updated with a section covering: default implementation syntax, Rust-style semantics (no lens behavior), the restriction that only methods can have defaults (not properties), overriding defaults, and interaction with abstract type inheritance.
2. THE section SHALL include compiled snippet examples demonstrating: an abstract type with a default method, a concrete type relying on the default, a concrete type overriding the default, and calling the method through both concrete and abstract references.
3. All code examples SHALL be compiled snippets following the Book Snippet Policy.

### Requirement 18: Phase 3 Verification Gate (Phase 3)

**User Story:** As a compiler maintainer, I want confirmation that default implementations work correctly before proceeding to Phase 4.

#### Acceptance Criteria

1. All tests SHALL pass with zero failures.
2. All samples SHALL compile and produce their expected output.
3. The new default implementation samples SHALL demonstrate at least: a default method taken as-is, a default method overridden, and calling through both reference types producing identical results.

---

## Phase 4 — Late Binding via With Blocks

*This phase re-adds `with` block support, now targeting abstract types instead of traits. `with Type : AbstractType { mappings }` syntax with the `as` mapping keyword.*

---

### Requirement 19: With Block Declaration Parsing (Phase 4)

**User Story:** As a Mist developer, I want to use `with` blocks to adapt existing types to abstract types without modifying the original type, so that I can achieve retroactive conformance.

#### Acceptance Criteria

1. THE Grammar SHALL re-add the `WITH` token, `AS` token, `WithDeclaration` rule, and `MemberMapping` rule.
2. THE `WithDeclaration` rule SHALL use the syntax: `with TypeName : AbstractType { mappings }`.
3. WHEN a `with` block maps a source member to a slot using the `as` keyword (e.g., `GetLabel as Name;`), THE Parser SHALL produce a member mapping node.
4. THE Parser SHALL support multiple mappings within a single `with` block.
5. A `with` block SHALL be a top-level declaration (not nested inside a type body).

### Requirement 20: With Block Conformance Establishment (Phase 4)

**User Story:** As a Mist developer, I want `with` blocks to establish nominal conformance between an existing type and an abstract type, so that the type becomes assignable to abstract-typed variables.

#### Acceptance Criteria

1. WHEN a `with` block declares `with TypeName : AbstractType { mappings }`, THE Compiler SHALL establish nominal conformance between that type and the abstract type.
2. WHEN a type has conformance established via a `with` block, THE type SHALL be assignable to variables and parameters typed as the abstract type.
3. WHEN a `with` block references an abstract type that has default implementations, THE Compiler SHALL only require mappings for slots without defaults. Default methods do not require mappings.
4. WHEN a `with` block maps a member to a slot that has a default implementation, THE mapping SHALL fill the slot — the mapped member is used instead of the default.
5. IF a `with` block references an undefined type or abstract type, THEN THE Compiler SHALL emit an error diagnostic identifying the undefined reference.
6. IF a `with` block maps a member whose type signature is incompatible with the slot's declared signature, THEN THE Compiler SHALL emit a type mismatch error diagnostic. THE Compiler SHALL check signature compatibility at the point of mapping declaration, not deferred to usage.
7. WHEN a `with` block establishes conformance for a type that already declares `: AbstractType` in its base clause, THE Compiler SHALL emit an error diagnostic indicating redundant conformance.
8. A type MAY have multiple `with` blocks establishing conformance to different abstract types. Each `with` block is independent.
9. WHEN a `with` block references a type that originates from a CLR assembly (an ambient/imported type not defined in Mist source), THE Compiler SHALL emit an error diagnostic indicating that `with` blocks cannot target CLR types because the emitter cannot modify the type's inheritance list. THE error message SHALL be: "Cannot use 'with' to establish conformance for CLR type '{TypeName}'. Only types defined in Mist source can be adapted via 'with' blocks."

### Requirement 21: With Block C# Emission (Phase 4)

**User Story:** As a Mist developer, I want `with` block mappings to emit as explicit interface implementations, so that the generated C# code correctly delegates to the mapped members.

#### Acceptance Criteria

1. WHEN a `with` block maps `SourceMember as TargetSlot`, THE Emitter SHALL generate an explicit interface implementation method that delegates to the source member (e.g., `string INamed.Name() => this.GetLabel();`).
2. THE Emitter SHALL include the abstract type's interface name in the concrete type's C# inheritance list when a `with` block establishes conformance. THE interface MUST appear in the inheritance list whenever explicit interface implementations are generated.
3. WHEN a `with` block omits a mapping for a default method, THE Emitter SHALL generate a forwarding method for that default (same as direct implementation without override).
4. WHEN a `with` block maps a member to a slot that has a default, THE mapped member's explicit implementation SHALL take precedence over the default.

### Requirement 22: Book Documentation — Phase 4 (Phase 4)

**User Story:** As a Mist developer, I want the Abstract Types chapter to document `with` block late binding with compiled examples.

#### Acceptance Criteria

1. THE "Abstract Types" chapter SHALL be updated with a section covering: `with` block syntax, retroactive conformance, `as` mapping keyword, interaction with defaults (only required slots need mappings), and multiple `with` blocks on the same type.
2. THE section SHALL include compiled snippet examples demonstrating: a basic `with` block, a `with` block with `as` mapping, a `with` block that relies on defaults for unmapped slots, and polymorphic usage after `with` block conformance.
3. All code examples SHALL be compiled snippets following the Book Snippet Policy.

### Requirement 23: Phase 4 Verification Gate (Phase 4)

**User Story:** As a compiler maintainer, I want confirmation that `with` blocks work correctly before the spec is considered complete.

#### Acceptance Criteria

1. All tests SHALL pass with zero failures.
2. All samples SHALL compile and produce their expected output.
3. The with-block samples SHALL demonstrate at least: basic `with` block conformance, `as` mapping, polymorphic usage through with-block conformance, and interaction with default methods.

---

## Cross-Phase Requirements

*These requirements apply across all phases or represent capabilities that exist before Phase 1 and must remain working throughout.*

---

### Requirement 24: C# Code Emission for Abstract Types (All Phases)

**User Story:** As a Mist developer, I want abstract types to emit as C# interfaces, so that the generated code interoperates with the .NET ecosystem.

#### Acceptance Criteria

1. THE Emitter SHALL emit each abstract type declaration as a C# `interface` declaration.
2. WHEN an abstract type contains only slots (no defaults), THE Emitter SHALL emit a standard C# interface with method/property signatures only.
3. WHEN an abstract type contains default implementations (Phase 3+), THE Emitter SHALL emit a C# interface with default interface method bodies for each default.
4. WHEN a concrete type implements an abstract type, THE Emitter SHALL include the abstract type's interface name in the concrete type's C# inheritance list.
5. WHEN a slot is a property signature in the abstract type (e.g., `string Name { get; }`), THE Emitter SHALL emit it as a property signature on the C# interface.
6. WHEN an abstract type contains associated type declarations, THE Emitter SHALL emit them using the same mechanism currently used for interface associated types.

### Requirement 25: CLR Interop via Ambient Type Provider (All Phases)

**User Story:** As a Mist developer, I want C# abstract and virtual members from referenced assemblies to appear as fillable slots in Mist, so that I can use framework extension patterns naturally.

#### Acceptance Criteria

1. WHEN the Ambient_Type_Provider encounters a C# interface in a referenced assembly, THE Ambient_Type_Provider SHALL expose it as an abstract type in Mist with all interface members as slots.
2. WHEN the Ambient_Type_Provider encounters a C# class with `abstract` methods, THE Ambient_Type_Provider SHALL expose those abstract methods as required slots on the Mist type representation.
3. WHEN the Ambient_Type_Provider encounters a C# class with `virtual` methods that have implementations, THE Ambient_Type_Provider SHALL expose those virtual methods as slots with default implementations (fillable but not required).
4. WHEN a Mist type fills a slot that originated from a C# `abstract` or `virtual` method on a base class, THE Emitter SHALL generate the C# `override` keyword on the emitted method.
5. WHEN a Mist type does NOT fill a slot that originated from a C# `virtual` method, THE Emitter SHALL NOT emit any method, keyword, or metadata for that slot (the base class's implementation is inherited normally through standard C# inheritance semantics).
6. THE distinction between CLR interop override (this requirement) and Mist concrete hiding (Requirement 26) SHALL be: `override` is emitted ONLY for filling slots that originate from CLR `abstract` or `virtual` members on base classes. Mist-to-Mist concrete class inheritance always uses hiding semantics with `new`.

### Requirement 26: Concrete Type Hiding Semantics (All Phases)

**User Story:** As a Mist developer, I want concrete type inheritance to continue using hiding semantics (not override), so that base type behavior is preserved when viewed through the base type lens.

#### Acceptance Criteria

1. WHEN a derived concrete type declares a method with the same name and signature as a base concrete type method in Mist-to-Mist concrete class inheritance, THE Compiler SHALL treat this as member hiding — the base method is still invoked when the value is accessed through the base type.
2. WHEN a derived concrete type hides a base member intentionally in Mist-to-Mist inheritance, THE Compiler SHALL require the `new` keyword on the hiding member. WHEN the `new` keyword is present, THE Compiler SHALL NOT emit a hiding warning for that member.
3. IF a derived concrete type declares a member matching a base concrete type member without the `new` keyword in Mist-to-Mist inheritance, THEN THE Compiler SHALL emit a warning diagnostic: "'{TypeName}.{MemberName}' hides inherited member '{BaseTypeName}.{MemberName}'. Use 'new' if intentional."
4. WHEN the `new` keyword is present on a member that does NOT hide a base member, THE Compiler SHALL emit a warning diagnostic indicating `new` is unnecessary.
5. Hiding semantics apply ONLY to Mist-to-Mist concrete class inheritance. They do NOT apply to filling abstract type slots (which is slot-filling, not hiding) or to filling CLR virtual/abstract slots (which emits `override`).

#### Proof Code

```mist
using Standard;

namespace HidingDemo;

public class Base
{
    public string Speak()
    {
        return "base";
    }
}

public class Derived : Base
{
    // PROOF: new keyword required for intentional hiding
    new public string Speak()
    {
        return "derived";
    }
}

public class Program
{
    private static void AcceptBase(Base b)
    {
        // PROOF: base behavior preserved when viewed through base type
        Console.WriteLine($"  via Base: {b.Speak()}");
    }

    public static void RunDemo()
    {
        var d = new Derived();

        // PROOF: derived type sees its own method
        Console.WriteLine($"via Derived: {d.Speak()}");

        // PROOF: same object, but viewed as Base, calls Base.Speak()
        AcceptBase(d);
    }

    public static void Main(string[] args)
    {
        RunDemo();
    }
}
```

**Expected output:**
```
via Derived: derived
  via Base: base
```

### Requirement 27: Polymorphic Usage of Abstract Types (All Phases)

**User Story:** As a Mist developer, I want to use abstract types as variable types, parameter types, and return types, so that I can write polymorphic code.

#### Acceptance Criteria

1. WHEN a variable is declared with an abstract type (e.g., `IDescribable item`), THE Type_Checker SHALL accept assignment of any concrete type that declares `: IDescribable` or has conformance established via a `with` block (Phase 4+).
2. WHEN a method parameter is typed as an abstract type, THE Type_Checker SHALL accept any argument whose type implements that abstract type.
3. WHEN member access is performed on an abstract-typed variable, THE Compiler SHALL resolve the access against the abstract type's declared members (both slots and defaults).
4. WHEN an abstract type inherits from another abstract type, THE Type_Checker SHALL unconditionally accept assignment of an implementer of the child abstract type to a variable typed as the parent abstract type (transitive assignability). This is safe because the child's implementer must fill all parent slots through the child relationship.

### Requirement 28: Slot Satisfaction Checking (All Phases)

**User Story:** As a Mist developer, I want the compiler to verify that my concrete types fill all required slots, so that I get compile-time errors for missing implementations.

#### Acceptance Criteria

1. WHEN a concrete type declares `: AbstractType` and provides public method/property implementations matching every slot signature, THE Type_Checker SHALL accept the type as a valid implementer.
2. WHEN a concrete type declares `: AbstractType` and is missing one or more slot implementations, THE Type_Checker SHALL emit an error diagnostic listing each unfilled slot by name and signature AND SHALL halt compilation (this is a hard error, not a warning).
3. WHEN an abstract type inherits from another abstract type (e.g., `abstract IChild : IParent`), THE Type_Checker SHALL require implementers of `IChild` to fill slots from both `IChild` and `IParent`.
4. WHEN matching slot signatures, THE Type_Checker SHALL verify name, return type, and parameter types all match exactly.
5. WHEN an abstract type inherits from multiple parent abstract types that declare a slot with the same name and signature (diamond inheritance), THE Type_Checker SHALL require a single implementation from the implementer that satisfies both parent slots.

### Requirement 29: Sample Application (All Phases — Final State)

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating abstract types, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/AbstractTypes/Program.mist`.
2. The sample SHALL demonstrate: abstract type declaration with slots only, abstract type with default implementations, property slots, concrete types filling all slots, concrete types relying on defaults, abstract type inheritance, polymorphic variable assignment, polymorphic parameter passing, late binding via `with` blocks, a concrete type overriding a default implementation, and a `with` block filling a slot that has a default.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `AbstractTypes.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

#### Sample Build-Up Plan

The sample is built incrementally across phases:

- **After Phase 1:** The sample exists with basic abstract type usage (slots only, slot filling, polymorphic usage, abstract inheritance). This is the migrated `samples/Interfaces/` content using `abstract` syntax.
- **After Phase 3:** The sample gains default implementation demonstrations (default taken as-is, default overridden, dispatch through both reference types).
- **After Phase 4:** The sample gains `with` block demonstrations (retroactive conformance, `as` mapping, with-block overriding a default). This is the FINAL state shown in the proof code below.

#### Proof Code (Final State — After Phase 4)

```mist
using Standard;

namespace AbstractTypes;

// PROOF: abstract keyword parses as a type declaration
// PROOF: members without bodies are slots (must be filled)
public abstract IDescribable
{
    string Describe();
}

// PROOF: abstract type inheritance works
public abstract IFullyDescribable : IDescribable
{
    string DetailedDescribe();
}

// PROOF: property slots on abstract types
public abstract ILabeled
{
    string Label { get; }
}

// PROOF: abstract type with default implementation
public abstract IGreeting
{
    string Name();

    // PROOF: default implementation has a body
    string Greet()
    {
        return $"Hello, I am {this.Name()}";
    }
}

// PROOF: concrete type fills required slots including property slot
public class Person : IDescribable, IGreeting, ILabeled
{
    private string name;
    private int age;

    public Person(string name, int age)
    {
        this.name = name;
        this.age = age;
    }

    public string Describe()
    {
        return $"Person: {this.name}, age {this.age}";
    }

    // PROOF: fills the Name() slot required by IGreeting
    public string Name()
    {
        return this.name;
    }

    // PROOF: fills the property slot from ILabeled
    public string Label { get; } = "person";

    // PROOF: does NOT provide Greet() — takes the default
}

// PROOF: concrete type that overrides a default
public class Robot : IFullyDescribable, IGreeting
{
    private string model;

    public Robot(string model)
    {
        this.model = model;
    }

    public string Describe()
    {
        return $"Robot: {this.model}";
    }

    public string DetailedDescribe()
    {
        return $"Robot model {this.model}, fully operational";
    }

    public string Name()
    {
        return this.model;
    }

    // PROOF: overrides the default Greet() with custom implementation
    public string Greet()
    {
        return $"Beep boop, I am {this.model}";
    }
}

// PROOF: late binding via with block (retroactive conformance)
public class Logger
{
    public string GetLabel()
    {
        return "SystemLogger";
    }
}

public abstract INamed
{
    string Name();
}

with Logger : INamed
{
    GetLabel as Name;
}

// PROOF: with block filling a slot that has a default
public abstract ITaggable
{
    string Tag();

    string DisplayTag()
    {
        return $"[{this.Tag()}]";
    }
}

public class TaggedWidget
{
    public string GetWidgetTag()
    {
        return "widget-tag";
    }

    public string GetWidgetDisplay()
    {
        return "<Widget>";
    }
}

with TaggedWidget : ITaggable
{
    GetWidgetTag as Tag;
    GetWidgetDisplay as DisplayTag;
}

public class Program
{
    // PROOF: abstract type as parameter type (polymorphism)
    private static void PrintDescription(IDescribable item)
    {
        Console.WriteLine($"  {item.Describe()}");
    }

    public static void RunDemo()
    {
        Console.WriteLine("=== Abstract type with slots only ===");
        var person = new Person("Alice", 30);
        Console.WriteLine(person.Describe());

        Console.WriteLine("=== Property slot filled ===");
        // PROOF: property slot accessible on concrete type
        Console.WriteLine($"  Label: {person.Label}");

        Console.WriteLine("=== Default implementation taken (Rust-style) ===");
        // PROOF: Greet() is callable directly on Person (no cast needed)
        Console.WriteLine(person.Greet());

        Console.WriteLine("=== Default implementation overridden ===");
        var robot = new Robot("T-800");
        Console.WriteLine(robot.Greet());

        Console.WriteLine("=== Polymorphic variable assignment ===");
        IDescribable describable = person;
        Console.WriteLine($"  {describable.Describe()}");

        Console.WriteLine("=== Transitive interface assignability ===");
        IDescribable robotAsDescribable = robot;
        Console.WriteLine($"  {robotAsDescribable.Describe()}");

        Console.WriteLine("=== Polymorphic parameter passing ===");
        PrintDescription(person);
        PrintDescription(robot);

        Console.WriteLine("=== Default via abstract-typed reference ===");
        // PROOF: same result whether called on concrete type or abstract reference
        IGreeting greetingRef = person;
        Console.WriteLine(greetingRef.Greet());

        Console.WriteLine("=== Late binding via with block ===");
        var logger = new Logger();
        INamed named = logger;
        Console.WriteLine($"  {named.Name()}");

        Console.WriteLine("=== With block overrides default ===");
        // PROOF: with block maps GetWidgetDisplay to DisplayTag slot,
        // overriding the default implementation
        var widget = new TaggedWidget();
        ITaggable taggable = widget;
        Console.WriteLine($"  Tag: {taggable.Tag()}");
        Console.WriteLine($"  DisplayTag: {taggable.DisplayTag()}");

        Console.WriteLine("Done!");
    }

    public static void Main(string[] args)
    {
        RunDemo();
    }
}
```

**Expected output:**
```
=== Abstract type with slots only ===
Person: Alice, age 30
=== Property slot filled ===
  Label: person
=== Default implementation taken (Rust-style) ===
Hello, I am Alice
=== Default implementation overridden ===
Beep boop, I am T-800
=== Polymorphic variable assignment ===
  Person: Alice, age 30
=== Transitive interface assignability ===
  Robot: T-800
=== Polymorphic parameter passing ===
  Person: Alice, age 30
  Robot: T-800
=== Default via abstract-typed reference ===
Hello, I am Alice
=== Late binding via with block ===
  SystemLogger
=== With block overrides default ===
  Tag: widget-tag
  DisplayTag: <Widget>
Done!
```

### Requirement 30: Final Verification (After All Phases)

**User Story:** As a compiler maintainer, I want a comprehensive final verification confirming the entire unification is complete and no remnants of the old system remain.

#### Acceptance Criteria

1. A grep of the entire codebase for "interface" (case-insensitive) SHALL confirm it ONLY exists in:
   - C# source code the compiler itself is written in (C# `interface` declarations)
   - The ambient type provider's handling of CLR interfaces
   - C# emission output (the emitter generates `interface` in generated C# code)
   - Third-party/generated code (NuGet packages, `obj/`, `bin/`)
   - Spec files (`.kiro/specs/`)
2. A grep of the entire codebase for "trait" (case-insensitive) SHALL confirm it exists NOWHERE except:
   - Spec files (`.kiro/specs/`)
   - Third-party/generated code
3. All tests SHALL pass with zero failures.
4. All samples SHALL compile and produce their expected output.
5. The `samples/AbstractTypes/Program.mist` proof code SHALL compile and produce the exact expected output listed in Requirement 29.
6. The `samples/HidingDemo/Program.mist` (or equivalent) proof code SHALL compile and produce the exact expected output listed in Requirement 26.

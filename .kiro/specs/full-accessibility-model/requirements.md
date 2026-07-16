# Requirements Document

## Introduction

This feature implements a C#-style accessibility model in Mist (the four levels `public`, `private`, `protected`, and `internal`, plus the `sealed` type modifier) and enforces it in the classic compiler pipeline. It also represents the same accessibility and `sealed` information in the experimental pipeline (the Program Model) without building enforcement or emission there.

Today Mist models accessibility as a small set of booleans:

- In the classic pipeline, a private `MemberModifiers(bool IsPublic, bool IsPrivate, bool IsStatic)` record struct inside the Type Checker carries only `public`/`private`, and defaults to `private` when no modifier is present.
- In the experimental pipeline, `ProgramModel/Model/MemberModifiers.cs` carries `IsPublic`/`IsPrivate` booleans (and `PropertyAccessor.IsPrivate`).
- `ClassType.IsSealed` exists in the classic semantics layer, but is set only for types projected from CLR assemblies; user-declared Mist types cannot currently be declared `sealed`.
- Only the `public` and `private` keywords are recognized as accessibility modifiers; `protected`, `internal`, and `sealed` are not yet parsed as Mist declaration modifiers.

This feature replaces the per-keyword booleans in each pipeline with a single `Accessibility` enum (one enum definition per pipeline, placed idiomatically in that pipeline, with no third shared location), adds `protected`/`internal`/`sealed` support, applies C# default accessibility in every declaration position, treats each whole compilation as one implicit assembly, and enforces the accessibility rule set in the classic Type Checker. Mist supports four accessibility levels — `public`, `private`, `protected`, `internal` — plus `sealed`. It deliberately OMITS C#'s two combined levels, `protected internal` and `private protected`, so Mist's accessibility model is a subset of C#'s rather than a full mirror. This is an intentional relaxation of strict C# parity: those two levels cross inheritance with assembly membership in ways Mist cannot meaningfully express. Interoperability with C# is preserved without loss — when consuming C#/CLR types, `protected internal` collapses to `protected` and `private protected` collapses to `private`, a mapping that never broadens access. Mist structs are inheritable (unlike C#'s value-type structs), so `sealed` and `protected` are fully supported and enforced on structs exactly as on classes. The only intentional "allow-but-warn" deviation in this feature is a `protected` member declared inside a `sealed` type: because a sealed type cannot be inherited, the protected accessibility is unnecessary, so the declaration compiles but produces a warning-severity diagnostic (mirroring C# CS0628).

All new diagnostics conform to the existing `DiagnosticDescriptor` registry shape. A related in-flight spec, `.kiro/specs/diagnostic-descriptor-system/`, is migrating diagnostics to that registry; the design phase will investigate the actual registry implementation and that spec to note any dependency or ordering relationship. For requirements purposes, new diagnostics must conform to the established registry shape as-is.

Existing-violation cleanup is in scope: once classic enforcement is enabled, the entire existing Mist codebase (compiler sources, samples, snippets, and test `.mist` fixtures) must build clean under the new rules, respecting the workspace zero-warnings policy.

## Out of Scope (Non-Goals)

The following are explicitly excluded from this feature:

- **Experimental enforcement analyzer**: The experimental pipeline SHALL NOT gain an accessibility/`sealed` enforcement analyzer in this feature.
- **Experimental emitter changes**: The experimental pipeline emitter SHALL NOT be changed to emit accessibility/`sealed` in this feature.
- **`sealed` scope**: The `sealed` modifier applies to classes and structs only; it is not valid on methods, other non-type members, or type aliases.

## Glossary

- **Compilation**: A single invocation of the Mist compiler over a set of source files that produces one output. In practice this corresponds to one `.mistproject`, but the operative unit is the compilation itself.
- **Implicit_Assembly**: The one logical assembly formed by a single Compilation. "Same assembly" is defined as "same Compilation".
- **External_Assembly**: Any assembly that is not the current Compilation, including referenced CLR/C# assemblies and separately-compiled Mist outputs.
- **Classic_Pipeline**: The existing, fully-featured compiler pipeline spanning the Lexer, Parser, Semantic_Model, Type_Checker, and CSharp_Backend.
- **Experimental_Pipeline**: The newer pipeline built around the Program_Model.
- **Lexer**: The classic-pipeline component that converts source text into tokens.
- **Parser**: The classic-pipeline component that converts tokens into an abstract syntax tree, including modifier lists.
- **Semantic_Model**: The classic-pipeline semantic representation of a Mist program (the `Mist.Semantics` layer).
- **Type_Checker**: The classic-pipeline analyzer that verifies semantic rules and produces diagnostics (`TypeCheckerAnalyzer`).
- **CSharp_Backend**: The classic-pipeline C# transpiler backend that emits C# source.
- **Program_Model**: The experimental-pipeline semantic representation of a Mist program (the `Mist.ProgramModel` layer).
- **Ambient_Type_Provider**: The Program_Model component that projects imported/CLR/Mist types (and their accessibility and `sealed` information) into the Program_Model.
- **Accessibility**: An enumeration with exactly the values `Public`, `Private`, `Protected`, and `Internal`. Mist intentionally omits C#'s combined `protected internal` and `private protected` levels. One `Accessibility` enum is defined per pipeline.
- **Sealed_Flag**: A boolean flag on a type declaration indicating the `sealed` modifier, modeled separately from the Accessibility enum.
- **Accessibility_Domain**: The region of program text within which a given declaration may be referenced, as defined by the C# language specification for the equivalent construct.
- **Accessibility_Consistency_Rule**: The C# rule that a member or type SHALL NOT be more accessible than any type it exposes in its signature (return types, parameter types, field types, property types, base types, implemented interfaces, and generic constraints). Commonly stated as the "less accessible than" or "inconsistent accessibility" rule.
- **Default_Accessibility**: The Accessibility value assigned to a declaration that has no explicit access modifier. It is determined by the kind of the enclosing scope (namespace/compilation -> Internal; class/struct -> Private; interface/enum -> Public) and is resolved to a concrete value during Semantic_Model and Program_Model construction; the Type_Checker consumes the resolved value and never computes a default.
- **Diagnostic_Registry**: The existing centralized diagnostic infrastructure in which each diagnostic is a `DiagnosticDescriptor` (fields: `Id`, `DefaultSeverity`, `Stage`, `MessageTemplate`) declared as a static field in a descriptor registry class under `Common/Diagnostics/Descriptors/`.
- **Allow_But_Warn**: A construct that Mist compiles successfully while emitting a warning-severity diagnostic. The single such case in this feature is a `protected` member declared inside a `sealed` type; because a sealed type has no derived types the protected accessibility is unnecessary. This mirrors C#, which also warns here (CS0628) rather than erroring.
- **Analog_Mapping**: A documented decision that determines the behavior of a Mist construct with no one-to-one C# equivalent by mirroring the closest C# construct and applying that construct's accessibility rules.

## Requirements

### Requirement 1: Accessibility and Sealed Keyword Lexing

**User Story:** As a Mist developer, I want the compiler to recognize all C# accessibility keywords and the `sealed` keyword, so that I can annotate declarations with the full accessibility model.

#### Acceptance Criteria

1. WHEN the Lexer encounters the keyword `public`, THE Lexer SHALL produce a `public` keyword token.
2. WHEN the Lexer encounters the keyword `private`, THE Lexer SHALL produce a `private` keyword token.
3. WHEN the Lexer encounters the keyword `protected`, THE Lexer SHALL produce a `protected` keyword token.
4. WHEN the Lexer encounters the keyword `internal`, THE Lexer SHALL produce an `internal` keyword token.
5. WHEN the Lexer encounters the keyword `sealed`, THE Lexer SHALL produce a `sealed` keyword token.

### Requirement 2: Parsing Modifier Combinations into an Accessibility Value and a Sealed Flag

**User Story:** As a Mist developer, I want the parser to translate access-modifier keyword combinations into a single accessibility value, so that every declaration carries one unambiguous accessibility.

#### Acceptance Criteria

1. WHEN the Parser parses a declaration whose only access modifier is `public`, THE Parser SHALL record the Accessibility value `Public`.
2. WHEN the Parser parses a declaration whose only access modifier is `private`, THE Parser SHALL record the Accessibility value `Private`.
3. WHEN the Parser parses a declaration whose only access modifier is `protected`, THE Parser SHALL record the Accessibility value `Protected`.
4. WHEN the Parser parses a declaration whose only access modifier is `internal`, THE Parser SHALL record the Accessibility value `Internal`.
5. WHEN the Parser parses a declaration bearing the `sealed` modifier, THE Parser SHALL record the Sealed_Flag as set, independently of the recorded Accessibility value.
6. IF a declaration bears more than one access modifier, THEN THE Parser SHALL report an error, because Mist provides four standalone access levels (`public`, `private`, `protected`, `internal`) and does not support combined levels such as `protected internal` or `private protected`. (The `sealed` modifier is orthogonal and may accompany an access modifier.)

### Requirement 3: Accessibility Representation in the Classic Semantic Model

**User Story:** As a compiler maintainer, I want the classic pipeline to model accessibility as one enum rather than per-keyword booleans, so that accessibility is represented consistently and Mist's four accessibility levels are expressible.

#### Acceptance Criteria

1. THE Semantic_Model SHALL represent the declared accessibility of every applicable declaration using a single Accessibility enum with the values `Public`, `Private`, `Protected`, and `Internal`.
2. THE Semantic_Model SHALL model the `sealed` modifier as a Sealed_Flag on type declarations, separate from the Accessibility enum.
3. THE Classic_Pipeline SHALL replace the existing classic accessibility booleans with the classic Accessibility enum, and SHALL migrate every existing usage of those booleans to the enum.
4. THE Classic_Pipeline SHALL define the classic Accessibility enum within the classic semantics layer.
5. THE Classic_Pipeline SHALL NOT introduce a third or cross-pipeline shared accessibility type.

### Requirement 4: Accessibility Representation in the Experimental Program Model

**User Story:** As a compiler maintainer, I want the experimental pipeline to model accessibility as one enum rather than per-keyword booleans, so that both pipelines represent the same information without sharing a type.

#### Acceptance Criteria

1. THE Program_Model SHALL represent the declared accessibility of every applicable declaration using a single Accessibility enum with the values `Public`, `Private`, `Protected`, and `Internal`.
2. THE Program_Model SHALL model the `sealed` modifier as a Sealed_Flag on type declarations, separate from the Accessibility enum.
3. THE Experimental_Pipeline SHALL replace the existing Program_Model accessibility booleans with the experimental Accessibility enum, and SHALL migrate every existing usage of those booleans to the enum.
4. THE Experimental_Pipeline SHALL define the experimental Accessibility enum within the Program_Model.
5. THE Experimental_Pipeline SHALL define the experimental Accessibility enum as a separate definition from the classic Accessibility enum, and SHALL NOT introduce a third or cross-pipeline shared accessibility type.

### Requirement 5: Default Accessibility Resolution (Scope-Based, Realized in the Models)

**User Story:** As a compiler maintainer, I want default accessibility to be determined by enclosing-scope kind and resolved into the Semantic Model and Program Model up front, so that Mist matches C# defaults and the visibility checker only ever reads concrete modifiers instead of computing defaults.

#### Acceptance Criteria

1. THE Classic_Pipeline SHALL determine the Default_Accessibility of a declaration that has no explicit access modifier from the kind of its immediately enclosing scope: a namespace or compilation (global) scope SHALL default to `Internal`; a class or struct scope SHALL default to `Private`; and an interface or enum scope SHALL default to `Public`. A nested type SHALL take the default of its enclosing scope's kind.
2. WHERE a top-level type declaration has no access modifier, THE Classic_Pipeline SHALL assign the Default_Accessibility `Internal`.
3. WHERE a class member or struct member has no access modifier, THE Classic_Pipeline SHALL assign the Default_Accessibility `Private`, applying this default to struct members exactly as C# does.
4. WHERE a nested type declaration has no access modifier, THE Classic_Pipeline SHALL assign the Default_Accessibility `Private`.
5. WHERE an interface member has no access modifier, THE Classic_Pipeline SHALL assign the Default_Accessibility `Public`.
6. WHERE any other declaration position has no access modifier, THE Classic_Pipeline SHALL assign the Default_Accessibility that C# assigns to the equivalent declaration position.
7. WHERE a declaration has no access modifier, THE Experimental_Pipeline SHALL record the same Default_Accessibility that the Classic_Pipeline assigns for the equivalent declaration position.
8. WHERE a declaration has no explicit access modifier, THE Semantic_Model SHALL resolve its Default_Accessibility to a concrete Accessibility value during model construction and store that value on the declaration.
9. WHERE a declaration has no explicit access modifier, THE Program_Model SHALL resolve its Default_Accessibility to a concrete Accessibility value during model construction and store that value on the declaration.
10. WHEN model construction is complete, THE Semantic_Model and THE Program_Model SHALL each carry an explicit, concrete Accessibility value on every applicable declaration, with no declaration retaining an unspecified or default-pending accessibility state.
11. THE Type_Checker SHALL evaluate accessibility using the resolved Accessibility value stored on each declaration and SHALL NOT compute, infer, or apply default accessibility during enforcement.

**Note:** Criterion 3 intentionally overrides Mist's current behavior. The Type_Checker today defaults struct members without an explicit modifier to `public` (documented in the source as "C++ struct semantics where value types expose their fields by default"). Under this feature, struct members default to `private`, matching C# exactly. Existing struct members that rely on the current public default are therefore in scope for the existing-violation cleanup (see Requirement 18). Applying this flip is mandatory and takes precedence over minimizing cleanup effort: where the flip and the minimal-touch objective conflict, correctness wins (see Requirement 18).

### Requirement 6: Compilation-as-Assembly Boundary Semantics

**User Story:** As a Mist developer, I want `internal` accessibility to be scoped to the current compilation, so that internal members are shared within my project but hidden from other assemblies.

#### Acceptance Criteria

1. THE Classic_Pipeline SHALL treat each whole Compilation as one Implicit_Assembly.
2. WHEN the Type_Checker evaluates whether two declarations are in the "same assembly", THE Type_Checker SHALL treat "same assembly" as "same Compilation".
3. WHEN an `internal` member is referenced from elsewhere within the same Compilation, THE Type_Checker SHALL permit the reference.
4. IF an `internal` member is referenced from an External_Assembly, THEN THE Type_Checker SHALL report an accessibility-violation error.
5. THE Type_Checker SHALL derive the assembly boundary from the Compilation unit rather than from any other grouping.

**Note:** The standard library interacts with this boundary the same way the struct-member flip interacts with Requirement 5. Because a top-level type with no access modifier defaults to `Internal` (Requirement 5) and `internal` is scoped to the declaring compilation (this requirement), any standard-library type or member left unspecified would become invisible to a consuming compilation that references the standard library as an External_Assembly. The standard library's types and members are intended to be public by design, so unspecified standard-library declarations are upgraded to explicit `public` to preserve that surface. This is an accepted, intentional widening for correctness (see Requirement 18).

### Requirement 7: Member-Access Enforcement

**User Story:** As a Mist developer, I want the type checker to reject accesses that violate a member's accessibility, so that encapsulation is enforced the same way as in C#.

#### Acceptance Criteria

1. IF a `Private` member is referenced from outside the Accessibility_Domain of the member's declaring type, THEN THE Type_Checker SHALL report an accessibility-violation error.
2. IF a `Protected` member is referenced from a location that is neither the declaring type nor a type derived from the declaring type, THEN THE Type_Checker SHALL report an accessibility-violation error, matching the C# `protected` access rules.
3. IF an `Internal` member is referenced from an External_Assembly, THEN THE Type_Checker SHALL report an accessibility-violation error.
4. WHEN a member is referenced from within the member's Accessibility_Domain, THE Type_Checker SHALL permit the reference without reporting an accessibility diagnostic.

### Requirement 8: Accessibility-Consistency ("Less Accessible Than") Enforcement

**User Story:** As a Mist developer, I want the type checker to reject signatures that expose types less accessible than the declaration itself, so that a public API cannot leak inaccessible types.

#### Acceptance Criteria

1. IF a method's return type is less accessible than the method, THEN THE Type_Checker SHALL report an inconsistent-accessibility error.
2. IF a parameter type of a method, constructor, indexer, or operator is less accessible than the declaring member, THEN THE Type_Checker SHALL report an inconsistent-accessibility error.
3. IF a field's type is less accessible than the field, THEN THE Type_Checker SHALL report an inconsistent-accessibility error.
4. IF a property's or indexer's type is less accessible than the property or indexer, THEN THE Type_Checker SHALL report an inconsistent-accessibility error.
5. IF a generic constraint type on a type or member is less accessible than the constrained type or member, THEN THE Type_Checker SHALL report an inconsistent-accessibility error.
6. WHEN every type exposed in a declaration's signature is at least as accessible as the declaration, THE Type_Checker SHALL permit the declaration without reporting an inconsistent-accessibility diagnostic.

### Requirement 9: Base-Type and Implemented-Interface Accessibility Enforcement

**User Story:** As a Mist developer, I want base types and implemented interfaces to be at least as accessible as the deriving type, so that a type cannot expose an inaccessible base in its public contract.

#### Acceptance Criteria

1. IF the base class of a derived class is less accessible than the derived class, THEN THE Type_Checker SHALL report an inconsistent-accessibility error.
2. IF an interface implemented by a type is less accessible than the implementing type, THEN THE Type_Checker SHALL report an inconsistent-accessibility error.
3. IF a base interface of an interface is less accessible than the derived interface, THEN THE Type_Checker SHALL report an inconsistent-accessibility error.
4. WHEN a base type and all implemented interfaces are at least as accessible as the declaring type, THE Type_Checker SHALL permit the declaration without reporting an inconsistent-accessibility diagnostic.

### Requirement 10: Override and Interface-Implementation Accessibility Enforcement

**User Story:** As a Mist developer, I want overrides and interface implementations to obey C# accessibility rules, so that member accessibility remains consistent across inheritance and interface contracts.

#### Acceptance Criteria

1. IF an overriding member declares an accessibility that differs from the overridden member in a way that C# rejects, THEN THE Type_Checker SHALL report an accessibility-violation error.
2. IF a member that implements an interface member is not `Public` where C# requires public accessibility for that implementation, THEN THE Type_Checker SHALL report an accessibility-violation error.
3. WHEN an overriding member and an interface-implementing member satisfy the C# accessibility rules for their position, THE Type_Checker SHALL permit the declaration without reporting an accessibility diagnostic.

### Requirement 11: Nested-Type Accessibility and Accessibility Domain

**User Story:** As a Mist developer, I want nested types to participate in the accessibility model, so that a nested type's declared accessibility and its enclosing type's accessibility both constrain where it can be used.

#### Acceptance Criteria

1. THE Type_Checker SHALL compute the Accessibility_Domain of a nested type from both the nested type's declared Accessibility and the Accessibility_Domain of its enclosing type, matching C#.
2. IF a nested type is referenced from outside its computed Accessibility_Domain, THEN THE Type_Checker SHALL report an accessibility-violation error.
3. WHEN a member of a type is referenced from within a nested type of the same declaring type, THE Type_Checker SHALL treat the reference as inside the declaring type's Accessibility_Domain, matching C#.

### Requirement 12: Sealed Type Behavior and Inheritance Enforcement

**User Story:** As a Mist developer, I want to mark a class or struct `sealed` to prevent inheritance, so that I can express non-extensible types exactly as in C#.

#### Acceptance Criteria

1. WHEN a class or struct is declared with the `sealed` modifier, THE Type_Checker SHALL accept the declaration without reporting a diagnostic.
2. IF a type declares a sealed class or a sealed struct as its base type, THEN THE Type_Checker SHALL report a sealed-inheritance error.
3. WHEN the base type of a derived type is not sealed, THE Type_Checker SHALL permit the inheritance without reporting a sealed-inheritance diagnostic.
4. THE Type_Checker SHALL apply sealed-inheritance enforcement to both user-declared sealed Mist types and sealed types projected from External_Assemblies.
5. WHEN a type alias targets a sealed type, THE Type_Checker SHALL accept the alias without a sealed-related diagnostic, because a type alias is transparent and introduces no new type or inheritance relationship.
6. WHEN a refinement type is declared over a sealed type, or over a type alias of a sealed type, THE Type_Checker SHALL accept the refinement without a sealed-related diagnostic, because a refinement narrows the values of its base type and does not extend the sealed type.
7. IF a newtype is declared wrapping a sealed type, THEN THE Type_Checker SHALL report an error, because a newtype builds a new nominal type on the wrapped type and is disallowed against a sealed type.
8. WHERE a type alias declaration carries an access modifier, THE Type_Checker SHALL model and enforce the alias's own Accessibility using the Accessibility enum, exactly as for other type declarations; all four accessibility forms are valid on a type alias, and when no access modifier is present the alias receives its Default_Accessibility from the scope-kind rule (Requirement 5).
9. IF a type alias declaration bears the `sealed` modifier, THEN THE Type_Checker SHALL report an error, because `sealed` is meaningful only on classes and structs and a type alias introduces no inheritable type.

### Requirement 13: Protected Members in Sealed Types

**User Story:** As a Mist developer, I want `protected` accessibility to be fully enforced on my inheritable types, and I want a warning (not an error) when I place a `protected` member in a sealed type, so that Mist matches C# behavior including its CS0628 warning.

#### Acceptance Criteria

1. WHERE a `Protected` member is declared on any non-sealed (inheritable) class or struct, THE Type_Checker SHALL fully support and enforce that accessibility without reporting a warning.
2. WHERE a `Protected` member is declared inside a `sealed` type (a sealed class or a sealed struct), THE Type_Checker SHALL compile the declaration AND SHALL report a warning-severity diagnostic stating that the protected accessibility is unnecessary because a sealed type cannot be inherited, mirroring C# CS0628.
3. THE Classic_Pipeline SHALL NOT support the `sealed` modifier on methods or non-type members within this feature.

### Requirement 14: Diagnostics via the Existing Descriptor Registry

**User Story:** As a compiler maintainer, I want every new diagnostic to use the existing descriptor registry, so that diagnostics remain centralized and consistent with the established convention.

#### Acceptance Criteria

1. THE Classic_Pipeline SHALL define every new accessibility-violation error as a `DiagnosticDescriptor` entry in a descriptor registry class, conforming to the existing registry shape, fields, ID scheme, categories, and conventions.
2. THE Classic_Pipeline SHALL define every new Allow_But_Warn warning as a `DiagnosticDescriptor` entry in a descriptor registry class, with `DefaultSeverity` set to warning severity.
3. THE Classic_Pipeline SHALL NOT modify, extend, redesign, or refactor the Diagnostic_Registry or the `DiagnosticDescriptor` structure, and SHALL only add new descriptor entries conforming to the established shape.

### Requirement 15: Explicit C# Emission of Accessibility and Sealed

**User Story:** As a Mist developer, I want the C# backend to emit explicit accessibility and `sealed` keywords, so that the generated C# is unambiguous and always valid.

#### Acceptance Criteria

1. WHEN the CSharp_Backend emits any declaration, THE CSharp_Backend SHALL emit an explicit C# accessibility keyword sequence corresponding to the declaration's Accessibility value.
2. WHEN the CSharp_Backend emits a declaration whose effective Accessibility is `Private`, THE CSharp_Backend SHALL emit the explicit `private` keyword rather than relying on the C# implicit default.
3. WHEN the CSharp_Backend emits a class whose Sealed_Flag is set, THE CSharp_Backend SHALL emit the `sealed` keyword on the emitted class.
4. WHEN the CSharp_Backend emits an inheritable or sealed struct, or a struct member whose Accessibility is `protected`, THE CSharp_Backend SHALL emit valid, compilable C# for that struct and its members. Because Mist structs are inheritable and can be sealed and can carry protected members — none of which map onto C#'s value-type `struct` — the exact C# lowering is a design-phase decision that this requirement does not prescribe; only the observable guarantee (valid, compilable C#) is fixed here.
5. THE CSharp_Backend SHALL emit accessibility and `sealed` such that the generated C# compiles without C# accessibility errors for every input that the Classic_Pipeline accepts.

### Requirement 16: Experimental Pipeline Representation-Only Scope

**User Story:** As a compiler maintainer, I want the experimental pipeline to carry accessibility and `sealed` information without enforcing or emitting it, so that the data is available for future work without changing current behavior.

#### Acceptance Criteria

1. WHEN the Program_Model is built for a declaration, THE Program_Model SHALL populate the declaration's Accessibility value and Sealed_Flag.
2. WHEN the Ambient_Type_Provider projects an imported, CLR, or Mist type, THE Ambient_Type_Provider SHALL populate the projected type's and members' Accessibility values and Sealed_Flag.
3. THE Experimental_Pipeline SHALL NOT implement an accessibility or `sealed` enforcement analyzer within this feature.
4. THE Experimental_Pipeline SHALL NOT implement accessibility or `sealed` emitter changes within this feature.
5. WHEN the Ambient_Type_Provider projects a CLR type, THE Ambient_Type_Provider SHALL set that type's Sealed_Flag from the CLR sealed state (treating a CLR static class — a type that is both abstract and sealed — as neither sealed nor abstract, per existing behavior), rather than representing sealed only through the sentinel-attribute mechanism.
6. WHEN the Ambient_Type_Provider projects a CLR type or member, THE Ambient_Type_Provider SHALL map CLR accessibility onto Mist's four-level Accessibility enum without ever broadening access: CLR public -> `Public`; CLR family (protected) -> `Protected`; CLR family-or-assembly (protected internal) -> `Protected`; CLR private -> `Private`; and CLR assembly (internal) and CLR family-and-assembly (private protected) -> `Private` (a consuming compilation cannot reach either, and they SHALL NOT be projected as Mist `Internal`, which would incorrectly widen them into the consumer's compilation). The nested-type CLR accessibility forms SHALL map by the same rule.

### Requirement 17: Analog-Mapping Decision Record

**User Story:** As a compiler maintainer, I want every accessibility decision for Mist constructs without a direct C# equivalent to be documented, so that the reasoning behind each behavior is captured and reviewable.

#### Acceptance Criteria

1. WHERE a Mist construct does not map one-to-one to a C# construct, THE feature documentation SHALL record the construct, its closest C# analog, the resulting accessibility behavior, and the rationale.
2. THE feature documentation SHALL record the Analog_Mapping decision for each accessibility rule whose application to a Mist construct requires choosing a closest C# analog.
3. THE feature documentation SHALL record the following decisions reflecting the corrected model: (a) Mist structs are inheritable, an intentional divergence from C#'s value-type structs; (b) the `sealed` modifier prevents inheritance for both classes and structs; (c) protected members are fully enforced on the members of inheritable structs; and (d) the sole Allow_But_Warn case is a `protected` member declared inside a `sealed` type, mirroring C# CS0628.
4. THE feature documentation SHALL additionally record the following decisions: (e) The Ambient_Type_Provider's projection of CLR types: CLR accessibility maps to the Accessibility enum as public->Public, family->Protected, family-or-assembly->Protected, and assembly/family-and-assembly/private->Private (never Mist Internal, which would broaden); and the first-class Sealed_Flag is populated from the CLR sealed state, reconciled with the pre-existing sentinel-attribute (`Mist.Sentinel.Sealed`) representation used by the emitter. (f) Sealed interactions with Mist-specific constructs: a type alias of a sealed type is allowed (transparent), a refinement type over a sealed type (or over an alias of one) is allowed (narrows values, does not extend), and a newtype wrapping a sealed type is an error (it builds a new nominal type on the sealed type); additionally, a type alias declaration may itself carry any of the four accessibility modifiers (its own Accessibility is modeled and enforced), but `sealed` is not a valid modifier on a type alias. (g) Mist deliberately omits C#'s `protected internal` and `private protected` levels, making its accessibility model a subset of C#'s and relaxing strict C# parity. Rationale: these levels combine inheritance with assembly membership in ways Mist cannot meaningfully express; C# code is still consumed losslessly by collapsing `protected internal` to `Protected` and `private protected` to `Private`, which never broadens access.

### Requirement 18: Existing-Violation Cleanup with Minimal Touch and Least Privilege

**User Story:** As a compiler maintainer, I want the existing codebase to build clean under the new rules, so that enabling enforcement does not break the build or violate the zero-warnings policy.

#### Acceptance Criteria

1. THE feature SHALL audit the existing Mist codebase, including compiler Mist sources, samples, snippets, and test `.mist` fixtures, for declarations that the new rules would newly flag as errors or warnings.
2. WHEN the audit identifies a declaration that the new rules would newly flag, THE feature SHALL edit only the declarations that must change to make the codebase build clean under the new rules.
3. WHEN a declaration must change to permit genuine cross-boundary usage, THE feature SHALL widen accessibility only to the narrowest level that permits that usage.
4. THE feature SHALL NOT broaden a declaration's accessibility solely to silence a diagnostic where no genuine cross-boundary usage exists.
5. WHEN the struct-member default changes from `public` to `private` (Requirement 5), THE feature SHALL identify existing struct members that relied on the public default and are referenced across an accessibility boundary, and SHALL add the narrowest explicit accessibility modifier that keeps the existing usage legal (least privilege, minimal touch).
6. WHERE correctly applying the struct-member default flip (Requirement 5) requires touching more declarations than a minimal-touch approach would favor, THE feature SHALL prioritize correctness over effort: it SHALL apply the flip in full and fix every resulting accessibility error and warning, regardless of how many declarations must change. The least-privilege and minimal-touch criteria continue to govern the manner of each individual fix (narrowest sufficient accessibility; no unrelated or gratuitous edits) but SHALL NOT be used to avoid, defer, or only partially apply the flip.
7. WHERE a standard-library type or member has no explicit access modifier and the new default rules would downgrade it below its intended public accessibility, THE feature SHALL upgrade it to explicit `public`, which is its intended accessibility. Because the standard library is public by design, this intended-public upgrade is the correct accessibility level (not a least-privilege violation), and correctness takes precedence over minimizing edits, consistent with the precedence established for the struct-member flip (Requirement 5).
8. WHEN cleanup edits are complete, THE existing Mist codebase SHALL build with zero errors and zero warnings under the new rules.

### Requirement 19: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want the new accessibility and `sealed` keywords to be syntax-highlighted, so that I get the same visual cues as existing modifiers.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `protected` as a storage modifier (scope `storage.modifier.mist`).
2. THE TextMate grammar SHALL recognize `internal` as a storage modifier (scope `storage.modifier.mist`).
3. THE TextMate grammar SHALL recognize `sealed` as a storage modifier (scope `storage.modifier.mist`).

### Requirement 20: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating the full accessibility model and the `sealed` modifier, so that I can see every warning-free usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/FullAccessibilityModel/Program.mist` (with supporting type files and a `mist.toml`) following the existing sample conventions.
2. The sample SHALL demonstrate every warning-free permutation defined in the preceding requirements, including but not limited to: `public`, `private`, `protected`, and `internal` on class members; `public`, `private`, `internal`, and `protected` on struct members; a struct inheriting from another struct, with the derived struct reading an inherited `protected` and an inherited `internal` base member; and a `sealed` struct declaration; a `sealed` class declaration; default (no-modifier) member accessibility resolving to `private`; a derived type accessing `protected` and `internal` base members; and a non-derived type in the same compilation accessing an `internal` member.
3. The sample SHALL NOT include any construct that triggers an Allow_But_Warn diagnostic (no `protected` member declared inside a `sealed` type), so that the sample builds clean under the zero-warnings policy.
4. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
5. `samples/AllFeatures/Program.mist` SHALL call `FullAccessibilityModel.Program.RunDemo()` to include the feature in the full-suite sample run.
6. The sample SHALL compile and run without errors or warnings using the Mist compiler.
7. IF, at any point during implementation, the proof code or its expected output appears incorrect, internally inconsistent, or in conflict with any requirement, THEN the implementer SHALL halt and request user intervention to reconcile the requirements against the proof code. The implementer SHALL NOT contort, weaken, work around, or silently modify the implementation (or the proof code) to force them to agree. The proof code is authoritative for the definition of 'done'; a suspected conflict between the proof code and the requirements is a decision reserved for the user.

#### Proof Code

The following source is locked before implementation and MUST appear verbatim in the sample. It is falsifiable: without the feature it fails to parse (new keywords), and any false-positive enforcement on the valid cross-boundary accesses would fail to compile.

```mist
// samples/FullAccessibilityModel/Shape.mist
using Standard;

namespace FullAccessibilityModel;

// PROOF: `protected` and `internal` parse and are enforced as member accessibility.
public class Shape
{
    // PROOF: protected field is reachable from derived types (positive path for protected access).
    protected string label;

    // PROOF: internal field is reachable from any type in the same compilation.
    internal int sideCount;

    // PROOF: private field is reachable only inside the declaring type.
    private int tag;

    public Shape(string label, int sideCount, int tag)
    {
        this.label = label;
        this.sideCount = sideCount;
        this.tag = tag;
    }

    // PROOF: default (no modifier) member resolves to `private`; used only inside the declaring type.
    int SecretSalt()
    {
        return 3;
    }

    // PROOF: a public method reads the private `tag` and the private-by-default SecretSalt from within the declaring type.
    public int Fingerprint()
    {
        return tag + SecretSalt();
    }
}
```

```mist
// samples/FullAccessibilityModel/Square.mist
using Standard;

namespace FullAccessibilityModel;

// PROOF: a derived class reads an inherited `protected` member and an inherited `internal` member
// (positive path for derived access in the same compilation). It does NOT read the base's `private` member.
public class Square : Shape
{
    public Square() : base("square", 4, 7)
    {
    }

    public string Describe()
    {
        // PROOF: `label` (protected) and `sideCount` (internal) are accessible from the derived type;
        // `tag` is private to Shape and is only reachable via the public Fingerprint() method.
        return $"{label}: {sideCount} sides, fingerprint {Fingerprint()}";
    }
}
```

```mist
// samples/FullAccessibilityModel/Ledger.mist
using Standard;

namespace FullAccessibilityModel;

// PROOF: `sealed` on a class parses and is accepted with no diagnostic.
public sealed class Ledger
{
    // PROOF: `private` field is accessed only inside the declaring type (positive path for private access).
    private int balance;

    // PROOF: `internal` field is readable elsewhere in the same compilation.
    internal string currency;

    public Ledger(string currency, int opening)
    {
        this.currency = currency;
        this.balance = opening;
    }

    public int Deposit(int amount)
    {
        balance = balance + amount;
        return balance;
    }
}
```

```mist
// samples/FullAccessibilityModel/Vector.mist
using Standard;

namespace FullAccessibilityModel;

// PROOF: Mist structs are inheritable, so `protected` and `internal` are real, enforced modifiers on struct members.
public struct Vector
{
    // PROOF: protected struct field is reachable from a derived struct (positive path for protected-on-struct).
    protected int magnitude;

    // PROOF: internal struct field is readable elsewhere in the same compilation.
    internal string name;

    public Vector(string name, int magnitude)
    {
        this.name = name;
        this.magnitude = magnitude;
    }
}
```

```mist
// samples/FullAccessibilityModel/UnitVector.mist
using Standard;

namespace FullAccessibilityModel;

// PROOF: a struct may inherit from another struct in Mist (unlike C#'s value-type structs).
public struct UnitVector : Vector
{
    public UnitVector(string name) : base(name, 1)
    {
    }

    public string Describe()
    {
        // PROOF: `magnitude` (protected) and `name` (internal) are both accessible from the derived struct in the same compilation.
        return $"{name}: magnitude {magnitude}";
    }
}
```

```mist
// samples/FullAccessibilityModel/Frozen.mist
using Standard;

namespace FullAccessibilityModel;

// PROOF: `sealed` is accepted on a struct with no diagnostic; because Mist structs are inheritable, sealing one is meaningful.
public sealed struct Frozen
{
    // PROOF: default (no modifier) struct member resolves to `private`; used only inside the declaring struct.
    int checksum;

    // PROOF: explicit `public` struct member.
    public int value;

    public Frozen(int value)
    {
        this.value = value;
        this.checksum = value * 2;
    }

    // PROOF: the sealed struct has NO protected member, so it does not trigger the sealed-protected warning.
    public int Checksum()
    {
        return checksum;
    }
}
```

```mist
// samples/FullAccessibilityModel/Program.mist
using Standard;

namespace FullAccessibilityModel;

public class Program
{
    // PROOF: reads an `internal` member from a non-derived type in the same compilation.
    private static int ReadSides(Shape shape)
    {
        return shape.sideCount;
    }

    public static void RunDemo()
    {
        Console.WriteLine("=== FullAccessibilityModel Mist Sample ===");

        var square = new Square();
        Console.WriteLine(square.Describe());

        // PROOF: `internal` member reachable from a non-derived type in the same compilation.
        Console.WriteLine($"sides via same-compilation access: {ReadSides(square)}");

        var ledger = new Ledger("USD", 100);
        Console.WriteLine($"balance after deposit: {ledger.Deposit(25)}");

        // PROOF: `internal` field readable from another type in the same compilation.
        Console.WriteLine($"currency (internal): {ledger.currency}");

        // Struct demonstrations (Mist structs are inheritable).
        var unit = new UnitVector("north");
        Console.WriteLine(unit.Describe());

        var frozen = new Frozen(21);
        Console.WriteLine($"frozen: value {frozen.value}, checksum {frozen.Checksum()}");

        // PROOF: `internal` struct member readable from a non-derived type in the same compilation.
        Console.WriteLine($"vector name (internal): {unit.name}");

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
=== FullAccessibilityModel Mist Sample ===
square: 4 sides, fingerprint 10
sides via same-compilation access: 4
balance after deposit: 125
currency (internal): USD
north: magnitude 1
frozen: value 21, checksum 42
vector name (internal): north
Done!
```

### Requirement 21: Book Documentation

**User Story:** As a Mist developer, I want the language book to document the accessibility model and the `sealed` modifier, so that I can learn the syntax and semantics from the official reference.

#### Acceptance Criteria

1. THE book SHALL include a chapter or section covering the accessibility model and the `sealed` modifier with compiled code examples.
2. THE book chapter SHALL demonstrate the four accessibility levels (`public`, `private`, `protected`, `internal`), and note that Mist omits C#'s `protected internal` and `private protected` and collapses them on projection, default accessibility per declaration position, the compilation-as-assembly boundary for `internal`, `sealed` classes and structs, and the single Allow_But_Warn case (a `protected` member declared in a sealed type).
3. All code examples SHALL be compiled snippets following the Book Snippet Policy.
4. THE book's SUMMARY.md SHALL reference the new chapter or section in the appropriate section.

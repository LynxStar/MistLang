# Requirements Document

## Introduction

Mist today has no standard library. Primitive types (`int`, `bool`, `string`, etc.) are hardcoded compiler singletons in `PrimitiveType.cs` with no members, fields, or methods. Operators (`+`, `-`, `*`, `<`, `==`, `&&`, etc.) are hardcoded in `TypeCheckerAnalyzer.cs` against a fixed `NumericTypes` set, and the C# emitter passes operator lexemes straight through so the .NET runtime does the actual work. Types that look like stdlib today — `String`, `Array`, `Console`, `Math`, `Convert` — are **ambient types** injected per-backend by `CSharpAmbientTypeResolver.cs`, not stdlib types.

This feature introduces a **Mist Standard Library** — a real, first-class library of types shipped with the compiler. It has two tiers:

- **Bootstrap tier**: numeric primitives (`Int32`, `Int64`, `UInt32`, `UInt64`, `Int16`, `UInt16`, `Byte`, `SByte`, `Single`, `Double`, `Decimal`), `Boolean`, `Char`, `String`, and `Array<T>`. These types require compiler cooperation to exist at all, because their `+`, comparison, indexing, length, etc., must ultimately bottom out in backend-provided IL or runtime calls — Mist cannot express `Int32.operator +` purely in terms of other Mist code without infinite regress.
- **Pure-Mist tier**: anything expressible in ordinary `.mist` source using only the bootstrap tier and other pure-Mist tier types. Everything in this tier is written as normal Mist code with no special compiler support.

To make the bootstrap tier expressible, Mist also gains **first-class user-definable operators** (`public static Int32 operator +(Int32 a, Int32 b)`). Operator overloads are normal overloadable members; the type checker stops applying hardcoded numeric rules and instead performs overload resolution against the operator members of the operand types. Operators are available to all user types, not just stdlib types.

To make the bootstrap tier terminate, the stdlib also introduces an **intrinsic marker** for methods whose bodies are provided by the backend rather than by Mist source. `Int32.operator +` is declared in Mist and marked intrinsic; the backend recognizes the marker and emits `a + b` in its target language (CLR IL, in the C# backend), avoiding infinite recursion.

Finally, to keep existing Mist programs compiling — programs where `42`, `"hello"`, `true`, and `int[]` Just Work without any `using` — the primitive-type keywords (`int`, `string`, `bool`, etc.) become **reserved aliases** for fully-qualified stdlib type names (`Standard.Int32`, `Standard.String`, `Standard.Boolean`, etc.). Because keyword resolution bypasses normal scoping, `int x = 42;` compiles without any `using` directive, and `var x = 42;` works via ordinary literal typing. Users who want to write the unqualified capitalized forms (`Int32`, `String`) must add an explicit `using Standard;`.

This document specifies requirements only. Distribution/loading strategy is an open question with tradeoffs; Requirement 10 presents the options and defers the choice to design.

## Glossary

- **Standard_Library** (or **Stdlib**): The library of Mist types shipped with the compiler, organized in the `Standard` root namespace.
- **Standard_Namespace**: The namespace `Standard`, which contains the bootstrap tier and the root of the pure-Mist tier.
- **Bootstrap_Tier**: The subset of Standard_Library types that cannot be fully expressed in Mist source without compiler cooperation: numeric primitives, `Boolean`, `Char`, `String`, and `Array<T>`.
- **Pure_Mist_Tier**: The subset of Standard_Library types that is expressed entirely in `.mist` source files using only Bootstrap_Tier types and other Pure_Mist_Tier types, with no intrinsic methods.
- **Stdlib_Source_Root**: The directory within the compiler source tree that holds the `.mist` files comprising the Standard_Library.
- **Primitive_Keyword**: A reserved language keyword that names a primitive type — `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`, `bool`, `char`, `string`. Defined in `Source/Mist/Grammar/keywords.storm`. Because each Primitive_Keyword is a reserved keyword, it can never be redeclared, shadowed, or rebound by user code.
- **Stdlib_Type_Name**: The canonical **fully-qualified** name of a Bootstrap_Tier numeric/text type in the Standard_Namespace, e.g., `Standard.Int32`, `Standard.UInt32`, `Standard.Int64`, `Standard.UInt64`, `Standard.Int16`, `Standard.UInt16`, `Standard.Byte`, `Standard.SByte`, `Standard.Single`, `Standard.Double`, `Standard.Decimal`, `Standard.Boolean`, `Standard.Char`, `Standard.String`. The simple-name form (e.g., `Int32`, `String`) may also appear in user code; when unqualified, it is an ordinary identifier subject to normal name-resolution scoping (it resolves only when `using Standard;` is in scope or when a user type of the same name is in scope), not via the Keyword_Alias_Mapping.
- **Keyword_Alias_Mapping**: The fixed mapping from each Primitive_Keyword to its fully-qualified Stdlib_Type_Name in the Standard_Namespace, e.g., `int → Standard.Int32`, `string → Standard.String`, `bool → Standard.Boolean`. This mapping bypasses normal name-resolution scoping: a Primitive_Keyword always resolves to the named type in the Standard_Namespace, regardless of any other types (including user-declared types) in scope.
- **Operator_Declaration**: A member declaration of the form `public static R operator <op>(T1 a, ...)` in a class or struct body, introducing a user-defined operator.
- **Operator_Symbol**: The symbolic operator of an Operator_Declaration: one of `+`, `-`, `*`, `/`, `%`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `!`, or unary `-`. (Bitwise, shift, and compound-assignment operators are non-goals for v1 — see Requirement 13.)
- **Operator_Overload_Resolution**: The process by which the type checker selects the applicable Operator_Declaration(s) for a given operator expression based on operand types.
- **Intrinsic_Marker**: The `intrinsic` modifier keyword, applied to a method, constructor, or operator declaration to mark its body as backend-provided, or applied to a class or struct declaration to mark the type as a Backend_Backed_Type (see Requirement 5). The `intrinsic` keyword appears in the modifier list alongside other modifiers like `public` and `static`, as in `intrinsic public static Int32 operator +(Int32 a, Int32 b);` or `intrinsic public struct Int32 { ... }`.
- **Intrinsic_Method**: A method, constructor, or operator whose body is replaced by the backend at emit time, based on an Intrinsic_Marker.
- **Type_Checker**: The `TypeCheckerAnalyzer` component in `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`.
- **Semantic_Model_Builder**: The `SemanticModelBuilder` component in `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`.
- **CSharp_Emitter**: The `CSharpEmitter` component in `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`.
- **CSharp_Ambient_Type_Resolver**: The `CSharpAmbientTypeResolver` component. After this feature, it materializes `Console`, `Math`, and `Convert` as Mist-typed ambient surfaces only; it does NOT expose `System.String`, `System.Array`, or any other target-language type as a Mist `IType`. Its internal knowledge of CLR runtime shapes is translated to Mist types at its API boundary per Requirement 9.10.
- **Ambient_Type**: A type injected per-backend by an `IAmbientTypeResolver`, representing an external runtime facility surfaced into Mist (after this feature: `Console`, `Math`, `Convert`). The signatures produced by an `IAmbientTypeResolver` are expressed exclusively in Mist types — Standard_Library types (including Backend_Backed_Types), keyword aliases, user-declared types, or other ambient types — never target-language types.
- **Backend_Backed_Type**: A Standard_Library type whose storage layout and intrinsic operations are provided by the Backend rather than by Mist source. Every Bootstrap_Tier type is a Backend_Backed_Type. A Backend_Backed_Type is identified by a type-level Intrinsic_Marker on its declaration (see Requirement 5). The type-level marker signals that the Backend recognizes the type's identity and will substitute a target-language backing type for it at emission time; it does not auto-promote the type's members to Intrinsic_Methods — per-member Intrinsic_Markers remain required for every intrinsic method, constructor, and operator on the type.
- **Backing_Type_Map**: A per-Backend mapping from each Backend_Backed_Type to its target-language backing type (e.g., in the C# backend: `Standard.String → System.String`, `Standard.Int32 → int`, `Standard.Array<T> → T[]`). The Backing_Type_Map is Backend-private: it is consulted exclusively by the emitter (or emitter-adjacent emission utilities) during code generation, and is NOT exposed to, queried by, or depended on by the semantic model, the Type_Checker, the Semantic_Model_Builder, any `IAmbientTypeResolver` implementation, or any analyzer. No Mist `IType` ever corresponds to a target-language backing type — backing types exist only as emission targets.
- **PrimitiveType_Singleton**: One of the static singleton instances in `Source/Mist/Semantics/Model/Types/PrimitiveType.cs` (e.g., `PrimitiveType.Int`), used today to represent primitive types in the semantic model.
- **Compilation_Unit**: A single Mist source file processed through the compilation pipeline.
- **Backend**: An implementation of `IBackend` (currently only the C# transpiler backend).
- **Entry_Compilation**: A compilation initiated by a user's `mist build` or `mist run` command, as distinct from compilation of the Standard_Library itself.

## Requirements

### Requirement 1: Standard Library Structure and Location

**User Story:** As a compiler developer, I want a clear, fixed place in the repo for stdlib source code, so that stdlib types are discoverable, versionable, and separable from compiler source.

#### Acceptance Criteria

1. THE Standard_Library SHALL be organized under a single root namespace named `Standard`.
2. THE Stdlib_Source_Root SHALL be a directory within the repository, separate from compiler source (e.g., `Source/Standard/`), containing `.mist` source files only — no C# source and no compiled artifacts committed.
3. THE Stdlib_Source_Root SHALL organize Bootstrap_Tier types and Pure_Mist_Tier types into distinguishable subdirectories or file-naming conventions so that a reader can tell which tier a file belongs to.
4. WHERE a Standard_Library type is a Bootstrap_Tier type, THE type SHALL reside in a file whose path clearly identifies it as bootstrap (e.g., `Source/Standard/Bootstrap/Int32.mist`).
5. WHERE a Standard_Library type is a Pure_Mist_Tier type, THE type SHALL reside in a file whose path clearly identifies it as non-bootstrap (e.g., `Source/Standard/Collections/List.mist`).
6. THE Stdlib_Source_Root SHALL contain a `README.md` or equivalent documentation file describing the tier layout and naming conventions.

### Requirement 2: Bootstrap Tier Type Membership

**User Story:** As a Mist user, I want the numeric and text primitives to be real types with callable members, so that I can write `x.ToString()` and, in future stdlib extensions, `x.CompareTo(y)`, without having to wrap primitives in external helpers.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare each of the following as a `public struct` in the `Standard` namespace: `Int32`, `UInt32`, `Int64`, `UInt64`, `Int16`, `UInt16`, `Byte`, `SByte`, `Single`, `Double`, `Decimal`, `Boolean`, `Char`.
2. THE Standard_Library SHALL declare `String` as a `public class` in the `Standard` namespace (not a struct, because strings are reference types in the current Mist model and on the .NET runtime).
3. THE Standard_Library SHALL declare `Array<T>` as a `public class` in the `Standard` namespace, parameterized over element type `T`.
4. THE Semantic_Model_Builder SHALL treat each Bootstrap_Tier type as a normal user-declared type for all purposes (member lookup, method resolution, inheritance, generics) except where a specific requirement in this document states otherwise.
5. WHERE a Bootstrap_Tier type declares instance fields, THE layout of those fields SHALL be irrelevant to the Backend — the Backend supplies the runtime representation via its Backing_Type_Map per Requirement 9.7 (e.g., for the C# backend, `Standard.Int32` maps to `int`).
6. THE Semantic_Model_Builder SHALL permit a Bootstrap_Tier type declaration to contain zero instance fields without emitting a diagnostic, because the backend provides the storage layout.
7. EACH Bootstrap_Tier type SHALL be declared with a type-level Intrinsic_Marker (see Requirement 5), making it a Backend_Backed_Type. The type-level marker signals that the Backend provides the backing storage and runtime representation for the type; it does not auto-promote the type's members to Intrinsic_Methods.

### Requirement 3: Bootstrap Tier Numeric Operators

**User Story:** As a Mist user, I want `+`, `-`, `*`, `/`, `%`, and comparison operators on primitive numeric types to behave exactly as they do today, so that existing programs keep working after the stdlib migration.

#### Acceptance Criteria

1. THE `Int32` type SHALL declare Operator_Declarations for each of `+`, `-`, `*`, `/`, `%` that take two `Int32` operands and return an `Int32`.
2. THE `Int32` type SHALL declare Operator_Declarations for each of `<`, `>`, `<=`, `>=`, `==`, `!=` that take two `Int32` operands and return a `Boolean`.
3. THE `Int32` type SHALL declare a unary `-` Operator_Declaration that takes one `Int32` and returns an `Int32`.
4. THE bootstrap tier SHALL repeat Requirement 3.1 through 3.3 for each of `UInt32`, `Int64`, `UInt64`, `Int16`, `UInt16`, `Byte`, `SByte`, `Single`, `Double`, `Decimal`, except that `UInt32`, `UInt64`, `UInt16`, and `Byte` SHALL NOT declare unary `-`. Arithmetic on `Byte`, `SByte`, `Int16` and `UInt16` answers the operand type, as declared, and wraps on overflow as `Int32` arithmetic does: `200 + 100` over two `Byte` operands is `44`. The Backend supplies that result whatever the target language's own arithmetic answers.
5. THE `Boolean` type SHALL declare Operator_Declarations for `&&`, `||`, and unary `!`.
6. THE `Boolean` type SHALL declare Operator_Declarations for `==` and `!=` between two `Boolean` operands returning a `Boolean`.
7. THE `Char` type SHALL declare Operator_Declarations for `==`, `!=`, `<`, `>`, `<=`, `>=` between two `Char` operands returning a `Boolean`.
8. THE `String` type SHALL declare an Operator_Declaration for `+` that takes two `String` operands and returns a `String` (concatenation).
9. THE `String` type SHALL declare Operator_Declarations for `==` and `!=` between two `String` operands returning a `Boolean`.
10. EACH Operator_Declaration described in this Requirement SHALL carry a member-level Intrinsic_Marker as described in Requirement 5 (member-level markers remain required on each operator, constructor, and method even when the containing type carries a type-level Intrinsic_Marker per Requirement 5; the type-level marker does not auto-promote members).

### Requirement 4: First-Class User-Definable Operators

**User Story:** As a Mist user (including the stdlib author), I want to declare operators on my own types using normal member syntax, so that the stdlib's operator declarations are not a privileged mechanism and I can define `+` on, for example, my own `Vector3` type.

#### Acceptance Criteria

1. THE Mist grammar SHALL accept an Operator_Declaration inside any `class` or `struct` body in the form: `<modifiers> <return_type> operator <op_symbol>(<parameter_list>) <body_or_semicolon>`.
2. THE grammar SHALL require an Operator_Declaration to carry the modifiers `public static` in v1. Existing `pure` effects and the Intrinsic_Marker under Requirement 5 remain permitted. Unsupported `sealed`, `readonly`, `owned`, and `affine` declaration modifiers SHALL produce a source diagnostic; the existing missing-static, async, and ref-return refusals remain in effect.
3. THE grammar SHALL support the following Operator_Symbol values for binary operators: `+`, `-`, `*`, `/`, `%`, `==`, `!=`, `<`, `>`, `<=`, `>=`.
4. THE grammar SHALL support the following Operator_Symbol values for unary operators: `-`, `!`.
5. THE grammar SHALL recognize `&&` and `||` as Operator_Symbol values declarable as binary operators, but only on the `Boolean` type for v1 (see Requirement 13 on non-goals).
6. WHEN an Operator_Declaration is parsed, THE Semantic_Model_Builder SHALL register it as a static method on the containing type, keyed by operator symbol and parameter types for overload resolution purposes. Two declarations with the same symbol and parameter signature SHALL receive a source duplicate-signature diagnostic at the later declaration, identifying the earlier declaration; return types alone SHALL NOT distinguish overloads. Unary and binary arities remain distinct.
7. IF an Operator_Declaration appears outside a class or struct body, THEN THE parser or Semantic_Model_Builder SHALL emit a diagnostic indicating operators must be declared as members of a type.
8. IF a binary Operator_Declaration's parameter count is not 2, THEN THE Semantic_Model_Builder SHALL emit a diagnostic indicating the expected parameter count for that operator.
9. IF a unary Operator_Declaration's parameter count is not 1, THEN THE Semantic_Model_Builder SHALL emit a diagnostic indicating the expected parameter count for that operator.
10. THE Semantic_Model_Builder SHALL permit user types to declare operators without any Intrinsic_Marker; the operator body is ordinary Mist code in that case.

### Requirement 5: Intrinsic Marker and Intrinsic Methods

**User Story:** As a stdlib author, I want a way to mark a type as backend-backed (so the backend knows to substitute its target-language backing representation wherever the type appears) and to mark individual members of that type as backend-provided (so calls to `Int32.operator +` don't infinitely recurse through Mist). Intrinsic members always sit inside backend-backed types, and a single type may mix intrinsic members (runtime-backed primitive operations) with ordinary Mist-bodied members (higher-level operations written in Mist that compose the intrinsics).

#### Acceptance Criteria

1. THE Mist grammar SHALL accept an Intrinsic_Marker on method declarations, operator declarations, and constructor declarations within `class` and `struct` bodies. The Intrinsic_Marker SHALL be the reserved keyword `intrinsic` appearing in the member's modifier list (alongside modifiers such as `public` and `static`).
2. WHERE a method, operator, or constructor carries the Intrinsic_Marker, THE declaration SHALL omit its body (terminate with `;` instead of `{ ... }`).
3. IF a declaration with the Intrinsic_Marker includes a body, THEN THE Semantic_Model_Builder SHALL emit a diagnostic indicating intrinsic members must not have a body.
4. IF a declaration without the Intrinsic_Marker omits its body (and is not an abstract interface method), THEN the existing behaviour SHALL apply (no change from today).
5. THE Intrinsic_Marker SHALL be recognized only on types declared within a privileged compilation set — specifically, only on types whose declaring Compilation_Unit is part of the Standard_Library.
6. IF a user-authored (non-stdlib) Compilation_Unit uses the Intrinsic_Marker, THEN THE Semantic_Model_Builder SHALL emit a diagnostic stating that the Intrinsic_Marker is reserved for the Standard_Library.
7. WHEN the CSharp_Emitter encounters a call to an Intrinsic_Method, THE CSharp_Emitter SHALL emit the target-language equivalent directly (e.g., for `Int32.operator +`, emit `a + b`) rather than emitting a call to a Mist-defined method.
8. THE CSharp_Emitter SHALL maintain a registry mapping `(stdlib type name, intrinsic member signature) → emission template`, used to implement Requirement 5.7.
9. IF an Intrinsic_Method is invoked but the Backend has no emission template for it, THEN THE Backend SHALL emit a diagnostic with severity `DiagnosticSeverity.Error` that identifies the missing intrinsic by its declaring type and member signature, SHALL NOT emit any fallback, stub, placeholder, or call to a Mist-defined body for that Intrinsic_Method, and SHALL cause the enclosing compilation to fail.
10. IF the Backend raises the missing-intrinsic error described in Requirement 5.9, THEN THE Backend SHALL produce no output artifact (no emitted source file, no compiled assembly, no serialized metadata) for that Entry_Compilation, and THE Compiler_Driver SHALL report the compilation as failed.
11. THE Mist grammar SHALL additionally accept the Intrinsic_Marker at the type level on `class` and `struct` declarations. At the type level, the `intrinsic` keyword appears in the type's modifier list (alongside `public`, `abstract`, etc.), as in `intrinsic public struct Int32 { ... }`. A type carrying a type-level Intrinsic_Marker is a Backend_Backed_Type: the Backend provides the type's storage layout and runtime representation via its Backing_Type_Map.
12. WHERE a type carries a type-level Intrinsic_Marker, the member-level Intrinsic_Marker on methods, constructors, and operators of that type SHALL still be required for each intrinsic member. The type-level marker SHALL NOT auto-promote members to Intrinsic_Methods; members without a member-level Intrinsic_Marker remain ordinary Mist-bodied members (and, per Requirement 5.4, must have a body).
13. IF a user-authored (non-stdlib) Compilation_Unit places a type-level Intrinsic_Marker on a class or struct declaration, THEN THE Semantic_Model_Builder SHALL emit a diagnostic stating that the type-level Intrinsic_Marker is reserved for the Standard_Library (analogous to Requirement 5.6 for member-level markers).
14. THE reserved keyword `intrinsic` SHALL be added to the Mist lexer's keyword list. Because `intrinsic` is a reserved keyword (not a contextual one), it SHALL NOT be usable as an identifier anywhere in Mist source. The keyword SHALL be defined in exactly one place in the grammar source (`Source/Mist/Grammar/keywords.storm`).
15. IF a member-level Intrinsic_Marker appears on a method, constructor, or operator declaration whose containing class or struct does NOT itself carry a type-level Intrinsic_Marker, THEN THE Semantic_Model_Builder SHALL emit a diagnostic stating that member-level `intrinsic` is only permitted inside a type that is itself declared `intrinsic`. This pairing rule SHALL apply even within the Standard_Library: intrinsic members are always hosted on Backend_Backed_Types.

### Requirement 6: Operator Overload Resolution

**User Story:** As a compiler developer, I want the type checker to stop using its hardcoded `NumericTypes` set and instead resolve operator expressions via overload resolution, so that user-defined operators and stdlib-defined operators are treated uniformly.

#### Acceptance Criteria

1. WHEN the Type_Checker analyzes a binary operator expression `a <op> b`, THE Type_Checker SHALL search for an applicable Operator_Declaration named `<op>` on either the type of `a` or the type of `b`.
2. WHEN the Type_Checker analyzes a unary operator expression `<op> a`, THE Type_Checker SHALL search for an applicable Operator_Declaration named `<op>` on the type of `a`.
3. THE Type_Checker SHALL apply the same overload-resolution algorithm used for ordinary method calls (as implemented in `OverloadResolver.cs`) to select among multiple applicable Operator_Declarations.
4. IF zero Operator_Declarations are applicable, THEN THE Type_Checker SHALL emit a diagnostic in the existing form: `Operator '<op>' cannot be applied to operands of type '<T1>' and '<T2>'`, preserving existing diagnostic IDs (`MIST_TC_0003 (historical)` through `MIST_TC_0006 (historical)`) where possible.
5. IF two or more Operator_Declarations are equally applicable, THEN THE Type_Checker SHALL emit an ambiguity diagnostic with ID `MIST_TC_0032 (historical)` (the existing ambiguous-overload code).
6. THE Type_Checker SHALL compute the result type of an operator expression as the return type of the selected Operator_Declaration.
7. THE Type_Checker SHALL no longer maintain a hardcoded `NumericTypes` HashSet for operator applicability; all numeric-operator applicability SHALL flow from Operator_Declarations on the bootstrap tier types.
8. THE Type_Checker SHALL no longer special-case `StringType` for `+` concatenation; string concatenation SHALL flow from the Operator_Declaration in Requirement 3.8.
9. THE Type_Checker SHALL no longer special-case `PrimitiveType.Bool` for `&&`, `||`, `!`; these SHALL flow from Operator_Declarations in Requirement 3.5.
10. WHEN operator overload resolution selects an Intrinsic_Method, THE CSharp_Emitter SHALL emit the target-language operator form (Requirement 5.7) rather than a method-call form.

### Requirement 7: Primitive Keyword Aliases

**User Story:** As a Mist user, I want existing code like `int x = 42;` and `string s = "hi";` to keep compiling after the stdlib migration, without me changing anything, so that backwards compatibility is preserved.

**Context:** Each Primitive_Keyword (`int`, `string`, etc.) is a reserved keyword in the Mist grammar. It is not an identifier and is not subject to normal name-resolution scoping. Writing `int` in Mist source is a request for the single specific type `Standard.Int32` — the type declared in the Standard_Namespace — and is unambiguous regardless of what identifiers (including user-declared types named `Int32`) happen to be in scope. Because of this keyword-alias mechanism, `int x = 42;` compiles without any `using Standard;` directive — using primitives feels as natural as it does today, with no implicit prelude required.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL treat each Primitive_Keyword as an alias for its corresponding fully-qualified Stdlib_Type_Name per the Keyword_Alias_Mapping: `int → Standard.Int32`, `uint → Standard.UInt32`, `long → Standard.Int64`, `ulong → Standard.UInt64`, `short → Standard.Int16`, `ushort → Standard.UInt16`, `byte → Standard.Byte`, `sbyte → Standard.SByte`, `float → Standard.Single`, `double → Standard.Double`, `decimal → Standard.Decimal`, `bool → Standard.Boolean`, `char → Standard.Char`, `string → Standard.String`.
2. WHEN the Type_Checker computes the type of a literal or variable declared with a Primitive_Keyword, THE resulting `IType` SHALL be the corresponding Standard_Library type instance (i.e., the type whose fully-qualified name appears on the right-hand side of the Keyword_Alias_Mapping), not a `PrimitiveType_Singleton`.
3. THE CSharp_Emitter SHALL emit the target-language equivalent for each Primitive_Keyword unchanged from today's behaviour (e.g., `int` in Mist → `int` in C#), regardless of whether the Mist source used the keyword or the stdlib name.
4. THE CSharp_Emitter SHALL emit the target-language equivalent for the corresponding Stdlib_Type_Name identically to its keyword form (e.g., `Standard.Int32` in Mist → `int` in C#), so that `int` and `Int32` (resolved to `Standard.Int32`) produce the same emission.
5. THE Keyword_Alias_Mapping SHALL be defined in exactly one place in the compiler source tree (e.g., a single `KeywordAliases.cs` file) so that the mapping is not duplicated.
6. WHEN the Semantic_Model_Builder resolves a Primitive_Keyword, THE resolution SHALL target the type declared in the Standard_Namespace specified by the Keyword_Alias_Mapping, and the resolution SHALL bypass all lexical, namespace, and using-directive scoping. The Primitive_Keyword SHALL NOT be affected by explicit `using` directives, nor by any types in scope with matching simple names.
7. IF a user Compilation_Unit declares a type whose simple name matches a Stdlib_Type_Name's simple-name form (e.g., a user declares a type named `Int32` in their own namespace), THEN the Primitive_Keyword (e.g., `int`) SHALL continue to resolve to the Standard_Library type named by the Keyword_Alias_Mapping (e.g., `Standard.Int32`) and SHALL NEVER resolve to the user-declared type. Keyword-to-stdlib-type resolution is not user-overridable.
8. THE Primitive_Keyword resolution in 7.1 and 7.6 operates at a different layer than simple-name resolution in Mist source: unqualified simple names (e.g., `Int32`, `String`) are ordinary identifiers subject to normal scoping rules — they resolve only when the corresponding namespace is in scope via an explicit `using Standard;` directive or when a user-declared type of that name is in scope, and they can be shadowed by user types; Primitive_Keywords (e.g., `int`, `string`) are reserved keywords that cannot be shadowed and always resolve via the Keyword_Alias_Mapping.

### Requirement 8: PrimitiveType Singletons Migration

**User Story:** As a compiler developer, I want to replace the hardcoded `PrimitiveType` singletons with references to real stdlib types, so that the semantic model has a single, consistent representation of primitive types.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL, at the end of this feature's implementation, no longer return a `PrimitiveType_Singleton` from any public API; all callers SHALL receive the corresponding Standard_Library type.
2. THE `PrimitiveType` class SHALL be removed from the compiler source tree, or retained only as a private implementation detail that is never exposed to analyzers, backends, or downstream callers.
3. DURING the migration window of this feature, THE Semantic_Model_Builder MAY maintain a compatibility shim that exposes `PrimitiveType.Int` (etc.) as a reference to the corresponding `Standard.Int32` type, to keep intermediate pipeline stages working while individual call sites are migrated.
4. IF any call site outside the stdlib continues to reference a `PrimitiveType_Singleton` after the migration completes, THEN THE build SHALL fail at compile time (via deletion of the type or a static analyzer enforced warning-as-error).
5. THE migration SHALL preserve reference-equality semantics for primitive-type identity: two occurrences of `int` in the semantic model SHALL resolve to the same `IType` instance (the `Standard.Int32` type).

### Requirement 9: Stdlib Migration of Existing Ambient Types

**User Story:** As a compiler developer, I want to know which of today's ambient types (`String`, `Array`, `Console`, `Math`, `Convert`) become stdlib types and which stay ambient, and I want the Mist-facing semantic model to see only Mist types — never target-language types — so that the migration plan is unambiguous and no CLR-specific type leaks into the semantic model.

#### Acceptance Criteria

1. THE Standard_Library SHALL own `String` as a Bootstrap_Tier class declared in Mist and marked as a Backend_Backed_Type per Requirement 5.11, replacing the role currently played by `StringType.Instance` and the `System.String` ambient type for Mist-facing purposes.
2. THE Standard_Library SHALL own `Array<T>` as a Bootstrap_Tier generic class declared in Mist and marked as a Backend_Backed_Type per Requirement 5.11, replacing the role currently played by `ArrayType` and the `System.Array` ambient type for Mist-facing purposes.
3. THE CSharp_Ambient_Type_Resolver SHALL continue to expose `Console` as an ambient type (not moved to stdlib in v1 — see Requirement 13 non-goals).
4. THE CSharp_Ambient_Type_Resolver SHALL continue to expose `Math` as an ambient type (not moved to stdlib in v1).
5. THE CSharp_Ambient_Type_Resolver SHALL continue to expose `Convert` as an ambient type (not moved to stdlib in v1).
6. THE CSharp_Ambient_Type_Resolver SHALL NOT expose `System.String`, `System.Array`, or any other target-language-specific type as a Mist `IType` after this feature is implemented. `System.String` and `System.Array` SHALL exist only as target entries in the C# backend's Backing_Type_Map (Requirement 9.7), not as values in the semantic model.
7. THE C# backend SHALL maintain a Backing_Type_Map that includes at minimum the entries `Standard.String → string` (the C# keyword alias for `System.String`) and `Standard.Array<T> → T[]`. The Backing_Type_Map SHALL additionally contain an entry for each Bootstrap_Tier numeric primitive and for `Boolean` and `Char`, mapping to the corresponding C# keyword alias (e.g., `Standard.Int32 → int`, `Standard.Boolean → bool`, `Standard.Char → char`).
8. WHEN the CSharp_Emitter encounters a reference to a Backend_Backed_Type in generated code — as a variable type, parameter type, return type, generic type argument, field type, cast target, or any other syntactic position where a type name appears — THE CSharp_Emitter SHALL substitute the corresponding target-language type from the Backing_Type_Map, applying the substitution uniformly across all emission sites.
9. THE `IAmbientTypeResolver` contract SHALL produce method, constructor, property, and field signatures whose parameter types and return types are expressed exclusively in Mist types — Standard_Library types (including Backend_Backed_Types), keyword aliases resolving to Standard_Library types, user-declared types in the current compilation, or other ambient types. No `IType` produced by any `IAmbientTypeResolver` SHALL correspond to a target-language type that is not visible in Mist.
10. THE CSharp_Ambient_Type_Resolver SHALL translate its internal knowledge of CLR runtime shapes into Mist-typed signatures at its API boundary. Specifically, the CLR method `System.Console.WriteLine(System.String)` SHALL be surfaced to the semantic model as `Console.WriteLine(Standard.String)`; the CLR's `System.String` SHALL appear only as the emitter's Backing_Type_Map target for `Standard.String`, not as a Mist `IType` returned by any resolver API.
11. THE Backing_Type_Map SHALL be Backend-private. THE Type_Checker, THE Semantic_Model_Builder, every `IAmbientTypeResolver` implementation, and every analyzer SHALL NOT access, query, or depend on the Backing_Type_Map. Only the CSharp_Emitter (or emitter-adjacent emission utilities in the C# backend) SHALL consult it.
12. ALL members currently exposed by the ambient `System.String` (`Length`, `Substring`, `Contains`) SHALL be declared as members of `Standard.String`, with the same names, signatures (expressed in Mist types), and semantics.
13. ALL members currently exposed by the ambient `System.Array` (`Length`) SHALL be declared as members of `Standard.Array<T>`, with the same names, signatures (expressed in Mist types), and semantics.
14. WHEN a Mist program uses the fully-qualified name `Standard.String`, or uses the simple name `String` with `using Standard;` in scope (and no user-declared type named `String` shadows it), THE Semantic_Model_Builder SHALL resolve it to the stdlib's `Standard.String` type. WHEN a Mist program uses the simple name `String` without `using Standard;` in scope and with no user-declared type of that name in scope, THE Semantic_Model_Builder SHALL emit an "unresolved type" diagnostic per the compiler's ordinary name-resolution rules.

### Requirement 10: Stdlib Distribution and Loading

**User Story:** As a compiler developer, I want to pick a stdlib loading strategy with eyes open to the tradeoffs, so that the chosen approach is deliberate and well-justified.

**NOTE:** This requirement documents three candidate approaches and their tradeoffs. The actual choice is deferred to the Design phase; the user will select one approach before design is finalized. All three approaches satisfy the user-facing acceptance criteria in Requirements 10.1 through 10.4 — they differ in *how* those criteria are satisfied.

#### Acceptance Criteria (user-facing, apply regardless of chosen approach)

1. WHEN a user runs `mist build` on any project, THE Standard_Library SHALL be available in that compilation without any per-project configuration.
2. WHEN a user runs `mist build` on a project with no network access, THE Standard_Library SHALL still be available (offline builds work).
3. THE Standard_Library SHALL ship with the Mist compiler as a single unit, versioned together with the compiler (no separate package download).
4. THE Standard_Library SHALL be loadable across all Mist backends (today: C# transpiler; future: any backend that satisfies the intrinsic-emission contract of Requirement 5).

#### Candidate Approach A: Precompiled Artifact

5. (Approach A) WHERE the loading strategy is a precompiled artifact, THE Standard_Library SHALL be compiled once at compiler-build time into a serialized metadata artifact (e.g., `standard.stdlib` file) that ships alongside the compiler binary.
6. (Approach A) WHERE the loading strategy is a precompiled artifact, THE Compiler_Driver SHALL load the artifact at compiler startup and register its types into the semantic model's root scope.
7. (Approach A tradeoffs) This approach offers the fastest compiler startup and the smallest per-compilation cost, at the price of needing a stdlib serialization format and a compiler-build step that produces it. It also couples the artifact format to the compiler version.

#### Candidate Approach B: Source Prelude

8. (Approach B) WHERE the loading strategy is a source prelude, THE Compiler_Driver SHALL prepend the stdlib `.mist` files to the user's compilation (or compile them as additional Compilation_Units) on every invocation.
9. (Approach B) WHERE the loading strategy is a source prelude, THE Compiler_Driver SHALL locate the stdlib source directory via a well-known path relative to the compiler binary (e.g., a `Standard/` sibling directory).
10. (Approach B tradeoffs) This approach has the simplest implementation (no serialization format, no compiler-build step), guarantees the stdlib source matches the compiler version, and makes stdlib source shippable and modifiable at compiler install location. Costs: per-compilation overhead of re-parsing and re-analyzing stdlib files (mitigable via caching).

#### Candidate Approach C: Declarations-Only With Backend Intrinsics

11. (Approach C) WHERE the loading strategy is declarations-only-with-backend-intrinsics, THE Standard_Library SHALL contain only type declarations and intrinsic method signatures (no method bodies for any operator or primitive member), and all operator semantics SHALL live in the backend's Intrinsic_Method emission templates.
12. (Approach C tradeoffs) This approach makes the stdlib trivially small and avoids recompiling intrinsics, at the price of preventing the Pure_Mist_Tier from being expressed as Mist source. This tension can be resolved by treating Approach C as applying only to Bootstrap_Tier; Pure_Mist_Tier would still require loading via Approach A or B. This bifurcation complicates the loading model.

#### Decision Criteria

13. THE chosen loading approach SHALL be recorded in the design document with explicit justification against the tradeoffs in 10.7, 10.10, and 10.12.
14. THE chosen loading approach SHALL preserve offline buildability (10.2) and versioning-with-compiler (10.3).

### Requirement 11: Existing Sample Backwards Compatibility

**User Story:** As a Mist developer, I want every existing sample program to keep compiling and running identically after the stdlib ships, so that this feature is a non-breaking change from the user's perspective.

#### Acceptance Criteria

1. THE existing sample `samples/AllFeatures/Program.mist` SHALL compile without changes after this feature is implemented.
2. THE existing sample `samples/AllFeatures/Program.mist` SHALL produce byte-identical standard output after this feature is implemented, compared to a baseline run captured before implementation begins.
3. EVERY other `.mist` file under `samples/` that compiles today SHALL compile after this feature is implemented without source modifications.
4. EVERY other `.mist` file under `samples/` that runs today SHALL produce byte-identical standard output after this feature is implemented.
5. THE CSharp_Emitter SHALL emit semantically-equivalent C# for a Mist program that uses primitive keywords (`int`, `string`, `bool`, etc.) before and after this feature — byte-identical emission is not required (comment headers, ordering, etc., may differ), but the compiled behaviour SHALL be unchanged.
6. WHEN a sample uses `String.Contains`, `String.Substring`, or `Array.Length`, THE call SHALL resolve after this feature via `Standard.String` / `Standard.Array<T>` members and SHALL emit the same C# call as today.

### Requirement 12: Correctness Properties

**User Story:** As a compiler developer, I want property-based tests that pin down the most critical invariants of this feature, so that regressions in operator resolution, name resolution, and backend emission are caught automatically.

#### Acceptance Criteria

1. (Bootstrap correctness) FOR ALL pairs of Int32 literal values `a, b` within the legal Int32 range, evaluating `a + b` in a compiled Mist program SHALL produce the same value as evaluating the equivalent C# expression `(int)a + (int)b` (with identical overflow/wrap semantics). THE test SHALL enumerate a representative sample via property-based testing.
2. (Bootstrap correctness) THE property in 12.1 SHALL be stated and tested for each of the operators in Requirement 3.1 (`+`, `-`, `*`, `/`, `%`) applied to each bootstrap numeric type.
3. (Bootstrap correctness — comparison) THE property analogous to 12.1 SHALL be stated and tested for each of `<`, `>`, `<=`, `>=`, `==`, `!=` on bootstrap numeric types, returning identical Boolean results to the C# equivalents.
4. (Namespace hygiene under explicit `using`) FOR ALL user Compilation_Units that include an explicit `using Standard;` directive and also declare a type named `T` in the compilation unit's own namespace `N`, writing the unqualified simple name `T` SHALL resolve to the user's `N.T` rather than to `Standard.T`. THE test SHALL construct adversarial examples where `T` matches a stdlib type simple-name form (e.g., user declares `String` in their own namespace) and verify that user types shadow stdlib types under ordinary name-resolution scoping.
5. (Determinism) FOR ANY well-typed Mist expression involving operators, Operator_Overload_Resolution SHALL produce the same selected Operator_Declaration on every compilation of the same source (no dependence on HashSet iteration order, file-system enumeration order, or parallelism).
6. (Backwards compatibility regression guard) THE test suite SHALL include a test that compiles every `.mist` file under `samples/`, runs it, and compares stdout against a captured baseline; the test SHALL fail if any sample's output diverges.
7. (Operator round-trip property) WHERE a stdlib-declared operator has an inverse (e.g., `+` and `-`), FOR ALL pairs `a, b` of the operator's operand type, `(a + b) - b` SHALL equal `a` (for overflow-free inputs). Where overflow matters, the property SHALL be narrowed to inputs that do not overflow.
8. THE property tests required by 12.1 through 12.7 SHALL be implementable with a property-based testing library (e.g., FsCheck) and SHALL NOT require external AWS, network, or slow operations. Operator-correctness properties are pure functions of their inputs; they meet the "property-based testing is appropriate" criteria in the workflow guidance.
9. (Keyword vs simple-name resolution) FOR ALL user Compilation_Units that declare a type named `Int32` (or any other Stdlib_Type_Name simple-name form) in some namespace `N`, writing the Primitive_Keyword `int` (or the keyword corresponding to the shadowed Stdlib_Type_Name) SHALL resolve to the `Standard`-namespace type named by the Keyword_Alias_Mapping, and SHALL NEVER resolve to the user's type. THE test SHALL construct adversarial examples where the user-declared `Int32` has different members than `Standard.Int32` and verify that expressions using `int` invoke `Standard.Int32` members.
10. (No leak of backing types into the semantic model) FOR ALL user-visible `IType` instances produced by the Type_Checker, the Semantic_Model_Builder, and every `IAmbientTypeResolver` in the course of analyzing a Compilation_Unit, NONE SHALL be a target-language-specific backing type (`System.String`, `System.Array`, `System.Int32`, etc.). THE test SHALL construct compilations that exercise `Console.WriteLine("hi")`, `Math.Abs(-1)`, ambient array operations (`array.Length`), string literals, numeric literals, member access on primitives (`x.ToString()`), and calls whose CLR signatures reference `System.String` / `System.Array` / CLR primitives, then enumerate every `IType` reachable through the resulting semantic model (parameter types, return types, variable types, expression types, generic type arguments, field types) and assert that each is either: a user-declared Mist type, a Standard_Library type (Backend_Backed_Type or Pure_Mist_Tier type), or an ambient synthesized type whose own members' signatures satisfy the same property recursively. THE test SHALL fail if any reachable `IType` corresponds to a target-language backing type.

### Requirement 13: Non-Goals (Explicitly Out of Scope for v1)

**User Story:** As a compiler developer, I want to pin down what this feature does *not* include, so that scope creep is prevented and reviewers can evaluate the proposal against a clear boundary.

#### Acceptance Criteria

1. THE Standard_Library v1 SHALL NOT include bitwise operators (`&`, `|`, `^`, `~`, `<<`, `>>`) on any type. Bitwise support is deferred to a later stdlib version.
2. THE Standard_Library v1 SHALL NOT include compound-assignment operators (`+=`, `-=`, `*=`, etc.) as declarable operators. These are desugared by the parser today and SHALL continue to desugar to their binary-operator form.
3. THE Standard_Library v1 SHALL NOT include increment/decrement operators (`++`, `--`). Mist does not have these operators today and adding them is out of scope.
4. THE Standard_Library v1 SHALL NOT include conversion operators (implicit or explicit user-defined conversions). All numeric conversions continue to require `Convert.ToInt32(...)` etc.
5. THE Standard_Library v1 SHALL NOT include indexer operator declarations (`this[...]`) as Operator_Declarations — indexers remain a separate member kind as they are today.
6. THE Standard_Library v1 SHALL NOT include any collection types beyond `Array<T>` (no `List<T>`, `Dictionary<K,V>`, `HashSet<T>` in v1) as Pure_Mist_Tier members. If Pure_Mist_Tier contents beyond operators-on-primitives are desired, they are a v1.x follow-up, not v1.
7. THE Standard_Library v1 SHALL NOT move `System.Console`, `System.Math`, or `System.Convert` into the stdlib. These remain ambient types per Requirement 9.
8. THE Standard_Library v1 SHALL NOT add generic constraints for numeric types (no `where T : INumeric`). Generic-over-numeric code remains impossible in v1 (it is impossible today).
9. THE Standard_Library v1 SHALL NOT support async or Task-based operators/methods on primitives.
10. THE Standard_Library v1 SHALL NOT support interface implementation on primitives (e.g., `Int32 : IComparable`). Interfaces on primitives are a v1.x follow-up blocked by this v1 landing first.
11. THE Mist language v1 SHALL NOT provide any syntactic escape hatch for Mist source to directly name a Backend's target-language backing type (e.g., Mist source cannot write `System.String`, `System.Int32`, `int[]` in C# syntax, or equivalent forms for other backends). Backing types are Backend-private per Requirement 9.11 and are reachable from Mist only indirectly through their corresponding Backend_Backed_Type (e.g., `Standard.String`). Arbitrary cross-language interop (e.g., naming C# types from Mist source) is out of scope for v1 and would be a separate feature.

### Requirement 14: Diagnostic IDs and Messages

**User Story:** As a compiler developer, I want the new diagnostics introduced by this feature to use clear, unique IDs that don't collide with existing ones, so that the diagnostic catalog stays organized.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL use existing diagnostic ID `MIST_TC_0003 (historical)` for "operator cannot be applied to operands of type X and Y" (unchanged from today; the detection path changes but the ID and message form are preserved).
2. THE Semantic_Model_Builder SHALL use existing diagnostic ID `MIST_TC_0032 (historical)` for ambiguous operator overload resolution (same ID used today for ambiguous method overloading).
3. THE Semantic_Model_Builder SHALL introduce a new diagnostic ID for "intrinsic marker is reserved for the Standard Library" (Requirement 5.6); the specific ID SHALL be allocated in design (next unused `MIST_SB_* (historical scheme)` or `MIST_TC_* (historical scheme)`). The same diagnostic ID (or a closely related one allocated in the same block) SHALL cover the type-level case in Requirement 5.13 ("type-level intrinsic marker is reserved for the Standard Library"); design SHALL decide whether one ID covers both placements or whether type-level and member-level misuse get distinct IDs.
4. THE Semantic_Model_Builder SHALL introduce a new diagnostic ID for "intrinsic member must not have a body" (Requirement 5.3).
5. THE Semantic_Model_Builder SHALL introduce a new diagnostic ID for "member-level `intrinsic` requires a type-level `intrinsic` declaration" (Requirement 5.15), emitted when a method, constructor, or operator is marked `intrinsic` but its containing class or struct is not.
6. THE Semantic_Model_Builder SHALL introduce a new diagnostic ID for "operator must be declared as a member of a type" (Requirement 4.7).
7. THE Semantic_Model_Builder SHALL introduce a new diagnostic ID for "binary operator requires exactly 2 parameters" / "unary operator requires exactly 1 parameter" (Requirements 4.8 and 4.9).
8. EVERY new diagnostic ID introduced by this feature SHALL be documented in the design document with its message template and an example source snippet that triggers it.

### Requirement 15: Open Questions Surfaced for Design

The following items are intentionally not pinned down in requirements — they require design-time thinking and possibly spike work:

1. **~~Intrinsic marker syntax.~~** **Resolved in requirements (Requirement 5.1, 5.11, 5.14):** the `intrinsic` reserved modifier keyword is used at both member level (`intrinsic public static Int32 operator +(...)`) and type level (`intrinsic public struct Int32 { ... }`). No further design decision required.
2. **Loading strategy (Approach A / B / C / hybrid).** Requirement 10 presents the tradeoffs; the choice belongs to design. The user has indicated they are unsure and want to see the tradeoffs laid out before deciding.
3. **Nominal identity during migration.** Today's `PrimitiveType.Int` singleton is distinguished by reference equality. When the stdlib's `Int32` becomes the "real" int, how does the compiler prevent a stale `PrimitiveType.Int` reference from slipping through? Is the safest migration to keep both alive and have `PrimitiveType.Int` be a compatibility alias for `Standard.Int32`, or to delete `PrimitiveType.Int` in one step?
4. **Operator resolution across type boundaries.** If `A + B` has an operator on `A` returning `C` and an operator on `B` returning `D`, which wins? C# has "better overload" tiebreakers; design must decide whether Mist mirrors them or picks a simpler deterministic rule.
5. **Where does the stdlib live on disk at install time?** If Approach B wins, the compiler must find stdlib `.mist` files relative to its own binary. Does `dotnet publish` of `mist` package the stdlib into `publish/Standard/`? Design must specify.
6. **Pure-Mist tier contents.** Requirement 13.6 defers non-trivial Pure_Mist_Tier content. The user may want at least one "real" pure-Mist example (e.g., `Optional<T>` or a single utility struct) in v1 to prove the tier works end-to-end. Design should propose whether v1 ships with a minimal pure-Mist tier demo or an empty one.

These questions are recorded here so the requirements review can confirm they are left open deliberately rather than being overlooked.

# Requirements Document

## Introduction

This document specifies the requirements for restricting enum base types to readonly types in the Mist programming language. Currently, enums can use any valid type as their underlying (base) type — primitives, classes, structs, or union types. This feature adds a compile-time constraint requiring that the base type of an enum declaration must be a readonly type. This ensures that enum member values are immutable, which aligns with the semantic expectation that enum values are constants that do not change after definition.

The standard library's bootstrap types (`Int32`, `String`, `Byte`, `Boolean`, etc.) are genuinely immutable and will be marked `readonly` in their stdlib definitions as part of this feature. This means the type checker needs no special-case logic for primitives — it simply checks whether the resolved type is readonly.

## Glossary

- **Enum_Declaration**: A top-level language construct that defines a named set of static members, each holding a value of the underlying type.
- **Underlying_Type**: The type that all enum member values conform to. May be explicitly annotated or inferred from member values. Also referred to as the "base type" of the enum.
- **Readonly_Type**: A type (class, struct, interface, or trait) annotated with the `readonly` modifier, whose members are all implicitly readonly.
- **Bootstrap_Type**: A standard library type defined in `Source/Standard/Bootstrap/` that is backed by a CLR primitive (e.g., `Int32`, `String`, `Byte`, `Boolean`). These types are intrinsic and immutable.
- **Type_Checker**: The component that validates type correctness and enforces semantic rules during compilation.
- **Semantic_Model**: The internal representation of resolved types, symbols, and their relationships after semantic analysis.
- **Union_Type**: A type formed by combining two or more types with the `|` operator. Each constituent type must independently satisfy the readonly requirement.
- **CSharp_Emitter**: The backend that translates the Mist semantic model into C# source code.

## Requirements

### Requirement 1: Mark Bootstrap Types as Readonly

**User Story:** As a language developer, I want the standard library's bootstrap types to be marked `readonly`, so that they are correctly recognized as immutable by the type system without special-case logic.

#### Acceptance Criteria

1. THE standard library definition of `Int32` SHALL include the `readonly` modifier.
2. THE standard library definition of `Int16` SHALL include the `readonly` modifier.
3. THE standard library definition of `Int64` SHALL include the `readonly` modifier.
4. THE standard library definition of `UInt16` SHALL include the `readonly` modifier.
5. THE standard library definition of `UInt32` SHALL include the `readonly` modifier.
6. THE standard library definition of `UInt64` SHALL include the `readonly` modifier.
7. THE standard library definition of `Byte` SHALL include the `readonly` modifier.
8. THE standard library definition of `SByte` SHALL include the `readonly` modifier.
9. THE standard library definition of `Boolean` SHALL include the `readonly` modifier.
10. THE standard library definition of `String` SHALL include the `readonly` modifier.
11. THE standard library definition of `Char` SHALL include the `readonly` modifier.
12. THE standard library definition of `Single` SHALL include the `readonly` modifier.
13. THE standard library definition of `Double` SHALL include the `readonly` modifier.
14. THE standard library definition of `Decimal` SHALL include the `readonly` modifier.
15. WHEN the semantic model is built from a standard library compilation unit containing a bootstrap type with the `readonly` modifier, THE semantic model SHALL set `IsReadonly` to `true` on the resulting type, so that `IsTypeReadonly()` returns `true` without name-based special-case checks.
16. WHEN the type checker needs to determine whether a bootstrap type is immutable, THE type checker SHALL use the general-purpose `IsTypeReadonly()` query rather than a hardcoded list of type names.

### Requirement 2: Readonly Restriction on Explicit Enum Base Types

**User Story:** As a Mist developer, I want the compiler to reject enum declarations whose explicit base type is not readonly, so that I can be confident enum values are immutable constants.

#### Acceptance Criteria

1. WHEN an enum declaration has an explicit type annotation and the annotated type is a readonly type, THE Type_Checker SHALL accept the declaration without error.
2. IF an enum declaration has an explicit type annotation and the annotated type is not readonly, THEN THE Type_Checker SHALL report a diagnostic error identifying the enum name and the non-readonly base type.
3. WHEN an enum declaration has an explicit union type annotation, THE Type_Checker SHALL verify that every constituent type in the union is readonly.
4. IF any constituent type in a union base type annotation is not readonly, THEN THE Type_Checker SHALL report a diagnostic error identifying the non-readonly constituent type.

### Requirement 3: Readonly Restriction on Inferred Enum Base Types

**User Story:** As a Mist developer, I want the compiler to reject enum declarations whose inferred base type is not readonly, so that the restriction applies consistently regardless of whether I write the type annotation explicitly.

#### Acceptance Criteria

1. WHEN an enum declaration has no explicit type annotation and the inferred underlying type is a readonly type, THE Type_Checker SHALL accept the declaration without error.
2. IF an enum declaration has no explicit type annotation and the inferred underlying type is not readonly, THEN THE Type_Checker SHALL report a diagnostic error identifying the enum name and the non-readonly inferred type.
3. WHEN an enum declaration has no explicit type annotation and the inferred type is a union type, THE Type_Checker SHALL verify that every constituent type in the union is readonly.
4. IF any constituent type in an inferred union base type is not readonly, THEN THE Type_Checker SHALL report a diagnostic error identifying the non-readonly constituent type.
5. WHEN an enum declaration has no explicit type annotation and has no members (name-only enum), THE Type_Checker SHALL apply the readonly check to the default inferred type.

### Requirement 4: Diagnostic Messages

**User Story:** As a Mist developer, I want clear error messages when I use a non-readonly type as an enum base, so that I understand what is wrong and how to fix it.

#### Acceptance Criteria

1. WHEN the Type_Checker reports a non-readonly base type error, THE diagnostic message SHALL include the enum name and the name of the non-readonly type.
2. WHEN the Type_Checker reports a non-readonly base type error, THE diagnostic message SHALL state that enum base types must be readonly.
3. WHEN the Type_Checker reports a non-readonly constituent type error in a union, THE diagnostic message SHALL include the enum name and identify which specific constituent type is not readonly.
4. THE Type_Checker SHALL report all non-readonly base type diagnostics and non-readonly constituent type diagnostics with a severity of Error.
5. THE Type_Checker SHALL assign a unique diagnostic ID to non-readonly base type errors and a unique diagnostic ID to non-readonly constituent type errors, distinct from all existing diagnostic IDs in the compiler.

### Requirement 5: Interaction with Existing Enum Features

**User Story:** As a Mist developer, I want the readonly restriction to compose correctly with all existing enum features, so that valid enum patterns using readonly types continue to work.

#### Acceptance Criteria

1. WHEN an enum uses a readonly struct as its base type, THE Type_Checker SHALL accept the declaration without error.
2. WHEN an enum uses a readonly class as its base type, THE Type_Checker SHALL accept the declaration without error.
3. WHEN an enum uses a readonly interface as its base type, THE Type_Checker SHALL accept the declaration without error.
4. WHEN an enum uses a readonly trait as its base type, THE Type_Checker SHALL accept the declaration without error.
5. WHEN an enum with a readonly base type has members with explicit values, THE Type_Checker SHALL verify that each member value is assignable to the readonly base type.
6. WHEN an enum with a readonly base type is used as a variable type, parameter type, return type, or field type, THE Semantic_Model SHALL accept those usages without error.
7. WHEN an enum value with a readonly base type is assigned to a variable of the underlying readonly type, THE Semantic_Model SHALL permit the implicit conversion without error.
8. WHEN an expression `x is EnumName.MemberName` is evaluated and the enum has a readonly base type, THE Pattern_Matcher SHALL perform value equality comparison and return the correct boolean result.
9. WHEN an enum with a readonly base type is accessed via `EnumName.MemberName`, THE Semantic_Model SHALL resolve the member and return a value typed as the EnumType without error.

### Requirement 6: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating the enum readonly restriction, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/EnumReadonlyRestriction/Program.mist` with an accompanying `mist.toml` that declares the project name as `EnumReadonlyRestriction`.
2. The sample SHALL demonstrate the following usage patterns: enum with primitive base type (int, string, byte), enum with readonly struct base type, enum with readonly class base type, name-only enum (inferred byte), enum with union of readonly types, member access on readonly-base enums, implicit conversion from readonly-base enum to underlying type, and pattern matching with readonly-base enums.
3. Each demonstrated pattern SHALL print a section header line identifying the pattern followed by one or more indented value lines showing the result, so that a tester can verify each pattern produced the expected output by inspecting stdout.
4. `samples/AllFeatures/Program.mist` SHALL call `EnumReadonlyRestriction.Program.RunDemo()`, and `samples/AllFeatures/mist.toml` SHALL include a `[[references]]` entry with `path = "../EnumReadonlyRestriction"`.
5. The sample SHALL compile and run without errors using the Mist compiler, both standalone and as part of the AllFeatures project.

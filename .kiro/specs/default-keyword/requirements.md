# Requirements Document

> **Superseded** by [`defaultability`](../defaultability/requirements.md) (2026-09-26), which states the rules as they now stand and governs wherever the two differ. This document is kept so references to it by requirement number still resolve; the new spec's appendix maps each requirement here to where it now lives.

## Introduction

This document specifies the requirements for adding the `default` keyword to the Mist programming language. The `default` keyword in Mist diverges significantly from C#: rather than producing a zeroed-out value or `null`, Mist's `default` is a value-based mechanism where every concrete type (class or struct) implements a default value. The default value is a pure compile-time (comptime) value — when the `default` operator is used, the compiler allocates memory for the value and copies the precomputed default into it.

Key characteristics:
- **100% concrete type coverage**: All concrete types (structs and classes) implement default. Most types have their default value **inferred** automatically from field initializers and zero-initialization rules. An explicit `default { }` block is only required when interface/trait fields need a concrete type binding.
- **Inferred defaults**: If every field of a type has a defaultable type (primitives, strings, or other defaultable structs/classes), the type's default value is inferred automatically — no explicit `default { }` block is needed. Field initializers contribute to the inferred default value.
- **Explicit default blocks**: An explicit `default { }` block is **required** when the type has interface or trait fields that need a concrete type binding. It is **optional** for overriding inferred default values.
- **Type bindings**: Inside a `default { }` block, interface/trait fields specify their concrete backing type (e.g., `someField: MyClass` or `someField: MyClass { x = 42 }`).
- **Compile-time computation**: The default value is fully resolved at compile time. Field initializers and default block overrides are evaluated in a comptime context.
- **Field initializers and zero-initialization**: Fields with initializers use their declared initial values. Fields without initializers are zero-initialized (numeric types get `0`, booleans get `false`, chars get `'\0'`, strings get `""`).
- **No null**: Mist does not have `null`. The `default` operator always produces a concrete value, never a null reference.
- **Pointer model for reference types**: Class fields are stored as pointers in the containing type's layout. The pointed-to default instance is allocated separately. This means the containing type's layout is always fixed-size (pointer-sized for reference-type fields).
- **Two expression forms**: `default(TypeName)` with an explicit type, and bare `default` where the type is inferred from context.
- **Inheritance**: Derived types inherit base class default values for the base's fields and only need to handle their own new fields. Derived types can override inherited defaults.
- **No constructor invocation**: The `default` block does not invoke any constructor. It directly specifies the field values that constitute the default.

Examples:
```mist
// Inferred default — no explicit block needed
public class Point {
    int x;        // zero-initialized to 0
    int y = 7;    // field initializer provides 7
}
Point p = default(Point);  // p.x == 0, p.y == 7

// Explicit default block — required for interface field binding
public class Widget {
    int id;
    IRenderer renderer;

    default {
        renderer: OpenGLRenderer
    }
}

// Explicit default block — optional override of inferred values
public class Config {
    int timeout = 30;
    int retries = 3;

    default {
        timeout = 60   // overrides the field initializer
    }
}

// Type binding with nested field overrides
public class App {
    ILogger logger;

    default {
        logger: ConsoleLogger { level = 2 }
    }
}

// Bare default expression (type inferred from context)
int x = default;           // inferred as default(int)
Point p = default;         // inferred as default(Point)
var y = default(int);      // explicit type required with var
// var z = default;        // ERROR: neither side provides a type
```

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in two passes: Pass 1 registers types and members, Pass 2 resolves type references and expressions.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Default_Block**: A member-level declaration inside a class or struct of the form `default { ... }` that optionally overrides inferred default values or provides required type bindings for interface/trait fields. The block body is evaluated at compile time.
- **Inferred_Default**: A default value that the Compiler computes automatically for a concrete type when all of the type's fields have defaultable types. No explicit Default_Block is needed for an Inferred_Default.
- **Explicit_Default**: A default value defined by an explicit Default_Block. An Explicit_Default is required when a type has interface or trait fields that need a Type_Binding, and is optional for overriding Inferred_Default values.
- **Type_Binding**: A declaration inside a Default_Block that specifies the concrete type to use for an interface or trait field, written as `fieldName: ConcreteType` or `fieldName: ConcreteType { fieldOverrides }`.
- **Default_Expression**: An expression of the form `default(TypeName)` that produces the compile-time default value of the named type.
- **Default_Literal_Expression**: An expression consisting of the bare `default` keyword whose type is inferred from the surrounding context (e.g., left-hand side of an assignment, return type, parameter type).
- **Comptime_Value**: A value that is fully resolved at compile time. The default value of a type is a Comptime_Value — all field values are known constants before the program runs.
- **Zero_Initialization**: The process of assigning the zero value to fields that lack an explicit Field_Initializer: `0` for numeric types, `false` for `bool`, `'\0'` for `char`, `""` for `string`.
- **Field_Initializer**: An expression assigned to a field at its declaration site (e.g., `int y = 7;`). Field initializers contribute to the default value of the containing type.
- **Fixed_Size_Type**: A type whose memory layout size is known at compile time. All primitive types are fixed-size. Reference-type fields (classes, interfaces, traits) are pointer-sized in the containing type's layout, so the containing type's layout is always fixed-size.
- **Defaultable_Type**: A type that supports the `default` operator. All primitive types, `string`, and concrete types (classes and structs) whose fields are all Defaultable_Types are Defaultable_Types. Interface and trait types are not Defaultable_Types on their own — they require a Type_Binding in a Default_Block.
- **ClassType**: The semantic model representation of a user-defined class type.
- **StructType**: The semantic model representation of a user-defined struct type.
- **InterfaceType**: The semantic model representation of a user-defined interface type.
- **TraitType**: The semantic model representation of a user-defined trait type.
- **TypeParameterType**: The semantic model representation of an unconstrained generic type parameter.
- **Member_Scope**: The scope containing all declared members (fields, methods, constructors, properties) of a type.

## Requirements

### Requirement 1: Default Keyword Token

**User Story:** As a Mist developer, I want the compiler to recognize `default` as a keyword, so that it can be used for default blocks and default expressions.

#### Acceptance Criteria

1. THE Grammar SHALL define a `DEFAULT` keyword token with the value `"default"`.
2. THE Grammar SHALL place the `DEFAULT` keyword token in the `keywords.storm` file alongside existing keyword definitions.
3. WHEN the lexer encounters the identifier `default` in source text, THE Compiler SHALL produce a `DEFAULT` token rather than an `IDENTIFIER` token.

### Requirement 2: Default Block Grammar

**User Story:** As a Mist developer, I want to declare a `default` block inside a class or struct, so that I can provide type bindings for interface/trait fields or override inferred default values.

#### Acceptance Criteria

1. THE Grammar SHALL define a `DefaultBlockDeclaration` rule that consists of the `DEFAULT` keyword, a left brace, an optional list of `DefaultFieldInitializer` entries, and a right brace.
2. THE Grammar SHALL define a `DefaultFieldInitializer` rule that supports two forms: a simple field override (`fieldName = Expression`) and a type binding (`fieldName : TypeReference`) optionally followed by a brace-enclosed list of nested field overrides (`{ fieldName = Expression, ... }`).
3. THE Grammar SHALL include `DefaultBlockDeclaration` as a variant in the `MemberDeclaration` rule alongside existing member kinds (constructors, methods, fields, properties, operators, cast operators, and indexers).
4. WHEN a default block is parsed inside a class declaration, THE Parser SHALL produce an AST node that represents the default block as a member of the class, including any field initializer overrides and type bindings.
5. WHEN a default block is parsed inside a struct declaration, THE Parser SHALL produce an AST node that represents the default block as a member of the struct, including any field initializer overrides and type bindings.
6. WHEN a default block appears inside an interface declaration, THE Parser SHALL reject it as a syntax error.
7. WHEN a default block appears inside a trait declaration, THE Parser SHALL reject it as a syntax error.

### Requirement 3: Default Expression Grammar

**User Story:** As a Mist developer, I want to write `default(TypeName)` and bare `default` expressions, so that I can obtain the default value of a type either by naming it explicitly or by letting the compiler infer it from context.

#### Acceptance Criteria

1. THE Grammar SHALL define a `DefaultExpression` rule that consists of the `DEFAULT` keyword, a left parenthesis, a type reference, and a right parenthesis.
2. THE Grammar SHALL define a `DefaultLiteralExpression` rule that consists of the bare `DEFAULT` keyword used as an expression without parentheses or a type argument.
3. THE Grammar SHALL include `DefaultExpression` and `DefaultLiteralExpression` as variants in the `Primary` expression rule alongside existing primary expressions.
4. WHEN the `default` keyword is followed by a left parenthesis, THE Parser SHALL parse it as a DefaultExpression that captures the type reference argument.
5. WHEN the `default` keyword appears in an expression context and is not followed by a left parenthesis or a left brace, THE Parser SHALL parse it as a DefaultLiteralExpression.

### Requirement 4: Pretty-Printer Support

**User Story:** As a Mist developer, I want the pretty-printer to correctly format default blocks, default expressions, and default literal expressions, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format an empty default block declaration as `default { }` with proper indentation inside the type body, consistent with other member declarations.
2. THE Pretty_Printer SHALL format a default block containing field overrides and type bindings with each entry on its own line, indented within the braces.
3. THE Pretty_Printer SHALL format a default expression as `default(TypeReference)`.
4. THE Pretty_Printer SHALL format a default literal expression as `default`.
5. FOR ALL valid Mist ASTs containing default blocks, default expressions, and default literal expressions, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 5: Inferred and Explicit Default Registration

**User Story:** As a Mist developer, I want the compiler to automatically determine which types are defaultable and whether their default is inferred or explicit, so that I only need to write explicit default blocks when interface/trait fields require type bindings.

#### Acceptance Criteria

1. WHEN a class or struct has no Default_Block and all of its fields have Defaultable_Types, THE Semantic_Model_Builder SHALL record the type as having an Inferred_Default.
2. WHEN a class or struct has an explicit Default_Block, THE Semantic_Model_Builder SHALL record the type as having an Explicit_Default.
3. WHEN a class or struct has no Default_Block and at least one field has a non-defaultable type (an interface or trait type without a Type_Binding), THE Semantic_Model_Builder SHALL record the type as not defaultable and SHALL emit a diagnostic error naming the field and explaining that a Default_Block with a Type_Binding is required.
4. WHEN a class or struct declaration contains more than one Default_Block, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that a type may declare at most one default block.
5. THE Semantic_Model_Builder SHALL make the defaultability status (inferred, explicit, or not defaultable) of each type queryable by the Type_Checker and Emitter.
6. THE Semantic_Model_Builder SHALL treat all primitive types (`int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`, `bool`, `char`) and `string` as intrinsically defaultable.

### Requirement 6: Default Value Computation

**User Story:** As a Mist developer, I want the compiler to compute the correct default value for each defaultable type, accounting for field initializers, zero-initialization, type bindings, nested overrides, and inheritance, so that `default(T)` produces the expected value.

#### Acceptance Criteria

1. WHEN computing the Inferred_Default for a type, THE Compiler SHALL use the declared Field_Initializer value for each field that has one.
2. WHEN computing the Inferred_Default for a type, THE Compiler SHALL apply Zero_Initialization to each field that lacks a Field_Initializer: `0` for integer types (`int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`), `0.0` for floating-point types (`float`, `double`, `decimal`), `false` for `bool`, `'\0'` for `char`, and `""` for `string`.
3. WHEN a field's type is itself a defaultable class or struct, THE Compiler SHALL use that nested type's default value as the field's default value.
4. WHEN an Explicit_Default block contains a field override (`fieldName = Expression`), THE Compiler SHALL use the override value instead of the field's initializer or zero-initialization value.
5. WHEN an Explicit_Default block contains a Type_Binding for an interface or trait field (`fieldName: ConcreteType`), THE Compiler SHALL use the default value of the specified ConcreteType as the field's default value.
6. WHEN an Explicit_Default block contains a Type_Binding with nested field overrides (`fieldName: ConcreteType { field = value }`), THE Compiler SHALL compute the default value of ConcreteType and then apply the nested field overrides to produce the field's default value.
7. WHEN a derived class inherits from a base class, THE Compiler SHALL inherit the base class's default field values for all base fields and SHALL compute default values only for the derived class's own new fields.
8. WHEN a derived class has an Explicit_Default block that overrides a base field, THE Compiler SHALL use the override value for that base field instead of the inherited value, subject to visibility restrictions (Requirement 10).
9. THE Compiler SHALL evaluate all Field_Initializer expressions and default block override expressions as compile-time constants; WHEN an expression is not a compile-time constant, THE Compiler SHALL emit a diagnostic error indicating that default values require compile-time constant expressions.
10. THE Compiler SHALL compute default values in topological order of type dependencies, ensuring that a type's default value is computed before any type that references it as a field type.

### Requirement 7: Default Expression Type Checking

**User Story:** As a Mist developer, I want the type checker to validate default expressions and default literal expressions, so that I get clear errors when misusing the default keyword.

#### Acceptance Criteria

1. WHEN a `default(TypeName)` expression is encountered, THE Type_Checker SHALL resolve the type reference to a ClassType or StructType and SHALL verify that the resolved type is defaultable.
2. WHEN the resolved type in a `default(TypeName)` expression is defaultable, THE Type_Checker SHALL treat the expression as valid and assign it the resolved type.
3. WHEN the resolved type in a `default(TypeName)` expression is not defaultable, THE Type_Checker SHALL emit a diagnostic error indicating that the type does not support the `default` operator and explaining why (e.g., it has an interface field without a type binding).
4. WHEN `default(TypeName)` is used with an InterfaceType, THE Type_Checker SHALL emit a diagnostic error indicating that interfaces do not support the `default` operator.
5. WHEN `default(TypeName)` is used with a TraitType, THE Type_Checker SHALL emit a diagnostic error indicating that traits do not support the `default` operator.
6. WHEN `default(TypeName)` is used with a TypeParameterType (unconstrained generic), THE Type_Checker SHALL emit a diagnostic error indicating that `default` on unconstrained generic type parameters is not supported.
7. WHEN the type reference in a `default(TypeName)` expression cannot be resolved to a known type, THE Type_Checker SHALL emit a diagnostic error indicating the type is undefined.
8. WHEN a bare `default` literal expression appears in a context where the target type is known (left-hand side of a typed variable declaration, return type of a method, parameter type of a method call), THE Type_Checker SHALL infer the type from that context and treat the expression as equivalent to `default(InferredType)`.
9. WHEN a bare `default` literal expression appears in a `var` declaration (`var x = default;`), THE Type_Checker SHALL emit a diagnostic error indicating that the type cannot be inferred because neither side provides a type.
10. WHEN a bare `default` literal expression appears in a context where no target type can be inferred, THE Type_Checker SHALL emit a diagnostic error indicating that the type of `default` cannot be determined.

### Requirement 8: Layout and Pointer Model

**User Story:** As a Mist developer, I want the compiler to handle reference-type fields as pointers in the containing type's layout, so that all concrete types have a fixed-size layout and circular references are supported.

#### Acceptance Criteria

1. THE Compiler SHALL treat all primitive types (`int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`, `bool`, `char`) as Fixed_Size_Types.
2. THE Compiler SHALL treat `string` fields as pointer-sized in the containing type's layout, with the default value being an empty string (`""`) allocated in the string pool at comptime.
3. THE Compiler SHALL treat class-typed fields as pointer-sized in the containing type's layout, with the pointed-to default instance allocated separately.
4. THE Compiler SHALL treat interface-typed and trait-typed fields as pointer-sized in the containing type's layout, with the pointed-to default instance determined by the Type_Binding in the Default_Block.
5. THE Compiler SHALL treat a struct or class that contains only fields with known fixed-size types (including pointer-sized reference-type fields) as a Fixed_Size_Type.
6. THE Compiler SHALL compute default values in topological order of type dependencies to ensure that referenced types' defaults are available before they are needed, and SHALL handle circular references between class types because class fields are pointer-sized and heap-allocated.

### Requirement 9: Visibility Restrictions in Default Blocks

**User Story:** As a Mist developer, I want the compiler to enforce visibility rules in default blocks, so that a derived type's default block cannot set fields it does not have access to.

#### Acceptance Criteria

1. WHEN a Default_Block contains a field override or Type_Binding, THE Compiler SHALL verify that the referenced field is visible to the type declaring the Default_Block.
2. WHEN a derived type's Default_Block attempts to override a private field of a base type, THE Compiler SHALL emit a diagnostic error indicating that the field is not accessible from the derived type's default block.
3. THE Compiler SHALL allow a Default_Block to override public fields of the declaring type and its base types.

### Requirement 10: Inheritance of Default Values

**User Story:** As a Mist developer, I want derived types to automatically inherit default values from their base class, so that I only need to specify defaults for new fields introduced by the derived type.

#### Acceptance Criteria

1. WHEN a derived class inherits from a base class that is defaultable, THE Compiler SHALL use the base class's default field values for all inherited fields in the derived class's default value.
2. WHEN a derived class introduces new fields that are all defaultable, THE Compiler SHALL compute the derived class's default value by combining inherited base defaults with inferred defaults for the new fields.
3. WHEN a derived class has an Explicit_Default block, THE Compiler SHALL allow the block to override inherited base field values (subject to visibility restrictions in Requirement 9) and to provide Type_Bindings or overrides for the derived class's own fields.
4. WHEN a derived class inherits from a base class that is not defaultable, THE Compiler SHALL record the derived class as not defaultable and SHALL emit a diagnostic error indicating that the base class must be defaultable.

### Requirement 11: Redundant Default Block Diagnostic

**User Story:** As a Mist developer, I want the compiler to inform me when an explicit default block is redundant, so that I can simplify my code by removing unnecessary blocks.

#### Acceptance Criteria

1. WHEN an Explicit_Default block produces a default value identical to what the Inferred_Default would produce (i.e., the block is empty or all overrides match the inferred values), THE Compiler SHALL emit an informational diagnostic indicating that the default block is redundant and can be removed.
2. THE Compiler SHALL classify the redundant default block diagnostic as an informational severity, not a warning or error.

### Requirement 12: C# Code Emission for Defaults

**User Story:** As a Mist developer, I want the transpiled C# code to correctly implement default value semantics for both inferred and explicit defaults, so that the compiled output produces the correct default values.

#### Acceptance Criteria

1. WHEN any type (class or struct) is defaultable (whether by Inferred_Default or Explicit_Default), THE Emitter SHALL emit a static factory method named `__CreateDefault` on the C# type that creates and returns a new instance with the computed default values on each call.
2. THE Emitter SHALL initialize the factory method body using the computed default value: field initializer values for fields that have them, zero values for fields that do not, and type-binding-resolved values for interface/trait fields.
3. WHEN a `default(TypeName)` expression or a bare `default` literal expression is encountered for any defaultable type (class or struct), THE Emitter SHALL emit a call to the `__CreateDefault()` factory method of the corresponding C# type.
4. THE Emitter SHALL NOT emit code that causes multiple `default(T)` evaluations to return the same object reference for any type that has reference-type fields.
5. THE Emitter SHALL ensure the emitted factory method does not invoke any user-defined constructor.
6. WHEN a class type is defaultable, THE Emitter SHALL emit a private parameterless constructor (if one does not already exist) to support creating the default instance without requiring a public constructor.
7. WHEN a derived type inherits default values from a base type, THE Emitter SHALL emit the factory method initialization so that inherited field values match the base type's defaults, with any overrides from the derived type's Default_Block applied.
8. WHEN a type has interface or trait fields with Type_Bindings, THE Emitter SHALL emit initialization code that calls the concrete backing type's `__CreateDefault()` factory method and assigns the result to the interface/trait field.
9. WHEN any type has a field whose type is a class (or a struct containing reference-type fields), THE Emitter SHALL emit the factory method so that it recursively calls `__CreateDefault()` for those nested fields, ensuring no shared references between independently-created default instances.

### Requirement 13: Diagnostic Messages

**User Story:** As a Mist developer, I want clear and specific error messages when I misuse the `default` keyword, so that I can quickly identify and fix the issue.

#### Acceptance Criteria

1. WHEN `default(TypeName)` is used on a type that is not defaultable, THE Compiler SHALL emit a diagnostic that names the type and explains why it is not defaultable (e.g., it has an interface field without a type binding).
2. WHEN a type declares more than one Default_Block, THE Compiler SHALL emit a diagnostic that names the type and states that only one default block is permitted per type.
3. WHEN a Default_Block references a field that is not visible to the declaring type, THE Compiler SHALL emit a diagnostic that names the field and states it is not accessible.
4. WHEN a field override or Field_Initializer expression in a default context is not a compile-time constant, THE Compiler SHALL emit a diagnostic that names the field and states that default values require compile-time constant expressions.
5. WHEN `default(TypeName)` references an undefined type, THE Compiler SHALL emit a diagnostic stating the type is undefined.
6. WHEN `default(TypeName)` is used with an InterfaceType or TraitType, THE Compiler SHALL emit a diagnostic stating that the `default` operator is only supported on concrete types (classes and structs).
7. WHEN `default(TypeName)` is used with a TypeParameterType (unconstrained generic), THE Compiler SHALL emit a diagnostic stating that `default` on unconstrained generic type parameters is not supported.
8. WHEN `var x = default;` is encountered, THE Compiler SHALL emit a diagnostic stating that the type of `default` cannot be inferred because neither side provides a type.
9. WHEN a bare `default` literal expression appears in a context where no target type can be inferred, THE Compiler SHALL emit a diagnostic stating that the type of `default` cannot be determined from context.
10. WHEN an Explicit_Default block is redundant (identical to the Inferred_Default), THE Compiler SHALL emit an informational diagnostic suggesting the block can be removed.
11. WHEN a type has an interface or trait field without a Type_Binding and no Default_Block, THE Compiler SHALL emit a diagnostic that names the field and states that a default block with a type binding is required.
12. WHEN a derived type's base class is not defaultable, THE Compiler SHALL emit a diagnostic stating that the derived type cannot be defaultable because its base class is not defaultable.

### Requirement 14: Syntax Highlighting

**User Story:** As a Mist developer using an editor, I want the `default` keyword to be syntax-highlighted, so that default blocks and default expressions are visually consistent with other language constructs.

#### Acceptance Criteria

1. THE TextMate grammar (`mist.tmLanguage.json`) SHALL include `default` in the keyword patterns so it is highlighted as a keyword.
2. WHEN a Mist source file contains the `default` keyword, THE editor SHALL highlight it with the same style as other keywords like `class`, `struct`, and `return`.

### Requirement 15: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented default keyword feature, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the default keyword feature is implemented, THE Compiler team SHALL update the `Expressions — Literals` section of CSHARP_FEATURES.md to mark `default` / `default(T)` as ✅ Implemented with a note describing Mist's divergence from C# semantics (inferred defaults, type bindings, bare default literal).
2. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation counts.

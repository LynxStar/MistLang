# Requirements Document

## Introduction

This document specifies the requirements for adding enum declarations to the Mist programming language. An enum in Mist is a named, nominal collection of static values that wraps an underlying type. Unlike C# enums (restricted to integral primitives), Mist enums can wrap any valid type — primitives, classes, structs, or union types. Enums are top-level constructs alongside classes, structs, interfaces, and traits.

## Glossary

- **Enum_Declaration**: A top-level language construct that defines a named set of static members, each holding a value of the underlying type.
- **Underlying_Type**: The type that all enum member values conform to. May be explicitly annotated or inferred from member values.
- **Enum_Member**: A named constant within an enum declaration, consisting of an identifier and optionally an explicit value.
- **Name_Only_Enum**: An enum where members have no explicit values; sequential byte values starting from 0 are auto-assigned.
- **Parser**: The Storm-generated parser that reads Mist source and produces a concrete syntax tree.
- **Semantic_Model**: The internal representation of resolved types, symbols, and their relationships after semantic analysis.
- **CSharp_Emitter**: The backend that translates the Mist semantic model into C# source code.
- **Type_Inference_Engine**: The component that determines types when no explicit annotation is provided.
- **Pattern_Matcher**: The component that evaluates `is` expressions for value equality checks.

## Requirements

### Requirement 1: Enum Grammar Definition

**User Story:** As a language developer, I want the Storm grammar to recognize enum declarations, so that the parser can produce a syntax tree for enum source code.

#### Acceptance Criteria

1. WHEN a source file contains `enum Identifier : TypeReference { members }`, THE Parser SHALL produce an EnumDeclaration node with the name, explicit type annotation, and member list.
2. WHEN a source file contains `enum Identifier { members }` without a type annotation, THE Parser SHALL produce an EnumDeclaration node with no explicit type and the member list.
3. WHEN an enum member is declared as `Identifier: Expression`, THE Parser SHALL produce an EnumMember node with both the name and the value expression.
4. WHEN an enum member is declared as a bare `Identifier` without a value, THE Parser SHALL produce an EnumMember node with the name and no value expression.
5. WHEN multiple enum members are present, THE Parser SHALL accept comma-separated members with an optional trailing comma.
6. WHEN access modifiers precede the `enum` keyword, THE Parser SHALL attach those modifiers to the EnumDeclaration node.

### Requirement 2: Enum Semantic Type

**User Story:** As a language developer, I want enums to be represented as a distinct nominal type in the semantic model, so that the type system can distinguish enums from their underlying types.

#### Acceptance Criteria

1. THE Semantic_Model SHALL represent each enum declaration as an EnumType that implements IType.
2. THE EnumType SHALL store the enum name, the resolved underlying type, and the list of enum members with their resolved values.
3. THE EnumType SHALL have a TypeKind of Enum (a new value added to the TypeKind enumeration).
4. WHEN two EnumType instances have different names, THE Semantic_Model SHALL treat them as distinct types even if their underlying types are identical.

### Requirement 3: Underlying Type Inference

**User Story:** As a Mist developer, I want the compiler to infer the underlying type when I omit the type annotation, so that I can write concise enum declarations.

#### Acceptance Criteria

1. WHEN all enum member values have the same type, THE Type_Inference_Engine SHALL infer that type as the underlying type.
2. WHEN enum member values have different types, THE Type_Inference_Engine SHALL infer a union type containing all distinct member value types as the underlying type.
3. WHEN an enum has no explicit values (name-only enum), THE Type_Inference_Engine SHALL infer `byte` as the underlying type.
4. WHEN an explicit type annotation is provided, THE Type_Inference_Engine SHALL use the annotated type as the underlying type without performing inference.
5. WHEN an explicit type annotation is provided, THE Semantic_Model SHALL verify that every member value is assignable to the annotated type.
6. IF any member value is not assignable to the explicitly annotated underlying type, THEN THE Semantic_Model SHALL report a type error identifying the non-conforming member.
7. IF a member value is not assignable to an inferred underlying type, THEN THE Semantic_Model SHALL report a type error.

### Requirement 4: Name-Only Enum Value Assignment

**User Story:** As a Mist developer, I want name-only enum members to receive sequential values automatically, so that I can define simple enumerations without specifying values.

#### Acceptance Criteria

1. WHEN an enum member has no explicit value and is the first member, THE Semantic_Model SHALL assign it the value 0.
2. WHEN an enum member has no explicit value and follows another member, THE Semantic_Model SHALL assign it the value of the previous member plus 1.
3. WHEN an enum member has an explicit value and the next member has no explicit value, THE Semantic_Model SHALL assign the next member the explicit value plus 1.
4. WHEN two enum members are assigned the same value (either explicitly or through sequencing), THE Semantic_Model SHALL accept the declaration without error.

### Requirement 5: Enum Member Access

**User Story:** As a Mist developer, I want to access enum members using dot notation, so that I can reference specific enum values in my code.

#### Acceptance Criteria

1. WHEN an expression uses `EnumName.MemberName`, THE Semantic_Model SHALL resolve it to the corresponding enum member value with the EnumType as its type.
2. IF `MemberName` does not exist in the enum, THEN THE Semantic_Model SHALL report an error indicating the member is not defined.
3. THE Semantic_Model SHALL treat enum member access as a static member access on the enum type.

### Requirement 6: Enum as a Type

**User Story:** As a Mist developer, I want to use enum names as types in parameters, return types, fields, and variables, so that I can write type-safe code with enums.

#### Acceptance Criteria

1. WHEN an enum name is used as a type annotation on a variable, THE Semantic_Model SHALL accept it as a valid type reference.
2. WHEN an enum name is used as a parameter type, THE Semantic_Model SHALL accept it as a valid type reference.
3. WHEN an enum name is used as a method return type, THE Semantic_Model SHALL accept it as a valid type reference.
4. WHEN an enum name is used as a field type, THE Semantic_Model SHALL accept it as a valid type reference.

### Requirement 7: Implicit Conversion to Underlying Type

**User Story:** As a Mist developer, I want enum values to implicitly convert to their underlying type, so that I can use them where the underlying type is expected without explicit casting.

#### Acceptance Criteria

1. WHEN an enum value is assigned to a variable of the underlying type, THE Semantic_Model SHALL accept the assignment without error.
2. WHEN an enum value is passed as an argument where the underlying type is expected, THE Semantic_Model SHALL accept the call without error.
3. WHEN a value of the underlying type is assigned to a variable of the enum type, THE Semantic_Model SHALL report a type error (reverse conversion is not supported).

### Requirement 8: Pattern Matching with Enum Values

**User Story:** As a Mist developer, I want to use `is` expressions to check if a value matches a specific enum member, so that I can write conditional logic based on enum values.

#### Acceptance Criteria

1. WHEN an expression `x is EnumName.MemberName` is evaluated, THE Pattern_Matcher SHALL perform a value equality check between `x` and the enum member's value.
2. WHEN the value of `x` equals the enum member's value, THE Pattern_Matcher SHALL evaluate the `is` expression to true.
3. WHEN the value of `x` does not equal the enum member's value, THE Pattern_Matcher SHALL evaluate the `is` expression to false.

### Requirement 9: C# Emission as Record Class

**User Story:** As a language developer, I want enums to emit as C# record classes, so that the generated code has value equality semantics and static member access.

#### Acceptance Criteria

1. WHEN an enum is emitted, THE CSharp_Emitter SHALL generate a `record class` with a primary constructor accepting the underlying type as `Value`.
2. WHEN an enum is emitted, THE CSharp_Emitter SHALL generate a `public static readonly` field for each enum member initialized with the member's value.
3. WHEN an enum is emitted, THE CSharp_Emitter SHALL generate an implicit conversion operator from the enum type to the underlying type that returns the `Value` property.
4. WHEN the enum has access modifiers, THE CSharp_Emitter SHALL apply the corresponding C# access modifier to the generated record class.
5. WHEN the underlying type is a union type, THE CSharp_Emitter SHALL use the existing union helper struct name as the `Value` type in the record class.

### Requirement 10: Enum Visibility

**User Story:** As a Mist developer, I want to control the visibility of my enum declarations, so that I can encapsulate implementation details.

#### Acceptance Criteria

1. WHEN an enum is declared with the `public` modifier, THE CSharp_Emitter SHALL emit the record class with `public` access.
2. WHEN an enum is declared with the `private` modifier, THE CSharp_Emitter SHALL emit the record class with `private` access.
3. WHEN an enum is declared without an access modifier, THE CSharp_Emitter SHALL emit the record class with `internal` access (matching the default for other Mist types).
4. THE Semantic_Model SHALL treat all enum members as publicly accessible regardless of the enum's own visibility modifier.

### Requirement 11: Enum Keyword Registration

**User Story:** As a language developer, I want `enum` to be a reserved keyword, so that the lexer correctly tokenizes it and it cannot be used as an identifier.

#### Acceptance Criteria

1. THE Parser SHALL recognize `enum` as a reserved keyword token.
2. WHEN `enum` appears in identifier position, THE Parser SHALL report a syntax error.

### Requirement 12: Round-Trip Parsing

**User Story:** As a language developer, I want enum declarations to survive a parse-then-print cycle, so that tooling can reliably reconstruct source from the syntax tree.

#### Acceptance Criteria

1. FOR ALL valid enum declarations, parsing the source then printing the syntax tree back to source then parsing again SHALL produce an equivalent syntax tree (round-trip property).

### Requirement 13: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want `enum` declarations to be syntax-highlighted correctly, so that I get the same visual cues as other type declarations.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `enum` as a declaration keyword (scope `keyword.declaration.mist`).
2. THE TextMate grammar SHALL highlight the identifier following `enum` as a type name (scope `entity.name.type.enum.mist`).
3. THE TextMate grammar SHALL highlight enum member names appropriately within the enum body.

### Requirement 14: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating enums, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/Enums/Program.mist`.
2. The sample SHALL demonstrate every syntax variant and usage pattern defined in the preceding requirements, including but not limited to: enum with explicit type annotation, enum without type annotation (inferred), name-only enum with auto-assigned values, enum with mixed explicit and implicit values, enum with union underlying type, enum member access via dot notation, enum used as variable type, enum used as parameter type, enum used as return type, enum used as field type, implicit conversion to underlying type, pattern matching with `is` expressions, public/private/default visibility modifiers, and trailing comma usage.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `Enums.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

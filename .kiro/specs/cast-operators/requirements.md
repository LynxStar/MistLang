# Requirements Document

## Introduction

This document specifies the requirements for adding implicit and explicit cast operators to the Mist programming language. Cast operators allow user-defined types to define conversions to and from other types, following C#'s `implicit operator` and `explicit operator` syntax.

Mist's cast operators differ from C# in one critical way: **explicit casts are only valid when the cast operator can be statically proven to exist on the compile-time type of the expression**. A cast operator defined on a derived type does not become available when the variable is typed as a base type. This eliminates all runtime cast failures by design — every cast is verified at compile time against the declared type, not the runtime type.

Example:
```
class Banana : Fruit {
    public static explicit operator Weight(Banana b) { ... }
}

var b = new Banana();
Fruit f = b;
var w1 = (Weight)b;   // Valid — Banana declares explicit operator Weight
var w2 = (Weight)f;   // Compile error — Fruit has no explicit operator Weight
```

Key characteristics:
- **Implicit cast operators**: Conversions that happen automatically during assignment, argument passing, and return statements without explicit syntax.
- **Explicit cast operators**: Conversions that require the `(TargetType)expression` cast syntax.
- **Compile-time safety**: Explicit casts are resolved against the compile-time (static) type of the expression only. Cast operators on derived types are not inherited or visible through base type variables.
- **No runtime cast errors**: Because all cast validity is checked at compile time, there are no runtime cast failures.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in two passes: Pass 1 registers types and members, Pass 2 resolves type references and expressions.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Cast_Operator_Declaration**: A member declaration inside a class or struct that defines a conversion. Syntax: `modifiers implicit operator TargetType(SourceType param) body` or `modifiers explicit operator TargetType(SourceType param) body`.
- **Implicit_Cast**: A conversion that the Compiler applies automatically when the source type has an `implicit operator` targeting the destination type. No explicit cast syntax is required.
- **Explicit_Cast**: A conversion that requires the programmer to write `(TargetType)expression`. The Compiler verifies that the expression could be passed as an argument to an `explicit operator` (or `implicit operator`) that converts to the target type (Requirement 8).
- **Lossless_Conversion**: A conversion that changes no data, and so never counts as a user-declared conversion: subsumption (a derived type to its base, `T` to `T | void`, a constituent to its union, a refinement to its base, anything to `unknown`), a Standard `intrinsic` conversion operator, an enum to its underlying value, and a newtype to a source whose copy cannot be told from the original (newtypes spec Requirement 2.4). A declared cast operator is never lossless, since it cannot promise that its body changes nothing.
- **Cast_Expression**: The expression syntax `(TargetType)expression` that invokes an explicit (or implicit) cast operator.
- **Compile_Time_Type**: The static type of an expression as determined by the Compiler during type checking, as opposed to the runtime type. Cast operator lookup uses only the Compile_Time_Type.
- **Enclosing_Type**: The class or struct in which a Cast_Operator_Declaration is declared. Either the source parameter type or the return type of a cast operator must be the Enclosing_Type.
- **ClassType**: The semantic model representation of a user-defined class type.
- **StructType**: The semantic model representation of a user-defined struct type.
- **OperatorDeclaration**: The existing grammar rule for arithmetic and comparison operator overloads.
- **IsAssignable**: The method in the Type_Checker that determines whether a source type can be assigned to a target type.

## Requirements

### Requirement 1: Cast Operator Keywords

**User Story:** As a Mist developer, I want `implicit` and `explicit` keywords available in the language, so that I can declare cast operators on my types.

#### Acceptance Criteria

1. THE Grammar SHALL define an `IMPLICIT` keyword token with the value `"implicit"`.
2. THE Grammar SHALL define an `EXPLICIT` keyword token with the value `"explicit"`.
3. THE Grammar SHALL place the `IMPLICIT` and `EXPLICIT` keyword tokens in `keywords.storm` alongside the existing keyword definitions.

### Requirement 2: Cast Operator Declaration Grammar

**User Story:** As a Mist developer, I want to declare implicit and explicit cast operators inside my classes and structs, so that I can define custom type conversions.

#### Acceptance Criteria

1. THE Grammar SHALL define a `CastOperatorDeclaration` rule that consists of zero or more modifiers, a cast kind (`IMPLICIT` or `EXPLICIT`), the `OPERATOR` keyword, a target type reference, a left parenthesis, a single parameter, a right parenthesis, and a method body.
2. THE Grammar SHALL define a `CastOperatorKind` rule with two variants: `ImplicitCast` (using the `IMPLICIT` keyword) and `ExplicitCast` (using the `EXPLICIT` keyword).
3. THE Grammar SHALL include `CastOperatorDeclaration` as a variant in the `MemberDeclaration` rule alongside `ConstructorMember`, `MethodMember`, `OperatorMember`, `PropertyMember`, `IndexerMember`, and `FieldMember`.
4. WHEN a cast operator declaration is parsed, THE Parser SHALL produce an AST node that captures the modifiers, the cast kind (implicit or explicit), the target type reference, the single parameter (type and name), and the method body.
5. WHEN a cast operator declaration has no body (semicolon-terminated), THE Parser SHALL produce an AST node with a no-body marker, consistent with the existing `OperatorNoBody` pattern for intrinsic operators.

### Requirement 3: Cast Expression Grammar

**User Story:** As a Mist developer, I want to write `(TargetType)expression` to explicitly cast a value, so that I can invoke explicit cast operators.

#### Acceptance Criteria

1. THE Grammar SHALL define a `CastExpression` rule that consists of a left parenthesis, a type reference, a right parenthesis, followed by a `Unary` expression.
2. THE Grammar SHALL include `CastExpression` as a variant in the `Unary` rule, at the same precedence level as `LogicalNot` and `Negate`.
3. WHEN a cast expression is parsed, THE Parser SHALL produce an AST node that captures the target type reference and the operand expression.
4. THE Parser SHALL disambiguate between a `CastExpression` and a `ParenthesizedExpression` by recognizing that a cast expression has a type reference inside the parentheses followed by a unary expression, while a parenthesized expression has a general expression inside the parentheses.

### Requirement 4: Pretty-Printer Support for Cast Operators

**User Story:** As a Mist developer, I want the pretty-printer to correctly format cast operator declarations and cast expressions, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format an implicit cast operator declaration as `implicit operator TargetType(SourceType param) { body }` with appropriate modifiers and indentation.
2. THE Pretty_Printer SHALL format an explicit cast operator declaration as `explicit operator TargetType(SourceType param) { body }` with appropriate modifiers and indentation.
3. THE Pretty_Printer SHALL format a cast expression as `(TargetType)expression` with no space between the closing parenthesis and the operand.
4. THE Pretty_Printer SHALL format a body-less cast operator declaration with a semicolon terminator, consistent with the existing `OperatorNoBody` formatting.
5. FOR ALL valid Mist ASTs containing cast operator declarations and cast expressions, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 5: Cast Operator Semantic Model Registration

**User Story:** As a Mist developer, I want the compiler to register cast operators in the semantic model, so that the type checker and emitter can look them up during compilation.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL create a symbol with `SymbolKind.Method` for each Cast_Operator_Declaration, storing the target type as the symbol's declared type (return type).
2. THE Semantic_Model_Builder SHALL register each cast operator symbol in the Enclosing_Type's member scope with a name that encodes the cast kind and target type (e.g., `"operator_implicit_Weight"` or `"operator_explicit_Weight"`).
3. THE Semantic_Model_Builder SHALL record whether each cast operator symbol is implicit or explicit, so that the Type_Checker can distinguish between the two kinds during resolution.
4. THE Semantic_Model_Builder SHALL register the single parameter of the cast operator as a `SymbolKind.Parameter` symbol within the cast operator's scope.

### Requirement 6: Cast Operator Declaration Validation

**User Story:** As a Mist developer, I want the compiler to validate my cast operator declarations, so that I receive clear errors for malformed or invalid declarations.

#### Acceptance Criteria

1. WHEN a Cast_Operator_Declaration is declared outside a class or struct (e.g., inside an interface or trait), THE Type_Checker SHALL emit a diagnostic error indicating that cast operators are only permitted in class and struct declarations.
2. WHEN a Cast_Operator_Declaration has a source parameter type that is not the Enclosing_Type and a target type that is also not the Enclosing_Type, THE Type_Checker SHALL emit a diagnostic error indicating that either the source or target type of a cast operator must be the Enclosing_Type.
3. WHEN a Cast_Operator_Declaration has more than one parameter, THE Type_Checker SHALL emit a diagnostic error indicating that cast operators must have exactly one parameter.
4. WHEN a Cast_Operator_Declaration has zero parameters, THE Type_Checker SHALL emit a diagnostic error indicating that cast operators must have exactly one parameter.
5. WHEN two Cast_Operator_Declarations in the same type have the same cast kind (both implicit or both explicit) and the same source-target type pair, THE Type_Checker SHALL emit a diagnostic error indicating a duplicate cast operator.
6. WHEN a non-intrinsic Cast_Operator_Declaration has no body, THE Type_Checker SHALL emit a diagnostic error indicating that a body is required, consistent with the existing operator body validation.
7. WHEN an intrinsic Cast_Operator_Declaration has a body, THE Type_Checker SHALL emit a diagnostic error indicating that intrinsic cast operators must not have a body, consistent with the existing intrinsic operator validation.

### Requirement 7: Implicit Cast Resolution in Type Checking

**User Story:** As a Mist developer, I want implicit casts to be applied automatically during assignment, argument passing, and return statements, so that I do not need to write explicit cast syntax for safe conversions.

An implicit conversion is the same call as an explicit cast (Requirement 8), written invisibly. Because the user cannot see it, it is the one user-declared conversion its value may pass through: user code the reader cannot see must not run in sequence.

#### Acceptance Criteria

1. WHEN the source type of an assignment is not directly assignable to the target type, THE Type_Checker SHALL search for an implicit cast operator on the Compile_Time_Type of the source expression that converts to the target type.
2. WHEN an implicit cast operator is found, THE Type_Checker SHALL treat the assignment as valid and record the resolved cast operator on the assignment node.
3. WHEN a method argument's type is not directly assignable to the parameter type, THE Type_Checker SHALL search for an implicit cast operator on the Compile_Time_Type of the argument that converts to the parameter type.
4. WHEN a return statement's expression type is not directly assignable to the method's return type, THE Type_Checker SHALL search for an implicit cast operator on the Compile_Time_Type of the expression that converts to the return type.
5. THE Type_Checker SHALL search for implicit cast operators on both the source type and the target type, consistent with C# semantics where either side can define the conversion.
6. WHEN multiple implicit cast operators are applicable for the same conversion, THE Type_Checker SHALL emit a diagnostic error indicating an ambiguous implicit conversion.
7. WHEN no implicit cast operator is found and the types are not directly assignable, THE Type_Checker SHALL emit the existing type mismatch diagnostic.
8. THE Type_Checker SHALL apply an implicit cast operator only when the source value reaches the operator's parameter, and the operator's result reaches the destination, by Lossless_Conversions alone. A value that would need a second user-declared cast operator -- `Feet f = reading;` where `Reading` converts implicitly to `Meters` and `Feet` declares `implicit operator Feet(Meters)` -- SHALL be refused; when the explicit cast of Requirement 8 would accept the same value, the diagnostic SHALL say that an explicit cast exists.

### Requirement 8: Explicit Cast Expression Type Checking

**User Story:** As a Mist developer, I want the compiler to validate explicit cast expressions against the compile-time type of the operand, so that I get compile-time errors instead of runtime failures.

A cast in Mist is sugar for calling a static conversion function: `(T)v` means `T.ExplicitCast(v)`, and finding the function to call is the desugaring, much as finding an extension method is. The declared cast operators that can convert to `T` are that function's overloads, and the operand is matched against their parameters **as an argument**, so choosing among them is ordinary overload resolution: two equally good candidates are an ambiguity, exactly as `Feet.Convert(reading)` would be. The written cast is the call itself, and its argument may take one user-declared implicit conversion of its own, so `(Feet)reading` is valid where `Reading` converts implicitly to `Meters` and `Feet` declares `explicit operator Feet(Meters)`.

#### Acceptance Criteria

1. WHEN a Cast_Expression is encountered, THE Type_Checker SHALL resolve the target type reference to an IType.
2. WHEN a Cast_Expression is encountered, THE Type_Checker SHALL determine the Compile_Time_Type of the operand expression.
3. THE Type_Checker SHALL take as candidates the explicit and implicit cast operators declared on the Compile_Time_Type of the operand and on its base types whose return type reaches the target type by a Lossless_Conversion.
4. THE Type_Checker SHALL also take as candidates the explicit and implicit cast operators declared on the target type and on its base types whose return type reaches the target type by a Lossless_Conversion.
5. WHEN a cast operator is selected, THE Type_Checker SHALL set the type of the Cast_Expression to the selected operator's return type, the return type of the function the cast calls, and record the selected cast operator on the cast expression node.
6. WHEN no candidate exists, or none accepts the operand as an argument, THE Type_Checker SHALL emit a diagnostic error indicating that no cast operator exists between the two types.
7. THE Type_Checker SHALL find candidates on the base types of the Compile_Time_Type, since a value is a valid argument to a parameter of its base type, and SHALL NOT search derived types, since a value of a base type is not a valid argument to a parameter of a derived type (Requirement 9.2).
8. WHEN the Compile_Time_Type is already assignable to the target type (identity or subtype), THE Type_Checker SHALL treat the cast expression as valid without requiring a cast operator, and set the expression type to the target type.
9. WHEN two or more candidates accept the operand and overload resolution finds none better than the others, THE Type_Checker SHALL emit a diagnostic error indicating an ambiguous cast that names the tied operators, and SHALL NOT choose one. Candidates declared with the same signature on both ends tie.
10. A candidate SHALL accept the operand exactly when the operand could be passed as an argument to the candidate's parameter: its Compile_Time_Type reaches the parameter type by Lossless_Conversions, possibly around one user-declared implicit cast operator, or it is an integer constant that fits an integral parameter type. An operand that would need a narrowing conversion or a second user-declared cast operator to reach the parameter -- `(Feet)sensor` where `Sensor` converts implicitly to `Reading` and `Reading` to `Meters` -- SHALL NOT be accepted.
11. Overload resolution among candidates that accept the operand SHALL prefer an operand whose type is the parameter type over one that converts to it, and otherwise the more specific parameter type, as the method-overloading spec's Requirement 3.3 defines it. Before that comparison, a candidate whose return type is the target type itself SHALL outrank one whose return type only widens into it, since it is the target type's own conversion: `(long)counter` calls `implicit operator long(Counter)` over `implicit operator int(Counter)`.
12. WHEN exactly one candidate exists and it does not accept the operand, THE Type_Checker SHALL emit a diagnostic error naming the operand's type, the target type, the operator and its parameter type, as for a call to a method with a single overload.
13. THE Type_Checker SHALL NOT refuse a cast because the emitted C# would need more than one user-defined conversion in a single cast. That limit is the target language's, not Mist's, and the Emitter lowers each step as a cast of its own.

### Requirement 9: Compile-Time-Only Cast Safety

**User Story:** As a Mist developer, I want the guarantee that all casts are validated at compile time against the static type, so that I never encounter a runtime cast failure.

What is decided at compile time is which conversion function a cast calls and that the operand may be passed to it. What the called operator's body then does is its own code, as the body of any called method is.

#### Acceptance Criteria

1. THE Type_Checker SHALL resolve cast operators using only the Compile_Time_Type of the operand expression, not any potential runtime type.
2. WHEN a variable is typed as a base class and the derived class defines a cast operator, THE Type_Checker SHALL NOT make that cast operator available through the base-typed variable.
3. WHEN a variable is typed as an interface and a class implementing the interface defines a cast operator, THE Type_Checker SHALL NOT make that cast operator available through the interface-typed variable.
4. THE Compiler SHALL produce no runtime type checks, runtime cast exceptions, or runtime cast failure paths for cast operator invocations; which operator a cast calls, and that its operand may be passed to that operator, are determined at compile time.

### Requirement 10: C# Code Emission for Cast Operator Declarations

**User Story:** As a Mist developer, I want the transpiled C# code to correctly emit cast operator declarations, so that the compiled output defines the conversions as standard C# implicit/explicit operators.

#### Acceptance Criteria

1. THE Emitter SHALL emit each implicit Cast_Operator_Declaration as a C# `public static implicit operator TargetType(SourceType param) { body }` method.
2. THE Emitter SHALL emit each explicit Cast_Operator_Declaration as a C# `public static explicit operator TargetType(SourceType param) { body }` method.
3. THE Emitter SHALL emit the cast operator body using the same expression and statement emission logic as regular method bodies.
4. WHEN a Cast_Operator_Declaration is marked as intrinsic, THE Emitter SHALL look up the emission template from the intrinsic registry and emit accordingly, consistent with the existing intrinsic operator emission pattern.

### Requirement 11: C# Code Emission for Cast Expressions

**User Story:** As a Mist developer, I want the transpiled C# code to correctly emit cast expressions, so that the compiled output invokes the appropriate conversion operator.

#### Acceptance Criteria

1. WHEN a Cast_Expression has a resolved cast operator that is a user-defined implicit or explicit operator, THE Emitter SHALL emit the cast as a static method call on the declaring type (e.g., `Banana.op_Explicit(b)` for an explicit cast from `Banana` to `Weight`).
2. WHEN a Cast_Expression has a resolved cast operator that is intrinsic, THE Emitter SHALL look up the emission template from the intrinsic registry and emit accordingly.
3. WHEN a Cast_Expression has no resolved cast operator (identity or subtype cast), THE Emitter SHALL emit a standard C# cast expression `(TargetType)expression`.
4. THE Emitter SHALL emit cast expressions with correct operator precedence, wrapping the operand in parentheses when necessary to preserve evaluation order.

### Requirement 12: Implicit Cast Emission at Assignment and Argument Sites

**User Story:** As a Mist developer, I want implicit casts to be emitted correctly at assignment, argument, and return sites, so that the transpiled C# code performs the conversion.

#### Acceptance Criteria

1. WHEN an assignment has a resolved implicit cast operator, THE Emitter SHALL emit the right-hand side as a static method call invoking the implicit cast operator (e.g., `Weight.op_Implicit(b)`).
2. WHEN a method argument has a resolved implicit cast operator, THE Emitter SHALL wrap the argument expression in a static method call invoking the implicit cast operator.
3. WHEN a return statement has a resolved implicit cast operator, THE Emitter SHALL wrap the return expression in a static method call invoking the implicit cast operator.

### Requirement 13: Cast Operator Diagnostic Messages

**User Story:** As a Mist developer, I want clear and specific error messages when cast operator declarations or cast expressions are invalid, so that I can quickly identify and fix the issue.

#### Acceptance Criteria

1. WHEN a Cast_Expression targets a type for which no cast operator exists on the Compile_Time_Type of the operand, THE Compiler SHALL emit a diagnostic that names the source type, the target type, and states that no cast operator is defined between them.
2. WHEN a Cast_Expression uses a base-typed variable and the cast operator is only defined on a derived type, THE Compiler SHALL emit a diagnostic that names the base type as the source type and indicates that the cast operator is not available on the compile-time type.
3. WHEN a Cast_Operator_Declaration violates the enclosing type constraint (neither source nor target is the enclosing type), THE Compiler SHALL emit a diagnostic that names the enclosing type, the source type, and the target type.
4. WHEN duplicate cast operators are declared, THE Compiler SHALL emit a diagnostic that identifies the cast kind (implicit or explicit), the source type, and the target type.
5. WHEN an implicit conversion is ambiguous, THE Compiler SHALL emit a diagnostic that names the source type, the target type, and lists the conflicting cast operators.

### Requirement 14: Interaction with Existing Type System Features

**User Story:** As a Mist developer, I want cast operators to work correctly alongside existing type system features like inheritance, interfaces, traits, and numeric widening, so that conversions compose predictably.

#### Acceptance Criteria

1. THE Type_Checker SHALL evaluate implicit cast operators only after checking direct assignability (identity, subtype, interface implementation, trait satisfaction, and numeric widening); implicit casts are a fallback when direct assignability fails.
2. THE Type_Checker SHALL NOT apply implicit cast operators when the source type is already directly assignable to the target type.
3. WHEN a type has both an inheritance relationship and a cast operator to the same target type, THE Type_Checker SHALL prefer the inheritance relationship for assignability and not invoke the cast operator.
4. THE Type_Checker SHALL allow cast operators to coexist with operator overloads on the same type without conflict; cast operators and arithmetic/comparison operators occupy separate namespaces.
5. THE Type_Checker SHALL allow a type to define both an implicit and an explicit cast operator for different target types without conflict.

### Requirement 15: Syntax Highlighting for Cast Keywords

**User Story:** As a Mist developer using an editor, I want the `implicit` and `explicit` keywords to be syntax-highlighted, so that cast operator declarations are visually consistent with other operator declarations.

#### Acceptance Criteria

1. THE TextMate grammar (`mist.tmLanguage.json`) SHALL include `implicit` and `explicit` in the keyword patterns so they are highlighted as keywords.
2. WHEN a Mist source file contains an `implicit` or `explicit` keyword, THE editor SHALL highlight it with the same style as other declaration keywords like `operator`, `class`, and `struct`.

### Requirement 16: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented cast operator features, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the cast operator feature is implemented, THE Compiler team SHALL update the `Implicit/explicit conversion operators` entry in the `Other Members` section of CSHARP_FEATURES.md from 🔲 to ✅.
2. WHEN the cast operator feature is implemented, THE Compiler team SHALL update the `Cast expressions ((T)expr)` entry in the `Other Expressions` section of CSHARP_FEATURES.md from 🔲 to ✅.
3. WHEN the cast operator feature is implemented, THE Compiler team SHALL update the `Explicit cast validation` entry in the `Type Checking & Diagnostics` section of CSHARP_FEATURES.md from 🔲 to ✅.
4. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation counts.

### Requirement 17: Cast Operator Sample Application

**User Story:** As a Mist developer evaluating the language, I want a sample application that demonstrates cast operators in action, so that I can see how implicit casts, explicit casts, and compile-time safety work in practice.

#### Acceptance Criteria

1. THE sample application SHALL be located in a new `samples/CastOperators/` directory following the existing sample app convention.
2. THE sample application SHALL demonstrate declaring an implicit cast operator on a type.
3. THE sample application SHALL demonstrate declaring an explicit cast operator on a type.
4. THE sample application SHALL demonstrate an implicit cast being applied automatically during assignment.
5. THE sample application SHALL demonstrate an explicit cast using `(TargetType)expression` syntax.
6. THE sample application SHALL demonstrate the compile-time safety feature with a commented-out example showing that casting through a base-typed variable produces a compile error.
7. THE sample application SHALL compile and run successfully through the Mist compiler pipeline, producing correct output via `Console.WriteLine`.
8. THE sample application SHALL include a `static void Main` entry point that exercises all demonstrated cast operator features and prints observable output.

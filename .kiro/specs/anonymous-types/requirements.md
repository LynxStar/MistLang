# Requirements Document

## Introduction

This document specifies the requirements for adding anonymous types to the Mist programming language. Anonymous types provide a concise way to create objects with read-only properties without declaring a named type, using the syntax `new { Name = value, ... }`. This mirrors C#'s anonymous type feature exactly — the Mist emitter maps directly to C#'s `new { ... }` syntax in the generated output.

Anonymous types are structural, immutable, reference types whose shape is determined by the property names and types of the initializer. Two anonymous type expressions with the same property names, types, and order produce the same anonymous type. Anonymous types support member access, equality comparison (structural by default via compiler-generated `Equals`/`GetHashCode`), and pattern matching via property patterns.

Because Mist compiles to C# and C# already has full anonymous type support (the compiler synthesizes the backing class), the Mist emitter relies on C#'s `new { ... }` syntax directly rather than generating helper types.

**Scope:** This spec covers only the behavior that mirrors C#'s anonymous types. Struct anonymous types, type aliases for anonymous shapes, and anonymous types in method signatures are explicitly out of scope for this iteration.

## Glossary

- **Anonymous_Type**: A compiler-generated, immutable reference type whose shape is defined by the properties in a `new { ... }` expression. The type has no user-declared name.
- **Anonymous_Type_Expression**: The syntactic form `new { Name1 = expr1, Name2 = expr2, ... }` that constructs a value of an Anonymous_Type.
- **Property_Projection**: A shorthand form where a property name is inferred from the expression, such as `new { variable }` producing a property named after the variable, or `new { expr.Member }` producing a property named after the member.
- **AnonymousType**: The semantic model type representing an anonymous type instance, containing ordered named properties with resolved types.
- **Parser**: The Mist parser generated from the Storm grammar.
- **Pretty_Printer**: The Mist pretty printer (`MistPrettyPrinter`) that formats AST nodes back into valid Mist source text.
- **Emitter**: The C# transpiler backend (`CSharpEmitter` / `CSharpSourceWriter`) that produces C# source from the Mist semantic model.
- **Type_Checker**: The analysis pass (`TypeCheckerAnalyzer`) that validates type correctness of expressions and statements.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST.

## Requirements

### Requirement 1: Anonymous Type Expression Syntax

**User Story:** As a Mist developer, I want to create anonymous objects using `new { Name = expr, ... }` syntax, so that I can group related values without declaring a named type.

#### Acceptance Criteria

1. WHEN an expression of the form `new { Name1 = expr1, Name2 = expr2, ..., NameN = exprN }` is encountered where N >= 1, THE Parser SHALL parse it as an Anonymous_Type_Expression node containing N named property assignments.
2. WHEN an anonymous type expression contains a single identifier `new { variable }`, THE Parser SHALL parse it as a Property_Projection where the property name is inferred from the identifier name.
3. WHEN an anonymous type expression contains a member access `new { expr.Member }`, THE Parser SHALL parse it as a Property_Projection where the property name is inferred from the member name.
4. WHEN an anonymous type expression mixes explicit assignments and projections (e.g., `new { Name = expr, variable }`), THE Parser SHALL parse each property according to its form.
5. WHEN an anonymous type expression contains zero properties `new { }`, THE Parser SHALL report a diagnostic error indicating that anonymous types require at least one property.
6. THE Pretty_Printer SHALL format an Anonymous_Type_Expression as `new { Name1 = expr1, Name2 = expr2 }` for explicit assignments and `new { name }` for projections.
7. FOR ALL valid Anonymous_Type_Expression AST nodes, parsing then pretty-printing then parsing SHALL produce an equivalent AST node (round-trip property).

### Requirement 2: Semantic Model Representation

**User Story:** As a compiler developer, I want an AnonymousType in the semantic model that represents anonymous types structurally, so that the type system can reason about their properties without a named declaration.

#### Acceptance Criteria

1. THE AnonymousType SHALL implement the IType interface with a DisplayName of the form `{ Name1: T1, Name2: T2, ..., NameN: TN }`.
2. THE AnonymousType SHALL store an ordered list of property names and a parallel list of property types.
3. WHEN two AnonymousType instances have the same property names, property types, and property order, THE Type_Checker SHALL treat them as the same type.
4. WHEN two AnonymousType instances differ in property names, property types, or property order, THE Type_Checker SHALL treat them as distinct types.
5. THE Semantic_Model_Builder SHALL infer the type of each property from the corresponding initializer expression and construct an AnonymousType with the resolved property types.
6. THE AnonymousType SHALL use `TypeKind.Class` (reference type semantics).

### Requirement 3: Type Checking — Property Access

**User Story:** As a Mist developer, I want to access properties on anonymous type values using dot notation, so that I can read the values stored in the anonymous object.

#### Acceptance Criteria

1. WHEN a member access expression `anonValue.PropertyName` is encountered where `anonValue` has an AnonymousType containing a property named `PropertyName`, THE Type_Checker SHALL resolve the access to the property's type.
2. IF a member access expression `anonValue.Name` is encountered where `Name` does not match any property in the AnonymousType, THEN THE Type_Checker SHALL report an undefined member diagnostic.
3. THE Type_Checker SHALL treat all AnonymousType properties as read-only and SHALL report a diagnostic error if an assignment to an anonymous type property is attempted.

### Requirement 4: Type Checking — Assignability and Scope Rules

**User Story:** As a Mist developer, I want clear rules for where anonymous types can and cannot be used, so that I understand the type system's behavior.

#### Acceptance Criteria

1. WHEN an anonymous type value is assigned to a variable with an inferred type (`var`), THE Type_Checker SHALL infer the variable's type as the AnonymousType.
2. WHEN an anonymous type value is assigned to a variable of type `unknown`, THE Type_Checker SHALL treat the assignment as valid.
3. WHEN an anonymous type is used as a method return type annotation, THE Type_Checker SHALL report a diagnostic error indicating that anonymous types cannot appear in method signatures.
4. WHEN an anonymous type is used as a method parameter type annotation, THE Type_Checker SHALL report a diagnostic error indicating that anonymous types cannot appear in method signatures.
5. WHEN an anonymous type is used as a field type annotation, THE Type_Checker SHALL report a diagnostic error indicating that anonymous types cannot be used as field types.
6. WHEN two variables have the same AnonymousType (same property names, types, and order), THE Type_Checker SHALL allow assignment between them.
7. WHEN an anonymous type is used as a generic type argument, THE Type_Checker SHALL report a diagnostic error indicating that anonymous types have no denotable name.

### Requirement 5: Type Checking — Pattern Matching

**User Story:** As a Mist developer, I want to use property patterns to destructure anonymous type values, so that I can extract their properties in conditional contexts.

#### Acceptance Criteria

1. WHEN a property pattern `{ Name: pattern }` is applied to a scrutinee of an AnonymousType containing a property named `Name`, THE Type_Checker SHALL validate the sub-pattern against the property's type.
2. WHEN a `var` pattern is applied to a scrutinee of an AnonymousType, THE Semantic_Model_Builder SHALL introduce the pattern variable with the full AnonymousType.
3. WHEN a property pattern references a property name that does not exist on the AnonymousType, THE Type_Checker SHALL report a diagnostic error.

### Requirement 6: Grammar Changes

**User Story:** As a compiler developer, I want the parser to correctly distinguish anonymous type expressions from target-typed new expressions and object creation expressions, so that existing code continues to parse correctly.

#### Acceptance Criteria

1. THE Grammar SHALL define an `AnonymousTypeExpression` alternative in the Primary rule that matches `NEW LEFT_BRACE AnonymousPropertyList RIGHT_BRACE`.
2. THE Grammar SHALL define an `AnonymousPropertyList` rule containing one or more `AnonymousProperty` elements separated by commas, with an optional trailing comma.
3. THE Grammar SHALL define an `AnonymousProperty` rule with alternatives for explicit assignment (`IDENTIFIER EQUAL Expression`) and projection (`Postfix`).
4. WHEN the parser encounters `new { ... }` where the braces contain at least one `IDENTIFIER EQUAL Expression` or a valid projection form, THE Parser SHALL always parse it as an AnonymousTypeExpression.
5. WHEN the parser encounters `new { ... }` where the content does not match any anonymous property syntax, THE Parser SHALL fall through to existing alternatives (TargetTypedNewExpressionNoArgs with PropertyInitializerBlock).

### Requirement 7: C# Emission

**User Story:** As a Mist developer, I want anonymous type expressions to emit idiomatic C# code, so that the generated code interoperates with C# libraries.

#### Acceptance Criteria

1. WHEN an Anonymous_Type_Expression `new { Name1 = expr1, Name2 = expr2 }` is encountered, THE Emitter SHALL emit C#'s native anonymous type expression `new { Name1 = expr1, Name2 = expr2 }`.
2. THE Emitter SHALL map a Mist Property_Projection `new { variable }` to the C# projection form `new { variable }`.
3. THE Emitter SHALL map a Mist member projection `new { expr.Member }` to the C# projection form `new { expr.Member }`.
4. THE Emitter SHALL map property accesses on anonymous type values `anonValue.Name` to C# member access `anonValue.Name`.
5. WHEN an anonymous type value is stored in a variable, THE Emitter SHALL emit the variable declaration using `var` (since C# anonymous types have no denotable name).

### Requirement 8: Interaction with `unknown` and Pattern Narrowing

**User Story:** As a Mist developer, I want to store anonymous type values in `unknown`-typed locations and narrow them back using property patterns, so that I can use anonymous types in dynamic scenarios.

#### Acceptance Criteria

1. WHEN an anonymous type value is assigned to an `unknown`-typed variable, THE Emitter SHALL emit an assignment to an `object`-typed variable (standard `unknown` lowering).
2. WHEN a property pattern is used to match against an `unknown`-typed scrutinee at runtime, THE Emitter SHALL emit C# code that checks the runtime type and extracts property values.
3. THE Type_Checker SHALL allow property patterns with any property names when the scrutinee type is `unknown`, deferring validation to runtime.

### Requirement 9: Equality Semantics

**User Story:** As a Mist developer, I want anonymous types to support structural equality by default, so that two anonymous objects with the same property values are considered equal.

#### Acceptance Criteria

1. THE Type_Checker SHALL allow `==` and `!=` operators on values of the same AnonymousType.
2. THE Emitter SHALL rely on C#'s built-in anonymous type equality (which provides structural equality via the compiler-generated `Equals` and `GetHashCode` overrides).
3. WHEN two anonymous type values of different AnonymousTypes are compared with `==`, THE Type_Checker SHALL report a diagnostic error.

### Requirement 10: Error Diagnostics

**User Story:** As a Mist developer, I want clear error messages when I misuse anonymous types, so that I can quickly understand and fix problems.

#### Acceptance Criteria

1. WHEN an anonymous type expression contains duplicate property names, THE Type_Checker SHALL report a diagnostic error indicating the duplicate property name.
2. WHEN a Property_Projection cannot infer a property name (e.g., `new { 42 }` or `new { method() }`), THE Type_Checker SHALL report a diagnostic error indicating that projections require an identifier or member access expression.
3. WHEN an anonymous type is used in a position that requires a denotable type (method signature, field declaration, generic type argument), THE Type_Checker SHALL report a diagnostic error explaining that anonymous types cannot escape their declaration scope.
4. WHEN an assignment to an anonymous type property is attempted, THE Type_Checker SHALL report a diagnostic error indicating that anonymous type properties are read-only.
5. IF a property initializer expression in an anonymous type cannot be type-checked, THEN THE Type_Checker SHALL report the underlying error and use `ErrorType` for that property.

### Requirement 11: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want anonymous type expressions to be syntax-highlighted correctly, so that I get visual cues distinguishing them from regular object creation.

#### Acceptance Criteria

1. THE TextMate grammar SHALL continue to recognize `new` as a keyword (scope `keyword.other.new.mist`) in anonymous type expressions.
2. THE TextMate grammar SHALL recognize property names in anonymous type initializers (before `=`) with scope `variable.other.property.mist`.
3. THE TextMate grammar SHALL distinguish the `=` in anonymous type property assignments from the regular assignment operator.

### Requirement 12: Book Documentation

**User Story:** As a Mist developer, I want the language book to document anonymous types, so that I can learn the syntax and semantics from the official reference.

#### Acceptance Criteria

1. THE book SHALL include a section covering anonymous types with compiled code examples.
2. THE book section SHALL demonstrate anonymous type creation with explicit assignments, property projections, property access, interaction with `unknown`, and pattern matching.
3. All code examples SHALL be compiled snippets following the Book Snippet Policy.
4. THE book's SUMMARY.md SHALL reference the new section in the appropriate chapter.

### Requirement 13: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating anonymous types, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/AnonymousTypes/Program.mist`.
2. The sample SHALL demonstrate every syntax variant and usage pattern defined in the preceding requirements, including but not limited to: explicit property assignment, property projection from variables, property projection from member access, mixed explicit and projection forms, property access on anonymous values, assignment to `unknown`, pattern matching with property patterns, and equality comparison.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `AnonymousTypes.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

#### Proof Code

```mist
using Standard;

namespace AnonymousTypes;

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("=== Anonymous Types Sample ===");

        // PROOF: Parser recognizes `new { Name = expr }` as anonymous type expression
        // PROOF: Type checker infers AnonymousType with named properties
        // PROOF: Emitter produces C# `new { Name = "Mist", Version = 1 }`
        Console.WriteLine("--- Explicit property assignment ---");
        var lang = new { Name = "Mist", Version = 1 };
        Console.WriteLine($"Name={lang.Name} Version={lang.Version}");

        // PROOF: Property projection infers name from variable identifier
        // PROOF: Emitter produces C# `new { city, population }`
        Console.WriteLine("--- Property projection from variables ---");
        var city = "Portland";
        var population = 650000;
        var info = new { city, population };
        Console.WriteLine($"city={info.city} population={info.population}");

        // PROOF: Property projection infers name from member access
        // PROOF: Emitter produces C# `new { lang.Name }`
        Console.WriteLine("--- Property projection from member access ---");
        var projected = new { lang.Name };
        Console.WriteLine($"projected.Name={projected.Name}");

        // PROOF: Mixed explicit and projection forms in one expression
        Console.WriteLine("--- Mixed explicit and projection ---");
        var tag = "release";
        var mixed = new { tag, BuildNumber = 42 };
        Console.WriteLine($"tag={mixed.tag} BuildNumber={mixed.BuildNumber}");

        // PROOF: Same-shape anonymous types are assignable to each other
        // PROOF: Type checker treats same property names/types/order as same type
        Console.WriteLine("--- Same-shape assignment ---");
        var first = new { X = 1, Y = 2 };
        var second = new { X = 10, Y = 20 };
        second = first;
        Console.WriteLine($"X={second.X} Y={second.Y}");

        // PROOF: Anonymous type assigned to `unknown`
        // PROOF: Emitter lowers `unknown` to `object`
        Console.WriteLine("--- Assignment to unknown ---");
        unknown box = new { X = 10, Y = 20 };
        Console.WriteLine($"box is object: {box is object}");

        // PROOF: Property pattern narrows anonymous type from `unknown`
        // PROOF: Emitter generates C# property pattern match
        Console.WriteLine("--- Property pattern matching ---");
        if (box is { X: var x, Y: var y })
        {
            Console.WriteLine($"X={x} Y={y}");
        }

        // PROOF: Structural equality — same shape and values are equal
        // PROOF: Emitter relies on C# compiler-generated Equals/GetHashCode
        Console.WriteLine("--- Equality ---");
        var a = new { Id = 1, Label = "one" };
        var b = new { Id = 1, Label = "one" };
        var c = new { Id = 2, Label = "two" };
        Console.WriteLine($"a==b: {a == b}");
        Console.WriteLine($"a==c: {a == c}");

        Console.WriteLine("Done!");
    }
}
```

**Expected output:**
```
=== Anonymous Types Sample ===
--- Explicit property assignment ---
Name=Mist Version=1
--- Property projection from variables ---
city=Portland population=650000
--- Property projection from member access ---
projected.Name=Mist
--- Mixed explicit and projection ---
tag=release BuildNumber=42
--- Same-shape assignment ---
X=1 Y=2
--- Assignment to unknown ---
box is object: True
--- Property pattern matching ---
X=10 Y=20
--- Equality ---
a==b: True
a==c: False
Done!
```

### Requirement 14: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented anonymous types feature, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the anonymous types feature is implemented, THE Compiler team SHALL update CSHARP_FEATURES.md to add an `Anonymous Types` entry marking it as implemented.
2. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation counts.

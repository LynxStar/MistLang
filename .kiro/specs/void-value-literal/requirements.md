# Requirements Document

## Introduction

Make the `void` keyword context-sensitive in expression position so that it serves as both a type literal and a value literal depending on the target type. Today, obtaining the Void singleton requires writing `Void.Void` — an implementation detail that leaks the struct's internal API. After this feature, `void` in expression position resolves to the Void singleton value when the target type is `void`, and continues to resolve as a type literal when the target type is `Type` or when no target type is available. This eliminates `Void.Void` from user-facing code entirely.

Additionally, an implicit conversion from `Void` to `Type` is added so that a void value can flow into a `Type`-typed context without explicit casting.

## Glossary

- **Compiler**: The Mist compiler pipeline, encompassing lexing, parsing, semantic analysis, type checking, and code generation.
- **Semantic_Model_Builder**: The `SemanticModelBuilder` component that constructs the semantic model from the parsed AST, resolving types and building scopes.
- **Type_Checker**: The `TypeCheckerAnalyzer` component that validates type correctness of Mist programs during semantic analysis.
- **Emitter**: The `CSharpEmitter` backend component that generates C# source code from the Mist semantic model.
- **VoidType**: The semantic model type representing the Mist unit type (`Standard.Void`).
- **Void_Singleton**: The single value of the Void type — the runtime instance accessed today via `Void.Void`.
- **Type_Literal**: An expression whose value is a `Standard.Type` instance representing a type at runtime (e.g., `int`, `string`, `void` used as a type literal).
- **Target_Type**: The type expected by the surrounding context of an expression (e.g., the declared type of a variable, the parameter type of a method, the return type of a function).
- **TypeLiteralExpressionPrimaryNode**: The AST node produced by the parser when a type keyword (like `void`) appears in expression position.

## Requirements

### Requirement 1: void as Value Literal with Target Type void

**User Story:** As a Mist developer, I want to write `void` in expression position where a `void` value is expected, so that I can produce the Void singleton without referencing the internal `Void.Void` API.

#### Acceptance Criteria

1. WHEN the `void` keyword appears in expression position AND the declared type of the assignment target, parameter, or return type is `VoidType`, THE Semantic_Model_Builder SHALL resolve the expression type as `VoidType` (the Void singleton value) rather than `Standard.Type`.
2. WHEN a variable is declared with explicit type annotation `void` and initialized with the expression `void`, THE Type_Checker SHALL accept the assignment without error and the variable's type SHALL be `VoidType`.
3. WHEN a method parameter has declared type `void` and the argument expression is `void`, THE Type_Checker SHALL accept the call without error.
4. WHEN a method has return type `void` and the body contains the statement `return void;`, THE Type_Checker SHALL accept the statement without error.
5. WHEN a generic type is instantiated with `void` as a type argument and a field or parameter of that type argument is assigned the expression `void`, THE Type_Checker SHALL accept the assignment without error.
6. IF the `void` keyword appears in expression position AND the target type is not `VoidType` (e.g., the target type is `int`, `string`, or any non-void type), THEN THE Semantic_Model_Builder SHALL resolve the expression type as `Standard.Type` (the metatype), preserving existing type-literal behavior.

### Requirement 2: void as Type Literal with Target Type Type

**User Story:** As a Mist developer, I want `void` to continue working as a type literal when the target type is `Type`, so that existing code using `void` as a type value is not broken.

#### Acceptance Criteria

1. WHEN the `void` keyword appears in expression position AND the Target_Type is `Standard.Type`, THE Semantic_Model_Builder SHALL resolve the expression type as `Standard.Type` (a Type_Literal representing the Void type).
2. WHEN a variable is declared with type `Type` and initialized with the expression `void`, THE Type_Checker SHALL accept the assignment without error and the variable SHALL hold a `Standard.Type` instance whose `Name` property returns `"Void"`.
3. WHEN a method parameter has type `Type` and the argument expression is `void`, THE Type_Checker SHALL accept the call without error and the parameter SHALL receive the Type instance representing `Standard.Void`.
4. WHEN a method has return type `Type` and the return expression is `void`, THE Type_Checker SHALL accept the return statement without error and the method SHALL return the Type instance representing `Standard.Void`.

### Requirement 3: void as Type Literal with No Target Type

**User Story:** As a Mist developer, I want `void` with no target type context (e.g., `var x = void;`) to remain a type literal, so that backward compatibility is preserved.

#### Acceptance Criteria

1. WHEN the `void` keyword appears in expression position AND no Target_Type is available (e.g., `var` inference), THE Semantic_Model_Builder SHALL resolve the expression type as `Standard.Type`.
2. WHEN a variable is declared with `var` and initialized with the expression `void`, THE Semantic_Model_Builder SHALL infer the variable type as `Standard.Type` and the variable SHALL hold the Type instance whose `Name` property returns `"Void"`.
3. WHEN a variable is declared with `var` and initialized with the expression `void`, THE Type_Checker SHALL reject assignment of that variable to a `void`-typed target without an explicit cast, confirming the inferred type is `Standard.Type` and not `VoidType`.

### Requirement 4: Implicit Conversion from Void to Type

**User Story:** As a Mist developer, I want a void value to implicitly convert to `Type` when needed, so that a variable holding the Void singleton can be passed where a `Type` is expected without explicit casting.

#### Acceptance Criteria

1. THE `Void` struct SHALL declare a `public static implicit operator Type(Void v)` that returns the Type instance whose `Name` property equals `"Void"`.
2. WHEN a variable of type `void` is assigned to a variable of type `Type`, THE Type_Checker SHALL accept the assignment via the implicit conversion operator without error.
3. WHEN a method parameter has type `Type` and the argument is a variable of type `void`, THE Type_Checker SHALL accept the call via the implicit conversion operator without error.
4. WHEN a method with return type `void` is called and its result is assigned to a variable of type `Type`, THE Type_Checker SHALL accept the assignment via the implicit conversion operator without error.

### Requirement 5: Deprecation of Void.Void

**User Story:** As a language designer, I want to deprecate the `Void.Void` static property so that users migrate to the `void` keyword for obtaining the Void singleton.

#### Acceptance Criteria

1. THE Compiler SHALL continue to accept `Void.Void` as a valid expression that produces the Void_Singleton (no breaking change).
2. WHEN `Void.Void` is used in source code as a member access expression, THE Compiler SHALL emit a warning diagnostic for each occurrence, with severity `Warning` and a stable diagnostic ID, indicating that `void` in expression position is the preferred way to obtain the Void singleton.
3. THE deprecation warning SHALL include the source location of the `Void.Void` expression so the developer can identify which usage to migrate.

### Requirement 6: C# Code Generation for void Value Literal

**User Story:** As a compiler developer, I want the C# backend to correctly emit code when `void` is used as a value literal, so that the generated C# produces the Void singleton at runtime.

#### Acceptance Criteria

1. WHEN the Emitter generates code for a `void` expression that resolved as a value literal (Target_Type is `VoidType`), THE Emitter SHALL emit the C# expression `MistVoid.Instance`.
2. WHEN the Emitter generates code for a `void` expression that resolved as a type literal (Target_Type is `Type`), THE Emitter SHALL emit the C# expression `typeof(MistVoid)`.
3. WHEN the Emitter generates code for the implicit `Void → Type` conversion, THE Emitter SHALL emit `MistVoid.Instance` at the assignment or argument site and rely on the C# compiler to resolve the implicit operator declared on the `MistVoid` struct without emitting an explicit cast or method call.
4. WHEN the Emitter generates code for a `void` expression that resolved as a value literal AND the enclosing context targets a two-constituent `T | void` optional union type, THE Emitter SHALL emit `null` instead of `MistVoid.Instance`.
5. WHEN any compilation unit uses `void` as a value (triggering emission of `MistVoid.Instance` or `null` in optional context), THE Emitter SHALL include the generated `MistVoid.g.cs` helper file in the output containing the `MistVoid` readonly struct with a public static `Instance` field, `IEquatable<MistVoid>` implementation, and equality operators.

### Requirement 7: Update Sample Applications

**User Story:** As a Mist developer, I want all sample applications to use the `void` keyword instead of `Void.Void`, so that the samples demonstrate the idiomatic way to obtain the Void singleton.

#### Acceptance Criteria

1. WHEN the `samples/VoidType/VoidDemo.mist` sample is compiled, THE sample SHALL use the `void` keyword in place of every `Void.Void` expression where the value is used as a void-typed value (variable initializers, return statements, arguments, and generic constructor arguments).
2. WHEN the `samples/OptionalTypes/Program.mist` sample is compiled, THE sample SHALL use the `void` keyword in place of every `Void.Void` expression where the value is assigned to an optional-typed variable (`T?`).
3. IF any other sample file under the `samples/` directory contains a `Void.Void` expression, THEN THE sample SHALL be updated to use the `void` keyword instead.
4. WHEN any updated sample is compiled and executed, THE sample SHALL produce output identical (byte-for-byte on stdout) to its corresponding baseline in `samples/__baselines__/` and SHALL exit with code 0.
5. WHEN any updated sample is compiled, THE compiler SHALL report zero errors and zero warnings.

### Requirement 8: Update Language Book

**User Story:** As a Mist developer reading the language book, I want the documentation to reflect that `void` is the idiomatic way to obtain the Void singleton, so that I learn the correct pattern.

#### Acceptance Criteria

1. THE `book/src/basics/void-type.md` chapter SHALL be updated so that all prose describing how to obtain the Void singleton uses the `void` keyword in expression position rather than `Void.Void`, and documents that `void` in expression position produces the Void singleton when the target type is `void`.
2. THE book chapter SHALL explain the context-sensitive resolution rules: target type `void` → value, target type `Type` → type literal, no target type → type literal.
3. All `.mist` snippet files in `book/snippets/basics/void-type/` that use `Void.Void` SHALL be updated to use the `void` keyword in expression position.
4. All `.mist` snippet files in `book/snippets/type-system/optional-types/` that use `Void.Void` SHALL be updated to use the `void` keyword in expression position.
5. THE `book/src/basics/functions.md` prose reference to `Void.Void` as a return form SHALL be updated to use `void`.
6. WHEN any `.mist` snippet file is modified, THE corresponding `.cs` file SHALL be regenerated by running `dotnet run --project tools/SnippetEmitter`, and the tool SHALL report zero failures for all updated snippets.
7. All updated `.mist` snippets SHALL compile successfully with zero errors when processed by the SnippetEmitter tool, confirming that the `void` keyword is valid in each expression context where `Void.Void` was previously used.

### Requirement 9: Sample Application — Void Value Literal Demo

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating the void value literal feature, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/VoidValueLiteral/Program.mist` with a corresponding `samples/VoidValueLiteral/mist.toml` project file.
2. The sample SHALL demonstrate every usage pattern: void as value with target type void, void as type literal with target type Type, void with var inference, void in return statements, void as method arguments, void in generic contexts, the implicit Void → Type conversion, void equality operators, and void as optional absence value.
3. Each demonstrated permutation SHALL print labeled output to stdout matching the expected output defined in the Proof Code section below.
4. `samples/AllFeatures/Program.mist` SHALL call `VoidValueLiteral.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.
6. The sample SHALL contain the Proof Code below verbatim.

#### Proof Code

```mist
// VoidValueLiteral — Demonstrates context-sensitive void keyword in expression position.

using Standard;

namespace VoidValueLiteral;

public class Program
{
    public static void DoNothing()
    {
        // PROOF: void in return statement resolves as Void singleton (target type is void)
        return void;
    }

    public static string DescribeVoid(void v)
    {
        return "received void";
    }

    public class Result<T>
    {
        T value;
        bool success;

        public Result(T value, bool success)
        {
            this.value = value;
            this.success = success;
        }

        public bool IsSuccess()
        {
            return success;
        }
    }

    public static void RunDemo()
    {
        Console.WriteLine("--- Void Value Literal ---");

        // PROOF: void with target type void resolves as Void singleton value
        void unit = void;
        Console.WriteLine($"void variable: {unit}");

        // PROOF: void with target type Type resolves as type literal (backward compat)
        Type t = void;
        Console.WriteLine($"Type variable: {t.Name}");

        // PROOF: void with var inference resolves as type literal (backward compat)
        var inferred = void;
        Console.WriteLine($"var inferred type name: {inferred.Name}");

        // PROOF: void as method argument where parameter type is void
        string desc = DescribeVoid(void);
        Console.WriteLine($"DescribeVoid(void): {desc}");

        // PROOF: void in return position — DoNothing returns Void singleton
        var returned = DoNothing();
        Console.WriteLine($"DoNothing() returned: {returned}");

        // PROOF: void in generic context — Result<void> field assigned void
        var result = new Result<void>(void, true);
        Console.WriteLine($"Result<void>.IsSuccess: {result.IsSuccess()}");

        // PROOF: implicit Void → Type conversion
        void v = void;
        Type fromVoid = v;
        Console.WriteLine($"implicit Void->Type: {fromVoid.Name}");

        // PROOF: void equality still works with void literal
        Console.WriteLine($"void == void: {void == void}");
        Console.WriteLine($"void != void: {void != void}");

        // PROOF: optional assignment — void as absence value
        int? absent = void;
        int coalesced = absent ?? 42;
        Console.WriteLine($"absent ?? 42: {coalesced}");
    }
}
```

**Expected output:**
```
--- Void Value Literal ---
void variable: Void
Type variable: Void
var inferred type name: Void
DescribeVoid(void): received void
DoNothing() returned: Void
Result<void>.IsSuccess: True
implicit Void->Type: Void
void == void: True
void != void: False
absent ?? 42: 42
```

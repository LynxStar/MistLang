# Requirements Document

## Introduction

This document specifies the requirements for adding the `as` operator to the Mist programming language. The `as` operator provides safe type conversion that yields an optional result (`T?`, i.e., `T | void`) when the conversion cannot be proven safe at compile time, or yields the bare target type `T` when the conversion is provably safe (widening/identity).

Unlike C#'s `as` operator which relies on null to signal failure, Mist's `as` operator uses `void` — the language's standard "absence of value" type. The result type depends on compile-time provability:

- **Provable conversions** (identity, widening to a base type): result type is `T` (bare, always succeeds)
- **Non-provable conversions** (narrowing from base to derived, narrowing from union to constituent, `unknown` to concrete): result type is `T?` (i.e., `T | void` — runtime check)
- **Impossible conversions** (unrelated types with no type relationship): compile error

The `as` operator complements the existing `is` pattern matching: `is` binds the narrowed value inside a conditional branch, while `as` produces the optional value as an expression that can be stored, passed, or chained with `??`.

Example:
```
Shape shape = new Circle(5);
Circle? maybeCircle = shape as Circle;    // T? — runtime check (narrowing)
Shape widened = circle as Shape;           // T — always succeeds (widening)
int? fromUnion = myUnion as int;          // T? — runtime check (union narrowing)
```

Key characteristics:
- **Provable widening yields bare `T`**: When the source type is a subtype of the target type (derived → base), the conversion always succeeds and the result type is `T` (not `T?`).
- **Non-provable conversions yield `T?`**: When the conversion requires a runtime check, the result is `T | void`.
- **Compile-time rejection of impossible conversions**: When no type relationship exists between source and target, the compiler emits an error.
- **No exceptions**: The `as` operator never throws. It either succeeds or produces `void`.
- **Works with unions**: `unionValue as T` where T is a constituent yields `T?`.
- **Works with `unknown`**: `unknownValue as T` yields `T?`.
- **Works with class/struct inheritance**: derived `as` base yields `T`; base `as` derived yields `T?`.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in two passes.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter`) that translates the Mist AST into C# source files.
- **As_Expression**: The expression `expr as T` that performs a safe type conversion.
- **Provable_Conversion**: A conversion that the Compiler can statically guarantee will always succeed (identity, widening).
- **Non_Provable_Conversion**: A conversion that requires a runtime type check (narrowing, union extraction, unknown narrowing).
- **Widening**: A conversion from a more-specific type to a less-specific type (derived → base, constituent → union). Always safe.
- **Narrowing**: A conversion from a less-specific type to a more-specific type (base → derived, union → constituent, unknown → concrete). May fail at runtime.
- **Optional_Type**: The type `T?` which is syntactic sugar for the union type `T | void`. Represents a value that is either `T` or absent (`void`).
- **UnionType**: A structural union type `A | B | ...` that can hold a value of any constituent type.
- **UnknownType**: The top type in Mist's type system. Every non-void value is assignable to `unknown`, but `unknown` cannot be assigned to a non-unknown type without narrowing.
- **IsExpression**: The existing `expr is Pattern` syntax that performs pattern matching with type narrowing inside conditional branches.
- **Compile_Time_Type**: The static type of an expression as determined by the Compiler during type checking.

## Requirements

### Requirement 1: As Expression Grammar

**User Story:** As a Mist developer, I want to write `expr as T` to safely attempt a type conversion, so that I get either the converted value or `void` without risking exceptions.

#### Acceptance Criteria

1. THE Grammar SHALL define an `AsExpression` variant in the `Relational` rule, consisting of a left operand (an `Additive` expression), the `AS` keyword, and a target type reference (`TypeReference`).
2. THE `AsExpression` variant SHALL be placed in the `Relational` rule at the same precedence level as the existing `IsExpression`, since both are type-testing/converting operators.
3. WHEN an `as` expression is parsed, THE Parser SHALL produce an AST node that captures the left operand expression and the target type reference.
4. THE `AS` keyword token already exists in `keywords.storm` and SHALL be reused (it is currently used only in trait with-block member mappings; the grammar context disambiguates).

### Requirement 2: As Expression Type Rules — Provable Conversions

**User Story:** As a Mist developer, I want `as` to return the bare target type `T` (not `T?`) when the conversion is provably safe at compile time, so that I don't need to unwrap an optional for widening conversions.

#### Acceptance Criteria

1. WHEN the Compile_Time_Type of the left operand is identical to the target type (identity), THE Type_Checker SHALL set the result type of the As_Expression to `T`.
2. WHEN the Compile_Time_Type of the left operand is a subtype of the target type (class or struct inheritance — derived → base), THE Type_Checker SHALL set the result type of the As_Expression to `T`.
3. WHEN the Compile_Time_Type of the left operand is a type that implements or satisfies the target interface or trait, THE Type_Checker SHALL set the result type of the As_Expression to `T`.
4. WHEN the Compile_Time_Type of the left operand is a constituent of the target union type (e.g., `int as int | string`), THE Type_Checker SHALL set the result type of the As_Expression to the target union type `T`.
5. THE Type_Checker SHALL use the same `IsDirectlyAssignable` logic (identity, subtype, interface, trait — but NOT implicit cast operators) to determine whether a conversion is provable.

### Requirement 3: As Expression Type Rules — Non-Provable Conversions

**User Story:** As a Mist developer, I want `as` to return `T?` when the conversion might fail at runtime, so that I can safely handle the absence case using `??`, `is`, or other void-handling mechanisms.

#### Acceptance Criteria

1. WHEN the target type is a subtype of the Compile_Time_Type (narrowing: base → derived), THE Type_Checker SHALL set the result type of the As_Expression to `T?` (i.e., `T | void`).
2. WHEN the Compile_Time_Type is a union type and the target type is one of its constituents, THE Type_Checker SHALL set the result type of the As_Expression to `T?`.
3. WHEN the Compile_Time_Type is `unknown` and the target type is any concrete type, THE Type_Checker SHALL set the result type of the As_Expression to `T?`.
4. WHEN the Compile_Time_Type is a class or struct and the target type is a derived class or struct of that type, THE Type_Checker SHALL set the result type of the As_Expression to `T?`.
5. WHEN the Compile_Time_Type is an interface or trait and the target type is a concrete type, THE Type_Checker SHALL set the result type of the As_Expression to `T?`.

### Requirement 4: As Expression Type Rules — Invalid Conversions

**User Story:** As a Mist developer, I want the compiler to reject `as` expressions between unrelated types at compile time, so that I don't write meaningless conversions that would always produce `void`.

#### Acceptance Criteria

1. WHEN the Compile_Time_Type and the target type have no type relationship (neither is a subtype of the other, no interface/trait relationship, no union membership, neither is `unknown`), THE Type_Checker SHALL emit a diagnostic error indicating that the `as` conversion is not valid between the two types.
2. THE diagnostic SHALL name the source type and the target type and suggest using an explicit cast operator if a user-defined conversion is desired.
3. WHEN the target type is `void`, THE Type_Checker SHALL emit a diagnostic error indicating that `as void` is not a valid conversion (the result would be meaningless).
4. WHEN the Compile_Time_Type is `void`, THE Type_Checker SHALL emit a diagnostic error indicating that a void-typed expression cannot be used with `as`.

### Requirement 5: As Expression Semantic Model Integration

**User Story:** As a Mist developer, I want the compiler to correctly resolve the types of `as` expressions in the semantic model, so that downstream analysis and code generation work correctly.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL resolve the target type reference in an As_Expression to an `IType` during Pass 2.
2. THE Semantic_Model_Builder SHALL resolve the left operand expression and determine its Compile_Time_Type during Pass 2.
3. THE Type_Checker SHALL set the expression type of the As_Expression node to either `T` (for provable conversions) or `T | void` (for non-provable conversions) after evaluating the type rules.
4. THE Type_Checker SHALL record metadata on the As_Expression node indicating whether the conversion is provable (so the emitter knows whether to emit a runtime check or a direct conversion).

### Requirement 6: C# Code Emission for As Expressions

**User Story:** As a Mist developer, I want the transpiled C# code to correctly implement `as` expressions, so that provable conversions emit direct casts and non-provable conversions emit safe runtime checks.

#### Acceptance Criteria

1. WHEN an As_Expression is a Provable_Conversion (identity), THE Emitter SHALL emit the operand expression unchanged (no cast needed).
2. WHEN an As_Expression is a Provable_Conversion (widening: derived struct → base struct), THE Emitter SHALL emit the existing implicit conversion operator call that the struct inheritance system already generates.
3. WHEN an As_Expression is a Provable_Conversion (widening: derived class → base class), THE Emitter SHALL emit a standard C# upcast `((BaseType)expr)`.
4. WHEN an As_Expression is a Non_Provable_Conversion and the target is a reference type (class), THE Emitter SHALL emit C#'s `as` operator: `(expr as T)`, relying on the union helper type's constructor to handle the `null` → `void` mapping.
5. WHEN an As_Expression is a Non_Provable_Conversion and the target is a value type (struct), THE Emitter SHALL emit a pattern-match check: `(expr is T _t ? _t : default)` wrapped in the appropriate union type construction for `T | void`.
6. WHEN an As_Expression is a Non_Provable_Conversion and the source is a union type, THE Emitter SHALL emit the union's tag-check logic to extract the constituent or produce `void`.
7. WHEN an As_Expression is a Non_Provable_Conversion and the source is `unknown`, THE Emitter SHALL emit a C# `is` type-check pattern that produces the value or the void sentinel.

> **Clarified (2026-10-03):** criteria 4, 5 and 7 prescribe C# spellings that are correct only where the C# spelling of the target is the whole runtime test. Where it drops a concrete length -- `int[3]` is `int[]` in C#, and a tuple may hold such an element -- the Emitter tests the length as well, `(x is int[] { Length: 3 } t ? t : default(int[]))`, even for a reference target. The observable contract governs the lowering: the value when it is the target, `void` otherwise, and the operand evaluated once.

### Requirement 7: As Expression Interaction with Existing Features

**User Story:** As a Mist developer, I want `as` to compose naturally with existing Mist features like `??`, `?.`, pattern matching, and assignment, so that I can write idiomatic code.

#### Acceptance Criteria

1. THE result of an As_Expression SHALL be usable with the void-coalescing operator `??` to provide a fallback value (e.g., `shape as Circle ?? defaultCircle`).
2. THE result of an As_Expression SHALL be usable with the conditional member access operator `?.` (e.g., `(shape as Circle)?.Radius`).
3. THE result of an As_Expression SHALL be assignable to a variable of type `T?` (for non-provable conversions) or `T` (for provable conversions).
4. THE result of an As_Expression SHALL be usable as a method argument where the parameter type is `T?` or `T` (matching the result type).
5. THE `as` operator SHALL NOT trigger user-defined implicit or explicit cast operators. It operates purely on type hierarchy relationships, union membership, and unknown narrowing. User-defined conversions require the explicit cast syntax `(T)expr`.
6. WHEN used inside an `if` condition (e.g., `if (x as T) is T t`), the narrowed binding `t` SHALL be available in the then-branch, composing with `is` patterns.

### Requirement 8: As Expression Diagnostic Messages

**User Story:** As a Mist developer, I want clear error messages when I misuse the `as` operator, so that I can quickly understand and fix the issue.

#### Acceptance Criteria

1. WHEN an As_Expression is used between unrelated types, THE Compiler SHALL emit a diagnostic (e.g., `MIST_TC_0070`) stating: "The 'as' operator cannot be used between '{sourceType}' and '{targetType}' because no type relationship exists. Use an explicit cast operator if a user-defined conversion is needed."
2. WHEN an As_Expression targets `void`, THE Compiler SHALL emit a diagnostic (e.g., `MIST_TC_0071`) stating: "'as void' is not a valid conversion."
3. WHEN an As_Expression has a void-typed left operand, THE Compiler SHALL emit a diagnostic (e.g., `MIST_TC_0072`) stating: "A void-typed expression cannot be used with the 'as' operator."
4. ALL diagnostics SHALL include the source span of the `as` expression for precise error location reporting.

### Requirement 9: As Expression Precedence and Associativity

**User Story:** As a Mist developer, I want the `as` operator to have predictable precedence so that complex expressions parse correctly without excessive parentheses.

#### Acceptance Criteria

1. THE `as` operator SHALL have the same precedence as the `is` operator (both in the `Relational` rule, below equality operators and above additive operators).
2. THE `as` operator SHALL be non-associative — `expr as T1 as T2` is a parse error (requires parentheses: `(expr as T1) as T2`).
3. THE `as` operator SHALL bind tighter than equality (`==`, `!=`) so that `x as int == 5` parses as `(x as int) == 5`.
4. THE `as` operator SHALL bind looser than additive/multiplicative operators so that `x + y as int` parses as `(x + y) as int`.

### Requirement 10: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want the `as` keyword in `as` expressions to be syntax-highlighted correctly, so that type conversion expressions are visually distinct.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize the `as` keyword in `as` expressions and highlight it with the `keyword.operator.mist` scope (consistent with `is`).
2. THE TextMate grammar SHALL highlight the type reference following `as` with the appropriate type scope (e.g., `entity.name.type.mist` or `storage.type.mist`).

### Requirement 11: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating the `as` operator, so that I can see every supported usage pattern and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/AsOperator/Program.mist`.
2. The sample SHALL demonstrate every conversion category: identity, widening (struct and class), narrowing (struct and class), union narrowing, and unknown narrowing.
3. Each demonstrated conversion SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `AsOperator.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

#### Proof Code

```mist
using Standard;

namespace AsOperator;

public class Animal
{
    public string Name;

    public Animal(string name)
    {
        Name = name;
    }
}

public class Dog : Animal
{
    public string Breed;

    public Dog(string name, string breed) : base(name)
    {
        Breed = breed;
    }
}

public class Cat : Animal
{
    public int Lives;

    public Cat(string name, int lives) : base(name)
    {
        Lives = lives;
    }
}

public struct Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }
}

public struct Point3D : Point
{
    public int Z;

    public Point3D(int x, int y, int z) : base(x, y)
    {
        Z = z;
    }
}

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("=== As Operator ===");

        // PROOF: Identity conversion — as with same type yields T (not T?)
        Console.WriteLine("--- Identity ---");
        var dog1 = new Dog("Rex", "Labrador");
        Dog identityDog = dog1 as Dog;
        Console.WriteLine($"  identity: {identityDog.Name}");

        // PROOF: Widening (class) — derived as base yields T (bare, not optional)
        Console.WriteLine("--- Widening (class) ---");
        var dog2 = new Dog("Buddy", "Poodle");
        Animal widened = dog2 as Animal;
        Console.WriteLine($"  widened: {widened.Name}");

        // PROOF: Widening (struct) — derived struct as base struct yields T
        Console.WriteLine("--- Widening (struct) ---");
        var p3d = new Point3D(1, 2, 3);
        Point widenedPoint = p3d as Point;
        Console.WriteLine($"  widened: ({widenedPoint.X}, {widenedPoint.Y})");

        // PROOF: Narrowing (class) — base as derived yields T? (runtime check)
        Console.WriteLine("--- Narrowing (class) ---");
        Animal animal1 = new Dog("Fido", "Beagle");
        Dog? maybeDog = animal1 as Dog;
        if (maybeDog is Dog d)
        {
            Console.WriteLine($"  narrowed dog: {d.Name}, {d.Breed}");
        }

        Animal animal2 = new Cat("Whiskers", 9);
        Dog? notADog = animal2 as Dog;
        if (notADog is Dog _)
        {
            Console.WriteLine($"  ERROR: should not reach here");
        }
        else
        {
            Console.WriteLine($"  not a dog: void");
        }

        // PROOF: Union narrowing — union as constituent yields T?
        Console.WriteLine("--- Union narrowing ---");
        int | string union1 = 42;
        int? maybeInt = union1 as int;
        if (maybeInt is int n)
        {
            Console.WriteLine($"  union->int: {n}");
        }

        int | string union2 = "hello";
        int? notInt = union2 as int;
        if (notInt is int _)
        {
            Console.WriteLine($"  ERROR: should not reach here");
        }
        else
        {
            Console.WriteLine($"  union->int failed: void");
        }

        // PROOF: Unknown narrowing — unknown as T yields T?
        Console.WriteLine("--- Unknown narrowing ---");
        unknown box = 99;
        int? fromUnknown = box as int;
        if (fromUnknown is int u)
        {
            Console.WriteLine($"  unknown->int: {u}");
        }

        unknown box2 = "surprise";
        int? notFromUnknown = box2 as int;
        if (notFromUnknown is int _)
        {
            Console.WriteLine($"  ERROR: should not reach here");
        }
        else
        {
            Console.WriteLine($"  unknown->int failed: void");
        }

        // PROOF: Composing as with ?? (void coalescing)
        Console.WriteLine("--- Compose with ?? ---");
        Animal animal3 = new Cat("Luna", 7);
        Dog? attempt = animal3 as Dog;
        var fallback = attempt ?? new Dog("Default", "Unknown");
        Console.WriteLine($"  fallback: {fallback.Name}, {fallback.Breed}");

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
=== As Operator ===
--- Identity ---
  identity: Rex
--- Widening (class) ---
  widened: Buddy
--- Widening (struct) ---
  widened: (1, 2)
--- Narrowing (class) ---
  narrowed dog: Fido, Beagle
  not a dog: void
--- Union narrowing ---
  union->int: 42
  union->int failed: void
--- Unknown narrowing ---
  unknown->int: 99
  unknown->int failed: void
--- Compose with ?? ---
  fallback: Default, Unknown
Done!
```

### Requirement 12: Book Documentation

**User Story:** As a Mist developer, I want the language book to document the `as` operator, so that I can learn its semantics and usage patterns from the official reference.

#### Acceptance Criteria

1. THE book SHALL include a section covering the `as` operator with compiled code examples.
2. THE book section SHALL demonstrate: identity conversion, widening, narrowing, union narrowing, unknown narrowing, composition with `??` and `?.`, and the difference between `as` and explicit casts.
3. All code examples SHALL be compiled snippets following the Book Snippet Policy.
4. THE book's SUMMARY.md SHALL reference the new section in the appropriate chapter.

### Requirement 13: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the `as` operator feature, so that feature tracking stays accurate.

#### Acceptance Criteria

1. WHEN the `as` operator feature is implemented, THE Compiler team SHALL add an `as operator (safe type conversion)` entry to the appropriate section of CSHARP_FEATURES.md and mark it ✅.
2. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation count.

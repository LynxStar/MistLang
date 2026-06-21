# Requirements Document

## Introduction

This document specifies the requirements for adding target-typed `new()` expressions to the Mist programming language. Target-typed `new()` allows developers to omit the type name in object creation expressions when the target type is statically known from context — a feature inspired by C# 9's target-typed new expressions.

Currently, Mist requires the full type name in all object creation expressions:

```mist
public List<WorkItem> WorkItems = new List<WorkItem>();
```

With target-typed `new()`, the redundant type name can be omitted when the compiler can infer it:

```mist
public List<WorkItem> WorkItems = new();
```

Key characteristics:
- **Context-dependent type inference**: The type of the `new()` expression is resolved from the target type in the surrounding context (field initializer, variable declaration, return statement, method argument, or assignment).
- **Constructor resolution**: After the target type is inferred, the compiler verifies that the type has an accessible constructor matching the provided arguments.
- **Arguments supported**: Target-typed `new` supports passing arguments — `new(arg1, arg2)` — just like a fully-typed object creation expression.
- **Property initializers supported**: Target-typed `new` supports trailing property initializer blocks — `new() { Prop = value }` and `new(args) { Prop = value }`.
- **Not valid with `var`**: Since `var` provides no target type, `var x = new();` is an error.
- **C# emission**: The Emitter produces fully-typed `new TypeName(args)` in the C# output (since target-typed new is syntactic sugar — the resolved type is always explicit in the emitted code).

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in two passes: Pass 1 registers types and members, Pass 2 resolves type references and expressions.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Target_Type**: The statically known type that a target-typed `new()` expression infers its type from. The target type is determined by the context in which the expression appears (e.g., the declared type of a field, variable, parameter, or return type).
- **Target_Typed_New_Expression**: An expression of the form `new(arguments?)` or `new(arguments?) { initializers }` where the type name is omitted and inferred from the Target_Type.
- **Object_Creation_Expression**: The existing fully-typed form `new TypeName(arguments?)` where the type is explicitly specified.
- **Property_Initializer_Block**: A brace-enclosed block of property assignments following a `new` expression, e.g., `{ Prop1 = value1, Prop2 = value2 }`.
- **Constructor_Resolution**: The process of finding an accessible constructor on a type that matches the provided argument list in terms of count and types.
- **Accessible_Constructor**: A constructor whose visibility allows it to be called from the current scope.

## Requirements

### Requirement 1: Target-Typed New Expression Grammar

**User Story:** As a Mist developer, I want to write `new()` and `new(args)` without specifying a type name, so that I can reduce redundancy when the target type is already known from context.

#### Acceptance Criteria

1. THE Grammar SHALL define a `TargetTypedNewExpression` rule that consists of the `NEW` keyword, a left parenthesis, an optional `ArgumentList`, a right parenthesis, and an optional `PropertyInitializerBlock`.
2. THE Grammar SHALL define a `TargetTypedNewExpressionNoArgs` rule that consists of the `NEW` keyword followed by a `PropertyInitializerBlock` (no parentheses, no type name) for the case where only property initializers are provided.
3. THE Grammar SHALL include `TargetTypedNewExpression` and `TargetTypedNewExpressionNoArgs` as variants in the `Primary` expression rule alongside existing primary expressions.
4. THE Grammar SHALL order the `TargetTypedNewExpression` alternative after the existing `ObjectCreationExpression` and `ObjectCreationExpressionNoArgs` alternatives, so that fully-typed `new TypeName(...)` is preferred when a type name is present.
5. WHEN the `NEW` keyword is followed immediately by a left parenthesis (with no intervening identifier or type argument list), THE Parser SHALL parse it as a TargetTypedNewExpression.
6. WHEN the `NEW` keyword is followed immediately by a left brace (with no intervening identifier, type argument list, or parentheses), THE Parser SHALL parse it as a TargetTypedNewExpressionNoArgs.

### Requirement 2: Pretty-Printer Support

**User Story:** As a Mist developer, I want the pretty-printer to correctly format target-typed new expressions, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a target-typed new expression with no arguments and no initializer as `new()`.
2. THE Pretty_Printer SHALL format a target-typed new expression with arguments as `new(arg1, arg2)`.
3. THE Pretty_Printer SHALL format a target-typed new expression with a property initializer block as `new() { Prop = value }` or `new(args) { Prop = value }`, preserving indentation consistent with existing property initializer formatting.
4. THE Pretty_Printer SHALL format a target-typed new expression with only a property initializer block (no parentheses) as `new { Prop = value }`.
5. FOR ALL valid Mist ASTs containing target-typed new expressions, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 3: Target Type Resolution

**User Story:** As a Mist developer, I want the compiler to infer the type of `new()` from the surrounding context, so that I do not need to repeat type names in positions where the type is already declared.

#### Acceptance Criteria

1. WHEN a target-typed new expression appears as the initializer of a field declaration with an explicit type annotation, THE Semantic_Model_Builder SHALL resolve the Target_Type from the declared field type.
2. WHEN a target-typed new expression appears as the initializer of a local variable declaration with an explicit type annotation, THE Semantic_Model_Builder SHALL resolve the Target_Type from the declared variable type.
3. WHEN a target-typed new expression appears in a return statement of a method with a declared return type, THE Semantic_Model_Builder SHALL resolve the Target_Type from the method's return type.
4. WHEN a target-typed new expression appears as an argument to a method call where the corresponding parameter type is known, THE Semantic_Model_Builder SHALL resolve the Target_Type from the parameter type.
5. WHEN a target-typed new expression appears on the right-hand side of an assignment to an existing typed variable, THE Semantic_Model_Builder SHALL resolve the Target_Type from the variable's declared type.
6. WHEN a target-typed new expression appears in a `var` declaration (`var x = new();`), THE Semantic_Model_Builder SHALL record the expression type as ErrorType because no Target_Type is available.
7. WHEN a target-typed new expression appears in a context where no Target_Type can be determined, THE Semantic_Model_Builder SHALL record the expression type as ErrorType.

### Requirement 4: Constructor Resolution and Validation

**User Story:** As a Mist developer, I want the compiler to verify that the inferred type has a matching accessible constructor, so that I get clear errors when `new()` is used with incompatible arguments.

#### Acceptance Criteria

1. WHEN the Target_Type is resolved and the target-typed new expression has arguments, THE Type_Checker SHALL verify that the resolved type has an accessible constructor whose parameter types match the provided argument types.
2. WHEN the Target_Type is resolved and the target-typed new expression has no arguments, THE Type_Checker SHALL verify that the resolved type has an accessible parameterless constructor.
3. WHEN the resolved type does not have a matching accessible constructor, THE Type_Checker SHALL emit a diagnostic error naming the type and indicating which constructor signature was expected.
4. WHEN the Target_Type resolves to an interface type, THE Type_Checker SHALL emit a diagnostic error indicating that interfaces cannot be instantiated with target-typed new.
5. WHEN the Target_Type resolves to a trait type, THE Type_Checker SHALL emit a diagnostic error indicating that traits cannot be instantiated with target-typed new.
6. WHEN the Target_Type resolves to an abstract class, THE Type_Checker SHALL emit a diagnostic error indicating that abstract classes cannot be instantiated.

### Requirement 5: Type Checking for Target-Typed New

**User Story:** As a Mist developer, I want clear diagnostic errors when I misuse target-typed `new()`, so that I can quickly fix inference failures and type mismatches.

#### Acceptance Criteria

1. WHEN `var x = new();` is encountered, THE Type_Checker SHALL emit a diagnostic error stating that the type of `new()` cannot be inferred because `var` does not provide a target type.
2. WHEN a target-typed new expression appears in a context where no target type can be determined, THE Type_Checker SHALL emit a diagnostic error stating that the type of `new()` cannot be inferred from context.
3. WHEN the resolved Target_Type is a generic type, THE Type_Checker SHALL use the fully-constructed generic type (including type arguments) as the inferred type for the target-typed new expression.
4. WHEN the resolved Target_Type is a type that has been resolved from context, THE Type_Checker SHALL assign the target-typed new expression the resolved Target_Type as its expression type.

### Requirement 6: C# Code Emission

**User Story:** As a Mist developer, I want the transpiled C# output to contain the fully-qualified type name in `new` expressions, so that the generated code compiles correctly regardless of C# version target.

#### Acceptance Criteria

1. WHEN a target-typed new expression has a resolved Target_Type, THE Emitter SHALL emit `new TypeName(args)` using the fully-resolved type name (including generic type arguments where applicable).
2. WHEN a target-typed new expression has a property initializer block, THE Emitter SHALL emit `new TypeName(args) { Prop = value }` with the resolved type name and the initializer block.
3. THE Emitter SHALL emit target-typed new expressions identically to how it emits fully-typed Object_Creation_Expressions with the same resolved type and arguments.

### Requirement 7: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want target-typed `new()` expressions to be syntax-highlighted correctly, so that the `new` keyword is visually distinguished from surrounding code.

#### Acceptance Criteria

1. THE TextMate grammar SHALL continue to highlight `new` as a keyword (scope `keyword.operator.new.mist` or equivalent) regardless of whether it is followed by a type name or directly by parentheses.
2. WHEN a `new` keyword is followed directly by `(`, THE TextMate grammar SHALL highlight `new` with the same keyword style as when it is followed by a type name.

### Requirement 8: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating target-typed new expressions, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/TargetTypedNew/Program.mist`.
2. The sample SHALL demonstrate every context where target-typed new is supported: field initializers, local variable declarations with explicit types, return statements, method arguments, and assignment to existing typed variables.
3. The sample SHALL demonstrate target-typed new with arguments.
4. The sample SHALL demonstrate target-typed new with property initializer blocks.
5. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
6. `samples/AllFeatures/Program.mist` SHALL call `TargetTypedNew.Program.RunDemo()` to include the feature in the full-suite sample run.
7. The sample SHALL compile and run without errors using the Mist compiler.

#### Proof Code

```mist
// PROOF: Target-typed new resolves type from field declaration
// PROOF: Constructor arguments are forwarded correctly
// PROOF: Target-typed new resolves type from local variable with explicit type
// PROOF: Target-typed new resolves type from return statement
// PROOF: Target-typed new resolves type from method parameter type
// PROOF: Target-typed new resolves type from assignment to existing variable
// PROOF: Target-typed new works with generic types

class Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override string ToString()
    {
        return $"Point({X}, {Y})";
    }
}

class Pair<T>
{
    public T First;
    public T Second;

    public Pair(T first, T second)
    {
        First = first;
        Second = second;
    }

    public override string ToString()
    {
        return $"Pair({First}, {Second})";
    }
}

class Container
{
    // PROOF: field initializer with target-typed new
    public Point Origin = new(0, 0);
}

class Program
{
    // PROOF: return type inference
    public static Point MakePoint(int x, int y)
    {
        return new(x, y);
    }

    // PROOF: parameter type inference
    public static void PrintPoint(Point p)
    {
        Console.WriteLine($"  param: {p}");
    }

    public static void RunDemo()
    {
        Console.WriteLine("=== Target-Typed New ===");

        // PROOF: local variable with explicit type
        Point p1 = new(3, 4);
        Console.WriteLine($"  local: {p1}");

        // PROOF: return type
        var p2 = MakePoint(5, 6);
        Console.WriteLine($"  return: {p2}");

        // PROOF: method argument
        PrintPoint(new(7, 8));

        // PROOF: assignment to existing typed variable
        Point p3 = new(0, 0);
        p3 = new(9, 10);
        Console.WriteLine($"  assign: {p3}");

        // PROOF: field initializer
        var c = new Container();
        Console.WriteLine($"  field: {c.Origin}");

        // PROOF: generic target type
        Pair<int> pair = new(1, 2);
        Console.WriteLine($"  generic: {pair}");
    }
}
```

**Expected output:**
```
=== Target-Typed New ===
  local: Point(3, 4)
  return: Point(5, 6)
  param: Point(7, 8)
  assign: Point(9, 10)
  field: Point(0, 0)
  generic: Pair(1, 2)
```

### Requirement 9: Book Documentation

**User Story:** As a Mist developer, I want the language book to document target-typed new expressions, so that I can learn the syntax and semantics from the official reference.

#### Acceptance Criteria

1. THE book SHALL include a section covering target-typed new expressions with compiled code examples.
2. THE book section SHALL demonstrate the syntax, supported contexts, constructor resolution, and error cases.
3. All code examples SHALL be compiled snippets following the Book Snippet Policy.
4. THE book's SUMMARY.md SHALL reference the new section in the appropriate chapter.

### Requirement 10: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented target-typed new feature, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the target-typed new feature is implemented, THE Compiler team SHALL update the `Expressions — Object Creation` section of CSHARP_FEATURES.md to mark `Target-typed new()` as ✅ Implemented.
2. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation counts.

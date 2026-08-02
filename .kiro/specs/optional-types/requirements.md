# Requirements Document

## Introduction

This feature introduces optional type syntax and void-safety operators to the Mist programming language. Mist has no null concept but uses `void` as a unit type and supports union types. This feature adds syntactic sugar (`T?` for `T | void`), a standard library type alias (`Optional<T>`), and three operators for ergonomic handling of potentially-void values: the optional chaining operator (`?.`), the void-coalescing operator (`??`), and the void-coalescing assignment operator (`??=`). The `??=` operator additionally performs flow narrowing, removing `void` from the union type in subsequent scope.

## Glossary

- **Parser**: The Mist compiler component that transforms a token stream into a syntax tree according to the Mist grammar
- **Lexer**: The Mist compiler component that transforms source text into a token stream
- **Type_Checker**: The Mist compiler component that validates type correctness of expressions and statements against the semantic model
- **Emitter**: The Mist compiler backend that generates C# source code from the Mist semantic model
- **Optional_Type**: A union type of the form `T | void`, indicating a value that may be present (of type `T`) or absent (`void`)
- **Optional_Suffix**: The `?` character appended to a type name (e.g., `int?`) as syntactic sugar for `T | void`
- **Optional_Chaining_Operator**: The `?.` operator that conditionally accesses a member or invokes a method only when the receiver is not `void`
- **Void_Coalescing_Operator**: The `??` operator that returns the left operand if it is not `void`, otherwise returns the right operand
- **Void_Coalescing_Assignment_Operator**: The `??=` operator that assigns the right operand to the left operand if the left operand is `void`, and narrows the type of the left operand to exclude `void` in subsequent scope
- **Flow_Narrowing**: The compiler's ability to refine a variable's type within a scope based on control-flow analysis
- **Void_Type**: The Mist unit type representing absence of a value, defined as `Standard.Void`
- **Union_Type**: A structural type representing a value that may be one of several constituent types, written as `A | B | C`
- **Standard_Library**: The built-in Mist library at `Source/Standard/` providing core types and utilities
- **Sample_Application**: A demonstration program at `samples/OptionalTypes/Program.mist`
- **TextMate_Grammar**: The VS Code syntax highlighting grammar at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`

## Requirements

### Requirement 1: Optional Type Suffix Syntax

**User Story:** As a developer, I want to write `T?` instead of `T | void`, so that optional types are concise and readable.

#### Acceptance Criteria

1. WHEN a type reference includes a trailing `?` character, THE Parser SHALL parse `T?` as equivalent to the union type `T | void`
2. THE Parser SHALL support the optional suffix on any type reference (primitive types, named types, generic types) with no distinction between intrinsic, struct, or class types — the `?` binds to the complete type including any type argument list (e.g., `int?`, `MyClass?`, `Result<int>?`)
3. WHEN the optional suffix is applied to a type that already contains `void` as a union constituent (e.g., `(int | void)?`), THE Type_Checker SHALL flatten the result to `int | void` without duplicating the `void` constituent
4. THE Parser SHALL support the optional suffix in all type reference positions: variable declarations, parameter types, return types, field types, property types, generic type arguments, and type alias definitions
5. WHEN the optional suffix is applied to `void` itself (i.e., `void?`), THE Type_Checker SHALL report a diagnostic warning indicating that the type is redundantly equivalent to `void`
6. THE Parser SHALL bind the `?` suffix with higher precedence than the union operator `|`, so that `int? | string` is parsed as `(int | void) | string`, and with lower precedence than the array rank suffix `[]`, so that `int[]?` is parsed as `(int[]) | void` and `int?[]` is parsed as `(int | void)[]`
7. IF the optional suffix is applied twice in sequence (i.e., `T??`), THEN THE Parser SHALL report a diagnostic error indicating that the optional suffix cannot be applied to an already-optional type

### Requirement 2: Standard Library Optional Type Alias

**User Story:** As a developer, I want a standard library type alias `Optional<T>` defined as `T | void`, so that I can use a named type when the intent is clearer than the shorthand.

#### Acceptance Criteria

1. THE Standard_Library SHALL define a type alias `type Optional<T> = T | void;`
2. THE Type_Checker SHALL treat `Optional<T>`, `T?`, and `T | void` as structurally equivalent types, permitting assignment in both directions without explicit conversion
3. WHEN a variable is declared with type `Optional<T>`, THE Type_Checker SHALL permit assigning a value of type `T` or `void` to that variable without explicit conversion
4. IF code attempts to access a member (such as `.HasValue` or `.Value`) on a value typed as `Optional<T>`, THEN THE Type_Checker SHALL emit a diagnostic error indicating that the member does not exist on the union type `T | void`
5. WHEN the existing class-based `Optional<T>` in `Source/Standard/Optional.mist` is replaced with the type alias, THE Emitter SHALL generate compilable target code for programs that declare variables of type `Optional<T>`, pass `Optional<T>` as a type argument, and assign `T` or `void` values to `Optional<T>` variables

### Requirement 3: Optional Chaining Operator

**User Story:** As a developer, I want to use `?.` to conditionally access members on a potentially-void value, so that I can chain operations without explicit void checks.

#### Acceptance Criteria

1. WHEN the `?.` operator is applied to an expression of type `T | void`, THE Type_Checker SHALL project the receiver to type `T` for the purpose of resolving the subsequent member access or method call
2. IF the projected type `T` does not have the referenced member or method, THEN THE Type_Checker SHALL report a diagnostic error indicating the member is not found on type `T`
3. WHEN the member access or method call on the projected type `T` would return type `R`, THE Type_Checker SHALL infer the overall `?.` expression's result type as `R | void` (flattening if `R` already contains `void`)
4. IF the `?.` operator is applied to an expression whose type does not contain `void` as a union constituent (including the case where the type is exactly `void` alone), THEN THE Type_Checker SHALL report a diagnostic error indicating that `?.` requires a type of the form `T | void` so that it can project to `T`
5. THE Parser SHALL support chaining multiple optional chaining operators in a single expression (e.g., `a?.b?.c?.Method()`) with no fixed limit on chain length
6. WHEN the receiver expression evaluates to `void` at runtime in a chain of `?.` operators, THE Emitter SHALL generate code that short-circuits the entire remaining chain of `?.` accesses and produces a `void` value
7. THE Parser SHALL support optional chaining method calls with arguments (e.g., `foo?.Method(arg1, arg2)`)
8. THE Parser SHALL support optional chaining property access (e.g., `foo?.Property`)

### Requirement 4: Void-Coalescing Operator

**User Story:** As a developer, I want to use `??` to provide a fallback value when an expression evaluates to void, so that I can concisely handle the absence case.

#### Acceptance Criteria

1. WHEN the `??` operator is applied, THE Type_Checker SHALL require the left operand to be of a type that contains `void` as a union constituent
2. WHEN the left operand of `??` has type `T | void` and the right operand has type `T`, THE Type_Checker SHALL infer the result type as `T`
3. IF the right operand of `??` has type `U` that is not assignable to `T` (where the left operand has type `T | void`), THEN THE Type_Checker SHALL report a diagnostic error
4. IF the left operand of `??` does not contain `void` as a union constituent, THEN THE Type_Checker SHALL report a diagnostic error that the operator requires an optional type on the left
5. WHEN the left operand evaluates to a non-void value at runtime, THE Emitter SHALL generate code that returns the left operand without evaluating the right operand (short-circuit evaluation)
6. THE Parser SHALL support chaining void-coalescing operators (e.g., `a ?? b ?? c`) with right-to-left associativity

### Requirement 5: Void-Coalescing Assignment Operator

**User Story:** As a developer, I want to use `??=` to assign a fallback value and have the compiler narrow the type to exclude void from that point forward, so that I can eliminate void checks in subsequent code.

#### Acceptance Criteria

1. WHEN the `??=` operator is applied, THE Type_Checker SHALL require the left operand to be a non-readonly variable whose type contains `void` as a union constituent
2. WHEN `x ??= value` is executed and `x` is `void`, THE Emitter SHALL generate code that assigns `value` to `x` without evaluating `value` more than once
3. WHEN `x ??= value` is executed and `x` is not `void`, THE Emitter SHALL generate code that leaves `x` unchanged and does not evaluate `value` (short-circuit semantics)
4. WHEN `x ??= value` appears in a scope where `x` has type `T | void`, THE Type_Checker SHALL narrow the type of `x` to `T` for all subsequent statements in the same scope (including nested blocks) after the `??=` statement
5. IF the right operand of `??=` has type `U` that is not assignable to `T` (where the left operand has type `T | void`), THEN THE Type_Checker SHALL report a diagnostic error
6. IF the left operand of `??=` is not an assignable variable (e.g., a readonly local, a readonly field, or an arbitrary expression), THEN THE Type_Checker SHALL report a diagnostic error
7. IF the left operand of `??=` does not contain `void` as a union constituent, THEN THE Type_Checker SHALL report a diagnostic error that the operator requires an optional type
8. THE Type_Checker SHALL treat `x ??= value` as a statement that does not produce a result value (it SHALL NOT be usable as an expression within another expression)

### Requirement 6: Operator Precedence and Interaction

**User Story:** As a developer, I want the new operators to interact correctly with existing operators and expressions, so that compound expressions behave predictably.

#### Acceptance Criteria

1. THE Parser SHALL insert the `??` operator into the precedence hierarchy between LogicalOr (lower) and Assignment (higher), so the chain becomes: Assignment < VoidCoalescing < LogicalOr < LogicalAnd < Equality < Relational < Additive < Multiplicative < Unary < Postfix < Primary
2. THE Parser SHALL assign the `??=` operator the same precedence as other assignment operators (`=`) in the Assignment rule
3. THE Parser SHALL parse `?.` as a postfix operator with the same precedence as the existing `.` member access operator in the Postfix/Primary rule
4. WHEN `?.` and `??` are combined in a single expression (e.g., `x?.Method() ?? defaultValue`), THE Parser SHALL parse the `?.` first (higher precedence) and the `??` second
5. THE Parser SHALL correctly handle parenthesized expressions containing the new operators (e.g., `(a ?? b).Method()`)
6. THE Parser SHALL parse `??` as right-associative, so that `a ?? b ?? c` is parsed as `a ?? (b ?? c)`

### Requirement 7: Code Generation

**User Story:** As a developer, I want the compiler to generate correct and efficient C# code for all optional type operations, so that the runtime behavior matches the language semantics.

#### Acceptance Criteria

1. WHEN emitting a type `T | void` (or equivalently `T?`), THE Emitter SHALL map the optional type to C# as follows:
   - IF T is a value type (struct, primitive, enum), THE Emitter SHALL emit `T?` (C# `Nullable<T>`)
   - IF T is a reference type (class), THE Emitter SHALL emit `T` (using C# null to represent the void case)
   - THE Emitter SHALL NOT generate a `Union_T_Void` helper struct for two-constituent unions where one constituent is void
2. WHEN emitting code for the `?.` operator on a receiver of type `T | void`, THE Emitter SHALL generate C#'s native `?.` operator (null-conditional) since the void case maps to null in both the Nullable and reference-type representations
3. WHEN emitting code for the `??` operator, THE Emitter SHALL generate C#'s native `??` operator (null-coalescing) since the void case maps to null in both representations
4. WHEN emitting code for the `??=` operator, THE Emitter SHALL generate C#'s native `??=` operator (null-coalescing assignment) since the void case maps to null in both representations
5. THE Emitter SHALL ensure that the right operand of `??` is not evaluated when the left operand is non-void (short-circuit semantics — guaranteed by C#'s native `??` operator)
6. WHEN emitting code for chained `?.` operators (e.g., `a?.b?.c`), THE Emitter SHALL generate chained C# `?.` operators that short-circuit the entire chain upon encountering null at any point
7. WHEN emitting `void` literal values assigned to an optional type, THE Emitter SHALL emit `null` (since both `Nullable<T>` and reference types use null to represent absence)
8. WHEN emitting a multi-constituent union that contains void alongside multiple non-void types (e.g., `int | string | void`), THE Emitter SHALL continue to use the existing union helper struct approach (this optimization only applies to two-constituent `T | void` unions)

### Requirement 8: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want the new operators (`?.`, `??`, `??=`) and the optional type suffix (`?`) to be syntax-highlighted correctly, so that I get visual cues for optional type operations.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL recognize `?.` as an operator (scope `keyword.operator.void-conditional.mist`)
2. THE TextMate_Grammar SHALL recognize `??=` as an operator (scope `keyword.operator.void-coalescing-assignment.mist`)
3. THE TextMate_Grammar SHALL recognize `??` as an operator (scope `keyword.operator.void-coalescing.mist`)
4. THE TextMate_Grammar SHALL match `??=` before `??` to prevent the longer operator from being tokenized as two separate `??` and `=` tokens

### Requirement 9: Book Chapter

**User Story:** As a Mist developer reading the language book, I want a chapter on optional types, so that I can learn the `T?` syntax, `?.`, `??`, and `??=` operators with compilable examples.

#### Acceptance Criteria

1. A new chapter SHALL be added to the Type System section of the book at `book/src/type-system/optional-types.md`
2. The chapter SHALL be registered in `book/src/SUMMARY.md` under the Type System section (after "Type Unions")
3. The chapter SHALL cover: the `T?` suffix syntax, the `Optional<T>` type alias, the `?.` optional chaining operator, the `??` void-coalescing operator, the `??=` void-coalescing assignment operator with flow narrowing
4. Each concept SHALL have a dedicated snippet file in `book/snippets/type-system/optional-types/` with a corresponding `.cs` file generated by the SnippetEmitter tool
5. The chapter SHALL use the tabbed `{{#include}}` pattern for all code examples (Mist tab + Emitted C# tab)
6. Each snippet SHALL be a complete, compilable Mist program following the book snippet policy

### Requirement 10: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating optional types, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/OptionalTypes/Program.mist`
2. The sample SHALL demonstrate every syntax variant and usage pattern defined in the preceding requirements, including but not limited to: `T?` on primitive types, named types, and generic types; `Optional<T>` equivalence; `?.` for property access, method calls, and chaining; `??` for fallback values and chaining; `??=` for assignment and flow narrowing; combined `?.` with `??` expressions
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable
4. `samples/AllFeatures/Program.mist` SHALL call `OptionalTypes.Program.RunDemo()` to include the feature in the full-suite sample run
5. The sample SHALL compile and run without errors using the Mist compiler
6. The sample SHALL use C#-style (Allman) braces throughout

#### Proof Code

```mist
using Standard;

namespace OptionalTypes;

public class Inner
{
    public int value;

    public Inner(int v)
    {
        this.value = v;
    }

    public int GetValue()
    {
        return this.value;
    }

    public string Describe()
    {
        return $"Inner({this.value})";
    }
}

public class Outer
{
    public Inner? child;

    public Outer(Inner? c)
    {
        this.child = c;
    }

    public Inner? GetChild()
    {
        return this.child;
    }
}

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("=== OptionalTypes Sample ===");

        // PROOF: T? syntax parses as T | void for primitive types
        // PROOF: Assigning a concrete value to T? works
        int? maybeInt = 42;
        Console.WriteLine($"maybeInt={maybeInt}");

        // PROOF: T? syntax works for named types
        Inner? maybeInner = new Inner(10);
        Console.WriteLine($"maybeInner={maybeInner?.GetValue()}");

        // PROOF: Optional<T> is equivalent to T? — cross-assignment works
        Optional<int> optInt = 99;
        int? fromOpt = optInt;
        Console.WriteLine($"optInt={optInt} fromOpt={fromOpt}");

        // PROOF: ?. operator invokes method when receiver is non-void
        Inner? present = new Inner(5);
        int? result1 = present?.GetValue();
        Console.WriteLine($"present?.GetValue()={result1}");

        // PROOF: ?. operator short-circuits to void when receiver is void
        Inner? absent = void;
        int? result2 = absent?.GetValue();
        Console.WriteLine($"absent?.GetValue()={result2}");

        // PROOF: ?. chaining works through multiple levels
        Outer outerPresent = new Outer(new Inner(7));
        int? chained1 = outerPresent.GetChild()?.GetValue();
        Console.WriteLine($"outerPresent.GetChild()?.GetValue()={chained1}");

        Inner? absentInner = void;
        Outer outerAbsent = new Outer(absentInner);
        int? chained2 = outerAbsent.GetChild()?.GetValue();
        Console.WriteLine($"outerAbsent.GetChild()?.GetValue()={chained2}");

        // PROOF: ?? operator returns left when non-void
        int? hasValue = 10;
        int coalesced1 = hasValue ?? 0;
        Console.WriteLine($"hasValue ?? 0={coalesced1}");

        // PROOF: ?? operator returns right when left is void
        int? noValue = void;
        int coalesced2 = noValue ?? 42;
        Console.WriteLine($"noValue ?? 42={coalesced2}");

        // PROOF: ?? with nested optional — demonstrates fallback chain
        int? a = void;
        int? b = 77;
        int fromA = a ?? 99;
        int fromB = b ?? 99;
        Console.WriteLine($"a ?? 99={fromA}, b ?? 99={fromB}");

        // PROOF: ?. combined with ?? in a single expression
        Inner? forCombo = void;
        int comboResult = forCombo?.GetValue() ?? -1;
        Console.WriteLine($"forCombo?.GetValue() ?? -1={comboResult}");

        Inner? forCombo2 = new Inner(33);
        int comboResult2 = forCombo2?.GetValue() ?? -1;
        Console.WriteLine($"forCombo2?.GetValue() ?? -1={comboResult2}");

        // PROOF: ??= assigns when variable is void
        int? x = void;
        x ??= 100;
        Console.WriteLine($"after x ??= 100: x={x}");

        // PROOF: ??= does not assign when variable is non-void
        int? y = 50;
        y ??= 200;
        Console.WriteLine($"after y ??= 200: y={y}");

        // PROOF: ??= narrows type — z is now int (not int?) after ??=
        int? z = void;
        z ??= 7;
        Console.WriteLine($"after z ??= 7: z={z}");

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
=== OptionalTypes Sample ===
maybeInt=42
maybeInner=10
optInt=99 fromOpt=99
present?.GetValue()=5
absent?.GetValue()=Void
outerPresent.GetChild()?.GetValue()=7
outerAbsent.GetChild()?.GetValue()=Void
hasValue ?? 0=10
noValue ?? 42=42
a ?? 99=99, b ?? 99=77
forCombo?.GetValue() ?? -1=-1
forCombo2?.GetValue() ?? -1=33
after x ??= 100: x=100
after y ??= 200: y=50
after z ??= 7: z=7
Done!
```

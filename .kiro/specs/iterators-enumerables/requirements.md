# Requirements Document

## Introduction

This feature introduces first-class iterator/enumerable support to the Mist programming language, leveraging the recently-added associated types system. The design follows C# nomenclature with two generic interfaces — `IEnumerator<TElement>` and `IEnumerable<TElement>` — where `IEnumerable<TElement>` uses an associated type slot for its enumerator type. This enables struct enumerators resolved statically at compile time (avoiding boxing). The existing `foreach` statement is upgraded from a pure syntax pass-through into a semantically-validated construct backed by the `IEnumerable<TElement>` protocol, and the intrinsic `Array` type is upgraded to implement the interface.

## Glossary

- **IEnumerator**: A generic interface `IEnumerator<TElement>` in the Standard namespace representing a stateful cursor over a sequence of elements of type `TElement`. Declares `MoveNext()` and `Current` members.
- **IEnumerable**: A generic interface `IEnumerable<TElement>` in the Standard namespace representing a type that can produce an `IEnumerator<TElement>`. Declares an associated type slot `Enumerator` constrained to implement `IEnumerator<TElement>`, and a `GetEnumerator()` method returning that type.
- **Foreach_Statement**: The `foreach` loop construct in Mist that iterates over a collection by obtaining an enumerator and advancing through elements.
- **Compiler**: The Mist compiler pipeline including lexer, parser, semantic model builder, type checker, and C# transpiler backend.
- **Standard_Library**: The set of Mist source files shipped with the compiler that define built-in types and interfaces (located at `Source/Standard/`).
- **Array**: The intrinsic generic array type `Array<TValue, Length:ZeroOrPositiveInt>` defined in the Standard namespace.
- **Type_Checker**: The semantic analysis phase that validates type correctness, interface implementation, and constraint satisfaction.
- **Emitter**: The C# transpiler backend that generates C# source code from the Mist semantic model.
- **Desugaring**: The compiler transformation that rewrites a `foreach` statement into equivalent `GetEnumerator()` + `MoveNext()` + `Current` calls.

## Requirements

### Requirement 1: IEnumerator<TElement> Interface Definition

**User Story:** As a Mist developer, I want a standard `IEnumerator<TElement>` interface, so that I can define stateful iteration cursors with a well-known contract.

#### Acceptance Criteria

1. THE Standard_Library SHALL define an `IEnumerator<TElement>` interface in the Standard namespace with a generic type parameter `TElement` representing the element type
2. THE IEnumerator interface SHALL declare a method `bool MoveNext()` that advances the enumerator to the next element, returning true if an element is available or false if the sequence is exhausted
3. THE IEnumerator interface SHALL declare a get-only property `TElement Current` that returns the element at the current position
4. WHEN a type implements `IEnumerator<TElement>`, THE Type_Checker SHALL verify that the implementor provides implementations for both `MoveNext()` and `Current`
5. IF a type declares that it implements `IEnumerator<TElement>` but does not provide implementations for both `MoveNext()` and `Current`, THEN THE Type_Checker SHALL emit a diagnostic error indicating which required members are missing

### Requirement 2: IEnumerable<TElement> Interface Definition

**User Story:** As a Mist developer, I want a standard `IEnumerable<TElement>` interface with an associated type slot for the enumerator, so that collections can produce concrete enumerator types without boxing.

#### Acceptance Criteria

1. THE Standard_Library SHALL define an `IEnumerable<TElement>` interface in the Standard namespace with a generic type parameter `TElement` representing the element type
2. THE IEnumerable interface SHALL declare an associated type slot `type Enumerator : IEnumerator<TElement>` constraining the enumerator to implement `IEnumerator<TElement>`
3. THE IEnumerable interface SHALL declare a method `Enumerator GetEnumerator()` that returns a new enumerator instance whose `Current` is undefined until the first call to `MoveNext()`
4. WHEN a type implements `IEnumerable<TElement>`, THE Type_Checker SHALL verify that the implementor's `Enumerator` type satisfies the `IEnumerator<TElement>` constraint, and IF the constraint is not satisfied, THEN THE Type_Checker SHALL emit a diagnostic error indicating which members of `IEnumerator<TElement>` the Enumerator type fails to implement
5. WHEN a type implements `IEnumerable<TElement>`, THE Type_Checker SHALL verify that the implementor provides an implementation for `GetEnumerator()` returning its concrete `Enumerator` type, and IF the method is missing or its return type does not match the declared `Enumerator` type, THEN THE Type_Checker SHALL emit a diagnostic error identifying the missing or mismatched method

### Requirement 3: Foreach Semantic Validation

**User Story:** As a Mist developer, I want the compiler to verify that foreach targets implement `IEnumerable<TElement>`, so that I get clear compile-time errors when iterating over non-iterable types.

#### Acceptance Criteria

1. WHEN a foreach statement is encountered, THE Type_Checker SHALL verify that the collection expression's type implements `IEnumerable<TElement>` for some type `TElement`
2. IF the collection expression's type does not implement `IEnumerable<TElement>`, THEN THE Type_Checker SHALL emit a diagnostic error indicating the expression's resolved type and that it does not implement `IEnumerable<TElement>`
3. WHEN a foreach statement targets a valid `IEnumerable<TElement>` type, THE Type_Checker SHALL infer the iteration variable's type as `TElement` from the `IEnumerable<TElement>` implementation
4. WHEN a foreach statement declares an explicit element type, THE Type_Checker SHALL verify that the declared type is assignable from the `TElement` resolved from the collection's `IEnumerable<TElement>` implementation
5. IF a foreach statement declares an explicit element type that is not assignable from the resolved `TElement`, THEN THE Type_Checker SHALL emit a diagnostic error indicating the declared type and the expected element type `TElement`

### Requirement 4: C# Interface Mapping and Foreach Emission

**User Story:** As a Mist developer, I want the Mist `IEnumerable<TElement>` and `IEnumerator<TElement>` interfaces to map to their C# counterparts during emission, so that C#'s native `foreach` mechanism handles iteration without frontend desugaring.

#### Acceptance Criteria

1. WHEN emitting a type that implements Mist's `IEnumerable<TElement>`, THE Emitter SHALL emit the type as implementing C#'s `System.Collections.Generic.IEnumerable<TElement>`
2. WHEN emitting a type that implements Mist's `IEnumerator<TElement>`, THE Emitter SHALL emit the type as implementing C#'s `System.Collections.Generic.IEnumerator<TElement>`
3. WHEN emitting a foreach statement, THE Emitter SHALL emit a C# `foreach` statement directly, relying on C#'s duck-typed foreach protocol to resolve `GetEnumerator()`/`MoveNext()`/`Current`
4. THE Emitter SHALL NOT perform any frontend desugaring of foreach statements into explicit `GetEnumerator()`/`MoveNext()`/`Current` calls
5. WHEN emitting the associated type slot `Enumerator` on an `IEnumerable<TElement>` implementor, THE Emitter SHALL emit the concrete enumerator type with its `IEnumerator<TElement>` implementation intact, even though C#'s foreach does not require it

### Requirement 5: Array Implements IEnumerable<TElement>

**User Story:** As a Mist developer, I want arrays to implement `IEnumerable<TElement>`, so that foreach loops over arrays are semantically validated and consistent with other iterable collections.

#### Acceptance Criteria

1. THE Array intrinsic type SHALL declare that it implements `IEnumerable<TValue>` where `TValue` is the array's element type parameter
2. THE Array type SHALL declare an intrinsic nested struct `Enumerator` that implements `IEnumerator<TValue>` and satisfies the `IEnumerable<TValue>` associated type slot `type Enumerator : IEnumerator<TValue>`
3. THE Array's `Enumerator` struct SHALL declare intrinsic `bool MoveNext()` and `TValue Current { get; }` members
4. THE Array type SHALL declare an intrinsic `Enumerator GetEnumerator()` method
5. WHEN a foreach statement iterates over an array expression of type `Array<TValue, N>`, THE Type_Checker SHALL resolve the element type as `TValue` from the array's `IEnumerable<TValue>` implementation

### Requirement 6: Book Documentation Update

**User Story:** As a Mist developer reading the language book, I want the foreach section to accurately describe the `IEnumerable<TElement>` requirement, so that I understand how foreach works with the type system.

#### Acceptance Criteria

1. THE book's control flow chapter (`book/src/basics/control-flow.md`) SHALL update the foreach section to state that the collection expression must implement `IEnumerable<TElement>`
2. THE book SHALL remove the existing "Note" and "Design intent" blockquotes that describe foreach as unvalidated and the design as future work
3. THE book SHALL include a snippet demonstrating foreach over a user-defined `IEnumerable<TElement>` type in addition to the existing array example
4. THE snippet SHALL follow the book snippet policy: a compilable `.mist` file at `book/snippets/basics/control-flow/` with a corresponding `.cs` file generated by the SnippetEmitter tool

### Requirement 7: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating iterators and enumerables, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/Iterators/Program.mist`
2. The sample SHALL contain the proof code blocks below verbatim and produce the specified expected output
3. Each demonstrated pattern SHALL print a distinct section header (e.g., `--- Pattern Name ---`) followed by at least one iterated element value to stdout
4. The sample SHALL include a `public static void RunDemo()` method that exercises all demonstrated patterns
5. `samples/AllFeatures/Program.mist` SHALL call `Iterators.Program.RunDemo()` to include the feature in the full-suite sample run
6. The sample SHALL compile and run without errors using the Mist compiler

#### Proof Code

```mist
using Standard;

namespace Iterators;

// ─── Supporting types ────────────────────────────────────────────────────────

// PROOF: User-defined struct enumerator implementing IEnumerator<TElement>
// PROOF: MoveNext() and Current satisfy the abstract type contract
public class CountSequence : IEnumerable<int>
{
    public struct Enumerator : IEnumerator<int>
    {
        private int current;
        private int end;
        public Enumerator(int end) { current = -1; this.end = end; }
        public bool MoveNext() { current = current + 1; return current < end; }
        public int Current => current;
    }

    private int count;

    public CountSequence(int count) { this.count = count; }

    public Enumerator GetEnumerator() { return new Enumerator(count); }
}

// PROOF: User-defined class enumerator implementing IEnumerator<TElement>
// PROOF: Associated type slot satisfied by a class (not just structs)
public class Words : IEnumerable<string>
{
    public class Enumerator : IEnumerator<string>
    {
        private string[] items;
        private int index;
        public Enumerator(string[] items) { this.items = items; index = -1; }
        public bool MoveNext() { index = index + 1; return index < items.Length; }
        public string Current => items[index];
    }

    private string[] data;

    public Words(string[] data) { this.data = data; }

    public Enumerator GetEnumerator() { return new Enumerator(data); }
}

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("--- Iterators ---");

        // PROOF: foreach over intrinsic array resolves TElement from IEnumerable<TValue>
        // PROOF: Array's intrinsic GetEnumerator/MoveNext/Current drive iteration
        Console.WriteLine("--- Array Foreach ---");
        int[] numbers = new int[3];
        numbers[0] = 10;
        numbers[1] = 20;
        numbers[2] = 30;
        foreach (int n in numbers)
        {
            Console.WriteLine($"n={n}");
        }

        // PROOF: foreach over user-defined IEnumerable<int> with struct Enumerator
        // PROOF: GetEnumerator() returns concrete struct type (not boxed)
        // PROOF: Type checker resolved TElement=int from IEnumerable<int>
        Console.WriteLine("--- Struct Enumerator ---");
        var countSequence = new CountSequence(4);
        foreach (int x in countSequence)
        {
            Console.WriteLine($"x={x}");
        }

        // PROOF: foreach over user-defined IEnumerable<string> with class Enumerator
        // PROOF: Associated type slot works with class enumerators
        Console.WriteLine("--- Class Enumerator ---");
        string[] wordData = ["hello", "world", "mist"];
        var words = new Words(wordData);
        foreach (string w in words)
        {
            Console.WriteLine($"w={w}");
        }

        // PROOF: Explicit element type annotation validated by type checker
        // PROOF: foreach infers element type and checks compatibility
        Console.WriteLine("--- Explicit Type Annotation ---");
        foreach (int n in numbers)
        {
            Console.WriteLine($"typed={n}");
        }

        // PROOF: foreach with var infers element type from array
        Console.WriteLine("--- Var Foreach (Array) ---");
        foreach (var n in numbers)
        {
            Console.WriteLine($"var_n={n}");
        }

        // PROOF: foreach with var infers element type from user-defined IEnumerable<int>
        Console.WriteLine("--- Var Foreach (Struct Enumerator) ---");
        foreach (var x in countSequence)
        {
            Console.WriteLine($"var_x={x}");
        }

        // PROOF: foreach with var infers element type from IEnumerable<string>
        Console.WriteLine("--- Var Foreach (Class Enumerator) ---");
        foreach (var w in words)
        {
            Console.WriteLine($"var_w={w}");
        }

        Console.WriteLine("--- Iterators Done ---");
    }

    public static void Main(string[] args)
    {
        RunDemo();
    }
}
```

**Expected output:**
```
--- Iterators ---
--- Array Foreach ---
n=10
n=20
n=30
--- Struct Enumerator ---
x=0
x=1
x=2
x=3
--- Class Enumerator ---
w=hello
w=world
w=mist
--- Explicit Type Annotation ---
typed=10
typed=20
typed=30
--- Var Foreach (Array) ---
var_n=10
var_n=20
var_n=30
--- Var Foreach (Struct Enumerator) ---
var_x=0
var_x=1
var_x=2
var_x=3
--- Var Foreach (Class Enumerator) ---
var_w=hello
var_w=world
var_w=mist
--- Iterators Done ---
```

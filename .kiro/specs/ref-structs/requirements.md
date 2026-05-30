# Requirements Document

## Introduction

This feature adds ref struct support to the Mist programming language, enabling both declaration of ref structs in Mist source code and consumption of C# ref struct types from ambient .NET assemblies. Ref structs are stack-only value types in C# (marked with `IsByRefLike`) that cannot be boxed, stored on the heap, or used as generic type arguments. Key examples include `Span<T>` and `ReadOnlySpan<T>`.

Currently, the Mist compiler's Visibility Filter excludes ref struct types entirely, hiding them and any members whose signatures reference them. This feature lifts that exclusion and introduces a `ref struct` declaration syntax in Mist, allowing developers to both define their own ref structs and consume ambient ones like `Span<T>`.

Ref structs — whether declared in Mist or projected from ambient assemblies — are usable as local variable types, parameter types, and return types, but cannot appear as field types (in non-ref-struct types), property types (in non-ref-struct types), generic type arguments, union type constituents, or optional types. The compiler enforces these restrictions statically. Ref structs may contain fields of other ref struct types (since the containing type is also stack-only). Ref structs may declare interface implementations, but ref struct values cannot be assigned to interface-typed variables or passed as interface-typed parameters (since that would require boxing).

The feature also synthesizes `IEnumerable<T>` on ambient types that duck-type to the enumerable pattern (i.e., have `GetEnumerator()`/`MoveNext()`/`Current`), enabling `foreach` over types like `Span<T>` that use C#'s duck-typed iteration protocol.

As proof that ref structs work end-to-end, the sample application demonstrates both declaring a custom ref struct in Mist and consuming `Span<T>` and `ReadOnlySpan<T>` from the .NET runtime.

## Glossary

- **Ref_Struct**: A value type declared with the `ref struct` modifier, indicating it can only live on the stack and cannot be boxed, stored in heap-allocated fields (of non-ref-struct types), or used as a generic type argument.
- **Visibility_Filter**: The Mist compiler component that determines which CLR types and members are projected into Mist's type system from ambient assemblies.
- **Assembly_Type_Provider**: The Mist compiler component that loads .NET assemblies and projects CLR types into Mist's semantic model.
- **Parser**: The Mist compiler component that transforms a token stream into a syntax tree.
- **Type_Checker**: The Mist compiler component that validates type correctness of expressions and statements.
- **Emitter**: The Mist compiler backend that generates C# source code from the Mist semantic model.
- **Semantic_Model**: The Mist compiler component that builds type information, resolves symbols, and validates constraints.
- **Span**: `System.Span<T>`, a ref struct representing a contiguous region of arbitrary memory with read-write access.
- **ReadOnlySpan**: `System.ReadOnlySpan<T>`, a ref struct representing a contiguous region of arbitrary memory with read-only access.
- **Stack_Only_Constraint**: The restriction that a ref struct value can only exist as a local variable, parameter, return value, or field of another ref struct — never in heap-allocated storage of a non-ref-struct type, as a generic type argument, or in a union/optional type.
- **Standard_Library**: The built-in Mist library at `Source/Standard/` providing core types and utilities.
- **Sample_Application**: A demonstration program at `samples/RefStructs/Program.mist`.
- **TextMate_Grammar**: The VS Code syntax highlighting grammar at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`.
- **Synthesized_Interface**: An interface added to an ambient type's Mist representation when the type duck-types to the interface's pattern (e.g., has `GetEnumerator()`/`MoveNext()`/`Current` matching `IEnumerable<T>`). The synthesis exists only in Mist's type system and is never emitted to C#.

## Requirements

### Requirement 1: Ref Struct Declaration Syntax

**User Story:** As a Mist developer, I want to declare ref structs in Mist source code using `ref struct` syntax, so that I can define my own stack-only value types.

#### Acceptance Criteria

1. WHEN a struct declaration is immediately preceded by the `ref` modifier (i.e., `ref struct Name { ... }`), THE Parser SHALL parse the declaration as a ref struct node in the syntax tree with a flag or node variant that distinguishes it from a regular struct declaration
2. THE Parser SHALL support `ref struct` declarations with all existing struct features: fields, methods, properties, constructors, indexers, interface implementations (e.g., `ref struct Foo : IBar`), and generic type parameters
3. WHEN the Semantic_Model processes a `ref struct` declaration, THE Semantic_Model SHALL register the type as a struct with the `IsRefStruct` flag set to true
4. THE Parser SHALL support access modifiers (`public`, `private`) on ref struct declarations in any order relative to `ref` (e.g., both `public ref struct` and `ref public struct` are valid), following the same rules as regular struct declarations
5. IF the `ref` modifier appears immediately before a `class` declaration (i.e., `ref class`), THEN THE Parser SHALL emit a diagnostic error indicating that `ref` is only valid on struct declarations
6. IF the `ref` modifier appears on a struct declaration combined with the `readonly` modifier (i.e., `readonly ref struct` or `ref readonly struct`), THEN THE Parser SHALL parse the declaration as a readonly ref struct, setting both the readonly and ref struct flags

### Requirement 2: Ref Struct Type Projection from Ambient Assemblies

**User Story:** As a Mist developer, I want the compiler to project ref struct types from .NET assemblies into Mist's type system, so that I can use types like `Span<T>` and `ReadOnlySpan<T>` in my programs.

#### Acceptance Criteria

1. WHEN the Assembly_Type_Provider encounters a CLR type marked with `IsByRefLike` (a ref struct), THE Assembly_Type_Provider SHALL project that type into the Mist semantic model as a struct type with the `IsRefStruct` flag set to true
2. THE Visibility_Filter SHALL no longer exclude ref struct types from projection — ref struct types that are public SHALL be projected like regular struct types
3. WHEN a ref struct type has public methods, properties, fields, indexers, or constructors, THE Assembly_Type_Provider SHALL project those members following the same rules as regular struct members (subject to existing exclusion rules: out parameters, ref parameters excluding in, ref return types, pointer types, non-public accessibility, and events)
4. WHEN a ref struct type has generic type parameters (e.g., `Span<T>`), THE Assembly_Type_Provider SHALL project the type as a generic struct with the `IsRefStruct` flag, preserving the type parameter
5. WHEN a member's signature references a ref struct type in any position (parameter types, return type, property type, field type, or generic type argument), THE Visibility_Filter SHALL no longer exclude that member — the member SHALL be projected with the ref struct type in its signature

### Requirement 3: Stack-Only Usage Restrictions

**User Story:** As a Mist developer, I want the compiler to enforce ref struct stack-only constraints at compile time, so that I cannot accidentally store ref struct values in heap-allocated locations.

#### Acceptance Criteria

1. THE Type_Checker SHALL permit ref struct types as the declared type of local variables
2. THE Type_Checker SHALL permit ref struct types as method parameter types
3. THE Type_Checker SHALL permit ref struct types as method return types
4. THE Type_Checker SHALL permit ref struct types as field types within another ref struct declaration (since the containing type is also stack-only)
5. IF a ref struct type is used as a field type in a non-ref-struct class or struct declaration, THEN THE Type_Checker SHALL emit a diagnostic error indicating that ref struct types cannot be stored in fields of heap-allocated types
6. IF a ref struct type is used as a property type in a non-ref-struct class or struct declaration, THEN THE Type_Checker SHALL emit a diagnostic error indicating that ref struct types cannot be stored in properties of heap-allocated types
7. IF a ref struct type is used as a generic type argument (e.g., `List<Span<int>>`), THEN THE Type_Checker SHALL emit a diagnostic error indicating that ref struct types cannot be used as generic type arguments
8. IF a ref struct type is used as a type argument to a union type (e.g., `Span<int> | string`), THEN THE Type_Checker SHALL emit a diagnostic error indicating that ref struct types cannot participate in union types
9. IF a ref struct type is used with the optional suffix (e.g., `Span<int>?`), THEN THE Type_Checker SHALL emit a diagnostic error indicating that ref struct types cannot be optional (since optional maps to `Nullable<T>` which requires boxing)
10. THE Type_Checker SHALL permit ref struct declarations to include interface implementations in their declaration (e.g., `ref struct Foo : IBar` is valid syntax and semantically accepted)
11. IF a ref struct value is assigned to a variable whose type is an interface type, or passed as an argument to a parameter whose type is an interface type, THEN THE Type_Checker SHALL emit a diagnostic error indicating that ref struct values cannot be converted to interface types (since that would require boxing)
12. THE Type_Checker SHALL enforce the interface-usage restriction at the usage site (assignment or cast to an interface type), not at the declaration site — declaring that a ref struct implements an interface is permitted

### Requirement 4: Ref Struct Member Access

**User Story:** As a Mist developer, I want to access members (methods, properties, indexers) on ref struct values, so that I can use the full public API of types like `Span<T>`.

#### Acceptance Criteria

1. WHEN a local variable, parameter, or expression has a ref struct type, THE Type_Checker SHALL permit member access (method calls, property reads, property writes, indexer reads, indexer writes) on that value following the same rules as regular struct member access
2. WHEN a ref struct type has a `Length` or `Count` property, THE Type_Checker SHALL resolve property access on ref struct values using the same mechanism as regular struct property access
3. WHEN a ref struct type has an indexer (e.g., `Span<T>.this[int]`), THE Type_Checker SHALL resolve both indexer read access and indexer write access (assignment through the indexer) on ref struct values using the same mechanism as regular struct indexer access
4. WHEN a method on a ref struct type returns another ref struct type, THE Type_Checker SHALL permit the return value to be stored in a local variable, used in a chained member access expression, or passed as an argument to a method that accepts that ref struct type as a parameter
5. WHEN a method on a ref struct type returns a non-ref-struct type, THE Type_Checker SHALL treat the return value as a regular value with no stack-only restrictions

### Requirement 5: C# Code Generation for Ref Structs

**User Story:** As a Mist developer, I want the compiler to generate correct C# code for ref struct declarations and usage, so that the compiled output runs correctly on the .NET runtime.

#### Acceptance Criteria

1. WHEN emitting a Mist-declared ref struct, THE Emitter SHALL emit a C# `ref struct` declaration with the same name, fields, methods, properties, constructors, and indexers
2. WHEN emitting a local variable declaration with a ref struct type (whether from an ambient assembly or declared in Mist), THE Emitter SHALL emit the fully-qualified C# type name (e.g., `System.Span<int>`) or the Mist-declared type name as the variable's type
3. WHEN emitting a method parameter with a ref struct type, THE Emitter SHALL emit the appropriate C# type name as the parameter's type
4. WHEN emitting a method return type that is a ref struct, THE Emitter SHALL emit the appropriate C# type name as the return type
5. WHEN emitting member access on a ref struct value (method calls, property access, indexer access), THE Emitter SHALL emit standard C# member access syntax identical to regular struct member access
6. WHEN emitting a Mist-declared ref struct that contains fields of other ref struct types, THE Emitter SHALL emit those fields with their ref struct type names in the C# output

### Requirement 6: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want `ref struct` declarations to be syntax-highlighted correctly, so that I get the same visual cues as other type declarations.

#### Acceptance Criteria

1. WHEN a `ref struct` declaration is encountered, THE TextMate_Grammar SHALL assign `ref` the scope `storage.modifier.mist` and `struct` the scope `keyword.declaration.mist`
2. WHEN a `ref struct` declaration is encountered, THE TextMate_Grammar SHALL highlight the identifier following `ref struct` as a type name with scope `entity.name.type.struct.mist`
3. WHEN a generic `ref struct` declaration is encountered (e.g., `ref struct Span<T>`), THE TextMate_Grammar SHALL highlight `ref` as `storage.modifier.mist`, `struct` as `keyword.declaration.mist`, and the identifier as `entity.name.type.mist`
4. THE TextMate_Grammar SHALL NOT change the scoping of `ref` when it appears outside of a `ref struct` declaration (e.g., `ref` parameters) — the existing `storage.modifier.mist` scope for standalone `ref` SHALL remain unchanged

### Requirement 7: Book Documentation Update

**User Story:** As a Mist developer reading the language book, I want the documentation to explain ref structs — both declaration and consumption from .NET — so that I understand what is available and what restrictions apply.

#### Acceptance Criteria

1. THE book's .NET interop chapter (`book/src/advanced/dotnet-interop.md`) SHALL replace the `ref struct` row in the "What Gets Hidden" table with a new row in the "CLR-to-Mist Type Mapping" table (or an addition to the "What Gets Hidden" section) indicating that ref struct types are now projected as `StructType` with stack-only restrictions, and SHALL add a cross-reference link to the ref struct subsection in the Object Model chapter for the full restriction list
2. THE book's Object Model section SHALL add a subsection titled "Ref Structs" after the "When to Use Structs vs Classes" section in `book/src/object-model/structs.md` explaining: the `ref struct` declaration syntax, stack-only lifetime semantics (the value cannot escape to the heap), permitted operations (local variables, parameters, return types, fields in other ref structs, member access), and prohibited operations (fields in non-ref-struct types, properties in non-ref-struct types, generic type arguments, union types, optional types, interface implementations)
3. THE book's ref struct subsection SHALL include a snippet demonstrating a Mist-declared ref struct with at least 1 field and at least 1 method, using the tabbed include pattern (`{{#include}}` references to `book/snippets/object-model/structs/ref-struct.mist` and `book/snippets/object-model/structs/ref-struct.cs`), with the `.cs` file generated by running `dotnet run --project tools/SnippetEmitter`
4. THE book snippets SHALL follow the book snippet policy: the `.mist` file at `book/snippets/object-model/structs/ref-struct.mist` SHALL be a complete compilable program, and the corresponding `.cs` file SHALL exist at `book/snippets/object-model/structs/ref-struct.cs` and be generated by the SnippetEmitter tool without errors

### Requirement 8: CSHARP_FEATURES.md Update

**User Story:** As a Mist developer reviewing the feature coverage matrix, I want the `CSHARP_FEATURES.md` document to reflect that ref structs are now supported, so that the document accurately tracks compiler capabilities.

#### Acceptance Criteria

1. THE `CSHARP_FEATURES.md` document SHALL contain a row in the "Unsafe / Low-Level" table where the Feature column is `ref struct`, the Status column is `✅`, and the Notes column is `Declaration and consumption; stack-only restrictions enforced`
2. THE `CSHARP_FEATURES.md` document SHALL contain a row in the "Unsafe / Low-Level" table where the Feature column is `` `Span<T>` / `Memory<T>` ``, the Status column is `✅`, and the Notes column is `Projected from ambient assemblies as ref struct types`
3. WHEN the `ref struct` and `Span<T> / Memory<T>` rows are updated, THE `CSHARP_FEATURES.md` document SHALL retain all other rows in the "Unsafe / Low-Level" section unchanged

### Requirement 9: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating ref struct declaration and usage, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/RefStructs/Program.mist` with a corresponding `samples/RefStructs/mist.toml` project file whose `name` field is set to `"RefStructs"`
2. The sample SHALL contain the proof code blocks below verbatim and produce the specified expected output, matched line-by-line exactly
3. Each demonstrated pattern SHALL print a section header prefixed with `---` followed by result values to stdout, matching the format shown in the expected output
4. The sample SHALL include a `public static void RunDemo()` method that exercises all demonstrated patterns
5. `samples/AllFeatures/Program.mist` SHALL call `RefStructs.Program.RunDemo()` and `samples/AllFeatures/mist.toml` SHALL include a reference entry `RefStructs = { path = "../RefStructs" }` to include the feature in the full-suite sample run
6. The sample SHALL compile with zero diagnostics and run with a zero exit code using the Mist compiler

#### Proof Code

```mist
using Standard;

namespace RefStructs;

// PROOF: ref struct declaration syntax is parsed and compiled
// PROOF: ref struct can have fields, constructors, methods, and properties
public ref struct IntPair
{
    public int first;
    public int second;

    public IntPair(int a, int b)
    {
        this.first = a;
        this.second = b;
    }

    public int Sum()
    {
        return this.first + this.second;
    }

    public int Max
    {
        get
        {
            if (this.first > this.second)
            {
                return this.first;
            }
            return this.second;
        }
    }
}

// PROOF: ref struct can contain fields of other ref struct types
public ref struct NestedRefStruct
{
    public IntPair pair;
    public int extra;

    public NestedRefStruct(int a, int b, int c)
    {
        this.pair = new IntPair(a, b);
        this.extra = c;
    }

    public int Total()
    {
        return this.pair.Sum() + this.extra;
    }
}

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("=== RefStructs Sample ===");

        // PROOF: Mist-declared ref struct usable as local variable
        // PROOF: Constructor, field access, method call, property access all work
        Console.WriteLine("--- Mist Declared Ref Struct ---");
        var pair = new IntPair(3, 7);
        Console.WriteLine($"pair.first={pair.first}");
        Console.WriteLine($"pair.second={pair.second}");
        Console.WriteLine($"pair.Sum()={pair.Sum()}");
        Console.WriteLine($"pair.Max={pair.Max}");

        // PROOF: Ref struct containing another ref struct field
        Console.WriteLine("--- Nested Ref Struct ---");
        var nested = new NestedRefStruct(10, 20, 5);
        Console.WriteLine($"nested.pair.first={nested.pair.first}");
        Console.WriteLine($"nested.pair.second={nested.pair.second}");
        Console.WriteLine($"nested.extra={nested.extra}");
        Console.WriteLine($"nested.Total()={nested.Total()}");

        // PROOF: Ref struct as method parameter and return type
        Console.WriteLine("--- Ref Struct Parameters ---");
        var p = new IntPair(100, 200);
        int sum = AddPair(p);
        Console.WriteLine($"AddPair={sum}");
        var doubled = DoublePair(p);
        Console.WriteLine($"doubled.first={doubled.first}");
        Console.WriteLine($"doubled.second={doubled.second}");

        // PROOF: Span<int> projected from ambient assembly, usable as local variable
        // PROOF: Array.AsSpan() extension method is no longer filtered
        Console.WriteLine("--- Span from Array ---");
        int[] numbers = new int[5];
        numbers[0] = 10;
        numbers[1] = 20;
        numbers[2] = 30;
        numbers[3] = 40;
        numbers[4] = 50;
        Span<int> span = numbers.AsSpan();
        Console.WriteLine($"span.Length={span.Length}");
        Console.WriteLine($"span[0]={span[0]}");
        Console.WriteLine($"span[4]={span[4]}");

        // PROOF: ReadOnlySpan<T> projected and usable as local variable
        // PROOF: string.AsSpan() returns ReadOnlySpan<char> — no longer filtered
        Console.WriteLine("--- ReadOnlySpan from String ---");
        string greeting = "Hello, Mist!";
        ReadOnlySpan<char> chars = greeting.AsSpan();
        Console.WriteLine($"chars.Length={chars.Length}");
        Console.WriteLine($"chars[0]={chars[0]}");
        Console.WriteLine($"chars[6]={chars[6]}");

        // PROOF: Span<T> indexer write works
        // PROOF: Writes through span mutate the underlying array
        Console.WriteLine("--- Span Indexer Write ---");
        Span<int> writeSpan = numbers.AsSpan();
        writeSpan[0] = 99;
        Console.WriteLine($"writeSpan[0]={writeSpan[0]}");
        Console.WriteLine($"numbers[0]={numbers[0]}");

        // PROOF: Span<T>.Slice() returns another Span<T> — ref struct return type works
        // PROOF: Chained member access on ref struct return values
        Console.WriteLine("--- Span Slice ---");
        Span<int> sliced = writeSpan.Slice(1, 3);
        Console.WriteLine($"sliced.Length={sliced.Length}");
        Console.WriteLine($"sliced[0]={sliced[0]}");
        Console.WriteLine($"sliced[2]={sliced[2]}");

        // PROOF: Span<T> is foreachable via synthesized IEnumerable<T>
        // PROOF: foreach lowering calls GetEnumerator() on concrete Span<T> type
        Console.WriteLine("--- Span Foreach ---");
        int[] foreachNums = new int[3];
        foreachNums[0] = 100;
        foreachNums[1] = 200;
        foreachNums[2] = 300;
        Span<int> foreachSpan = foreachNums.AsSpan();
        foreach (int val in foreachSpan)
        {
            Console.WriteLine($"val={val}");
        }

        Console.WriteLine("=== RefStructs Done ===");
    }

    // PROOF: Ref struct usable as parameter type in user-defined method
    public static int AddPair(IntPair p)
    {
        return p.first + p.second;
    }

    // PROOF: Ref struct usable as return type in user-defined method
    public static IntPair DoublePair(IntPair p)
    {
        return new IntPair(p.first * 2, p.second * 2);
    }

    public static void Main(string[] args)
    {
        RunDemo();
    }
}
```

**Expected output:**
```
=== RefStructs Sample ===
--- Mist Declared Ref Struct ---
pair.first=3
pair.second=7
pair.Sum()=10
pair.Max=7
--- Nested Ref Struct ---
nested.pair.first=10
nested.pair.second=20
nested.extra=5
nested.Total()=35
--- Ref Struct Parameters ---
AddPair=300
doubled.first=200
doubled.second=400
--- Span from Array ---
span.Length=5
span[0]=10
span[4]=50
--- ReadOnlySpan from String ---
chars.Length=12
chars[0]=H
chars[6]= 
--- Span Indexer Write ---
writeSpan[0]=99
numbers[0]=99
--- Span Slice ---
sliced.Length=3
sliced[0]=20
sliced[2]=40
--- Span Foreach ---
val=100
val=200
val=300
=== RefStructs Done ===
```


### Requirement 10: Synthesized IEnumerable<T> for Duck-Typed Ambient Types

**User Story:** As a Mist developer, I want the compiler to synthesize `IEnumerable<T>` on ambient types that duck-type to the enumerable pattern, so that I can use `foreach` over types like `Span<T>` without requiring the type to explicitly implement the interface.

#### Acceptance Criteria

1. WHEN the Assembly_Type_Provider projects an ambient type that does NOT implement `IEnumerable<T>` but DOES have a public `GetEnumerator()` method whose return type has a public `bool MoveNext()` method and a public `Current` property, THE Assembly_Type_Provider SHALL synthesize `IEnumerable<T>` on the projected type's interface list
2. THE Assembly_Type_Provider SHALL resolve `TElement` for the synthesized `IEnumerable<T>` from the `Current` property's return type on the enumerator type returned by `GetEnumerator()`
3. THE Assembly_Type_Provider SHALL resolve the associated enumerator type for the synthesized `IEnumerable<T>` from the return type of `GetEnumerator()`
4. THE Synthesized_Interface SHALL exist only in Mist's type system — the Emitter SHALL NOT emit the synthesized `IEnumerable<T>` back to C# in any generated code
5. WHEN a `foreach` statement iterates over a value whose type has a synthesized `IEnumerable<T>`, THE Emitter SHALL emit a C# `foreach` that calls `GetEnumerator()` directly on the concrete type (not through the interface), which C#'s duck-typing handles correctly
6. WHEN an ambient type already explicitly implements `IEnumerable<T>`, THE Assembly_Type_Provider SHALL NOT synthesize an additional `IEnumerable<T>` — the explicit implementation takes precedence

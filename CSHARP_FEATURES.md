# C# Feature Coverage

Tracks which C# language features the Mist compiler supports, which are remaining, and which are intentionally skipped.

Legend: ✅ Implemented | 🔲 Remaining | ⛔ Skipped

---

## Types

### Value Types

| Feature | Status | Notes |
|---|---|---|
| `int` | ✅ | |
| `uint` | ✅ | |
| `long` | ✅ | |
| `ulong` | ✅ | |
| `short` | ✅ | |
| `ushort` | ✅ | |
| `byte` | ✅ | |
| `sbyte` | ✅ | |
| `float` | ✅ | |
| `double` | ✅ | |
| `decimal` | ✅ | |
| `bool` | ✅ | |
| `char` | ✅ | |
| `nint` / `nuint` | 🔲 | Native-sized integers (C# 9) |
| User-defined structs | ✅ | |
| Enums | 🔲 | |
| Nullable value types (`int?`) | 🔲 | |
| Tuples (`(int, string)`) | 🔲 | |

### Reference Types

| Feature | Status | Notes |
|---|---|---|
| `string` | ✅ | |
| `object` | 🔲 | |
| `dynamic` | ⛔ | DLR feature, out of scope |
| User-defined classes | ✅ | |
| Single-dimensional arrays (`T[]`) | ✅ | |
| Multi-dimensional arrays (`T[,]`) | 🔲 | |
| Jagged arrays (`T[][]`) | 🔲 | |
| Nullable reference types (`string?`) | 🔲 | |
| Records | 🔲 | C# 9 |
| Record structs | 🔲 | C# 10 |

---

## Type Declarations

| Feature | Status | Notes |
|---|---|---|
| Classes | ✅ | |
| Structs | ✅ | |
| Interfaces | ✅ | |
| Enums | 🔲 | |
| Delegates | 🔲 | |
| Records | 🔲 | C# 9 |
| Record structs | 🔲 | C# 10 |
| Abstract classes | 🔲 | |
| Sealed classes | 🔲 | |
| Static classes | 🔲 | |
| Partial classes | 🔲 | |
| Nested types | 🔲 | |
| Traits (structural typing) | ✅ | Structural typing via `trait` declarations |
| Generic type declarations | ✅ | |
| Generic constraints (`where T : ...`) | 🔲 | |

---

## Members

### Fields

| Feature | Status | Notes |
|---|---|---|
| Instance fields | ✅ | |
| Static fields | ✅ | Via `static` modifier |
| Field initializers | ✅ | |
| `const` fields | 🔲 | |
| `readonly` fields | 🔲 | |
| `volatile` fields | ⛔ | Low-level threading, out of scope |
| `required` fields | 🔲 | C# 11 |

### Properties

| Feature | Status | Notes |
|---|---|---|
| Auto-properties | ✅ | `get; set;` with optional initializers |
| Get/set accessors | ✅ | Auto, block-body, and expression-bodied forms |
| Init-only setters (`init`) | ✅ | C# 9; auto, block, and expression forms |
| Expression-bodied properties | ✅ | `Type Name => expr;` |
| Static properties | ✅ | Via `static` modifier |
| Accessor modifiers | ✅ | e.g., `private set;` |
| `field` keyword | ✅ | Backing field access in accessor bodies |
| Computed properties | ✅ | Get-only with body, no backing field |
| Interface properties | ✅ | `get;` and/or `set;` signatures |
| Indexers | ✅ | `this[ParamType param]` with accessors |
| Abstract/virtual properties | 🔲 | |

### Methods

| Feature | Status | Notes |
|---|---|---|
| Instance methods | ✅ | |
| Static methods | ✅ | Via `static` modifier |
| Method parameters | ✅ | |
| Return types | ✅ | |
| `void` return | ✅ | |
| Expression-bodied methods (`=>`) | 🔲 | |
| Method overloading | 🔲 | |
| `params` arrays | 🔲 | |
| Optional parameters (default values) | 🔲 | |
| Named arguments | 🔲 | |
| `ref` parameters | 🔲 | |
| `out` parameters | 🔲 | |
| `in` parameters | 🔲 | |
| `ref readonly` parameters | 🔲 | C# 12 |
| Extension methods | 🔲 | |
| Async methods (`async`/`await`) | 🔲 | |
| Iterator methods (`yield`) | 🔲 | |
| Local functions | 🔲 | C# 7 |
| Static local functions | 🔲 | C# 8 |

### Constructors

| Feature | Status | Notes |
|---|---|---|
| Instance constructors | ✅ | |
| Constructor chaining (`: this(...)`) | ✅ | |
| Base constructor calls (`: base(...)`) | ✅ | |
| Static constructors | 🔲 | |
| Primary constructors | 🔲 | C# 12 |
| Parameterless struct constructors | 🔲 | C# 10 |

### Other Members

| Feature | Status | Notes |
|---|---|---|
| Destructors / Finalizers | 🔲 | |
| Operator overloading | 🔲 | |
| Implicit/explicit conversion operators | ✅ | |
| Events | 🔲 | |
| Deconstruct methods | 🔲 | |

---

## Access Modifiers

| Feature | Status | Notes |
|---|---|---|
| `public` | ✅ | |
| `private` | ✅ | |
| `static` | ✅ | |
| `protected` | 🔲 | |
| `internal` | 🔲 | |
| `protected internal` | 🔲 | |
| `private protected` | 🔲 | |
| `abstract` | ⛔ | |
| `virtual` | ⛔ | |
| `override` | ⛔ | |
| `sealed` | ⛔ | |
| `new` (member hiding) | ⛔ | |
| `extern` | ⛔ | P/Invoke, out of scope |
| `unsafe` | ⛔ | Pointer operations, out of scope |
| `readonly` (on members) | 🔲 | |
| `required` | 🔲 | C# 11 |
| `file` (file-scoped types) | 🔲 | C# 11 |

---

## Statements

### Declaration Statements

| Feature | Status | Notes |
|---|---|---|
| Typed variable declarations | ✅ | `int x = 5;` |
| `var` (type inference) | ✅ | `var x = 5;` |
| Multiple declarators (`int x, y;`) | ⛔ | |
| `const` locals | 🔲 | |
| Ref locals (`ref int x = ...`) | 🔲 | |
| Pattern-based declarations | 🔲 | |
| Using declarations (`using var x = ...`) | 🔲 | C# 8 |
| Scoped locals (`scoped`) | 🔲 | C# 11 |

### Control Flow

| Feature | Status | Notes |
|---|---|---|
| `if` / `else if` / `else` | ✅ | |
| `while` | ✅ | |
| `for` | ✅ | With initializer, condition, iterator |
| `foreach` / `in` | ✅ | |
| `break` | ✅ | |
| `return` (with/without value) | ✅ | |
| `do...while` | 🔲 | |
| `switch` statement | 🔲 | |
| `switch` expression | 🔲 | C# 8 |
| `continue` | 🔲 | |
| `goto` | ⛔ | Intentionally excluded |
| `throw` statement | 🔲 | |
| `throw` expression | 🔲 | C# 7 |
| Labeled statements | 🔲 | |

### Exception Handling

| Feature | Status | Notes |
|---|---|---|
| `try` / `catch` / `finally` | 🔲 | |
| `catch` with exception filters (`when`) | 🔲 | C# 6 |
| `using` statement (resource management) | 🔲 | |
| `using` declaration | 🔲 | C# 8 |

### Other Statements

| Feature | Status | Notes |
|---|---|---|
| Expression statements | ✅ | |
| `lock` statement | 🔲 | |
| `checked` / `unchecked` | ⛔ | Overflow control, low priority |
| `fixed` statement | ⛔ | Pointer pinning, out of scope |
| `yield return` / `yield break` | 🔲 | |
| `await foreach` | 🔲 | C# 8 |
| `await using` | 🔲 | C# 8 |

---

## Expressions

### Literals

| Feature | Status | Notes |
|---|---|---|
| Integer literals | ✅ | With suffixes (L, U, UL, etc.) |
| Hex literals (`0xFF`) | ✅ | |
| Binary literals (`0b1010`) | ✅ | C# 7; with digit separators |
| Float/double/decimal literals | ✅ | With suffixes (f, d, m) |
| String literals | ✅ | With escape sequences |
| Interpolated strings (`$"..."`) | ✅ | With expression holes |
| Verbatim strings (`@"..."`) | 🔲 | |
| Raw string literals (`"""..."""`) | 🔲 | C# 11 |
| Char literals | ✅ | With escape sequences |
| `true` / `false` | ✅ | |
| `null` | ⛔ | |
| `default` / `default(T)` | 🔲 | |
| `typeof(T)` | ⛔ | |
| `nameof(x)` | 🔲 | C# 6 |
| `sizeof(T)` | ⛔ | Low-level, out of scope |
| Digit separators (`1_000_000`) | ✅ | C# 7; in all numeric literal types |
| UTF-8 string literals (`"..."u8`) | ⛔ | C# 11, low-level |

### Arithmetic Operators

| Feature | Status | Notes |
|---|---|---|
| `+` (addition) | ✅ | Also string concatenation |
| `-` (subtraction) | ✅ | |
| `*` (multiplication) | ✅ | |
| `/` (division) | ✅ | |
| `%` (modulo) | ✅ | |
| Unary `-` (negation) | ✅ | |
| Unary `+` | 🔲 | |
| `++` / `--` (increment/decrement) | 🔲 | |

### Comparison Operators

| Feature | Status | Notes |
|---|---|---|
| `==` | ✅ | |
| `!=` | ✅ | |
| `<` | ✅ | |
| `>` | ✅ | |
| `<=` | ✅ | |
| `>=` | ✅ | |

### Logical Operators

| Feature | Status | Notes |
|---|---|---|
| `&&` (logical AND) | ✅ | |
| `\|\|` (logical OR) | ✅ | |
| `!` (logical NOT) | ✅ | |

### Bitwise Operators

| Feature | Status | Notes |
|---|---|---|
| `&` (bitwise AND) | 🔲 | |
| `\|` (bitwise OR) | 🔲 | |
| `^` (bitwise XOR) | 🔲 | |
| `~` (bitwise NOT) | 🔲 | |
| `<<` (left shift) | 🔲 | |
| `>>` (right shift) | 🔲 | |
| `>>>` (unsigned right shift) | 🔲 | C# 11 |

### Assignment Operators

| Feature | Status | Notes |
|---|---|---|
| `=` | ✅ | |
| `+=`, `-=`, `*=`, `/=`, `%=` | 🔲 | |
| `&=`, `\|=`, `^=` | 🔲 | |
| `<<=`, `>>=`, `>>>=` | 🔲 | |
| `??=` | 🔲 | C# 8 |

### Member Access

| Feature | Status | Notes |
|---|---|---|
| `.` (member access) | ✅ | |
| Method invocation | ✅ | |
| Array indexing (`a[i]`) | ✅ | |
| Direct invocation (`func(args)`) | ✅ | |
| `?.` (null-conditional access) | 🔲 | C# 6 |
| `?[]` (null-conditional indexing) | 🔲 | C# 6 |
| `!` (null-forgiving) | 🔲 | C# 8 |

### Object Creation

| Feature | Status | Notes |
|---|---|---|
| `new T(args)` | ✅ | |
| `new T[size]` | ✅ | |
| Array initializers (`new[] { 1, 2 }`) | 🔲 | |
| Collection initializers | 🔲 | |
| Object initializers (`new T { P = v }`) | 🔲 | |
| Target-typed `new()` | 🔲 | C# 9 |
| `with` expressions (records) | 🔲 | C# 9 |
| Collection expressions (`[1, 2, 3]`) | 🔲 | C# 12 |

### Other Expressions

| Feature | Status | Notes |
|---|---|---|
| `this` | ✅ | |
| Parenthesized expressions | ✅ | |
| `base` | 🔲 | Requires inheritance |
| Ternary conditional (`? :`) | 🔲 | |
| Null-coalescing (`??`) | 🔲 | |
| `is` type check | 🔲 | |
| `as` type cast | 🔲 | |
| Cast expressions (`(T)expr`) | ✅ | |
| Lambda expressions (`=>`) | 🔲 | |
| Anonymous methods (`delegate { }`) | ⛔ | Superseded by lambdas |
| LINQ query expressions | 🔲 | |
| `await` | 🔲 | |
| `stackalloc` | ⛔ | Low-level, out of scope |
| Range expressions (`..`) | 🔲 | C# 8 |
| Index expressions (`^`) | 🔲 | C# 8 |
| `is` pattern matching | 🔲 | C# 7+ |
| `switch` expression | 🔲 | C# 8 |
| Discard (`_`) | 🔲 | C# 7 |
| String interpolation (raw) | 🔲 | C# 11 |
| Interpolated string handlers | ⛔ | C# 10, compiler internals |

---

## Inheritance & Polymorphism

| Feature | Status | Notes |
|---|---|---|
| Class inheritance (`: BaseClass`) | ✅ | |
| Interface implementation (`: IFoo`) | ✅ | |
| Multiple interface implementation | ✅ | |
| `virtual` methods | 🔲 | |
| `override` methods | 🔲 | |
| `abstract` methods | 🔲 | |
| `sealed` override | 🔲 | |
| `base` member access | 🔲 | |
| Covariant return types | 🔲 | C# 9 |
| Default interface methods | 🔲 | C# 8 |
| Static abstract interface members | 🔲 | C# 11 |

---

## Structural Typing

Mist introduces traits as a structural typing mechanism. Unlike interfaces (nominal typing), traits use late-bound structural matching: any type whose public members match the trait's declared members automatically satisfies the trait, with no explicit declaration required.

### Trait Declarations

| Feature | Status | Notes |
|---|---|---|
| `trait` keyword | ✅ | `trait Name { ... }` |
| Trait field members | ✅ | `type name;` — matched by public fields or properties |
| Trait method members | ✅ | `returnType name(params);` — matched by name, return type, and parameter types |
| Trait property members | ✅ | `type name { get; set; }` — matched by name, type, and accessors |
| Empty traits | ✅ | Traits with no members |

### Structural Matching

| Feature | Status | Notes |
|---|---|---|
| Automatic satisfaction | ✅ | Types satisfy traits by having matching public members |
| Field-to-property matching | ✅ | Trait fields can be matched by public properties |
| Visibility constraint | ✅ | Only public members participate in matching |
| Trait assignability | ✅ | Trait-typed variables accept any satisfying type |
| Trait member access | ✅ | Member access on trait-typed variables resolves against trait scope |

### `with` Blocks

| Feature | Status | Notes |
|---|---|---|
| `with` declarations | ✅ | `with TypeName : TraitName { ... }` |
| Member mapping (`as`) | ✅ | `sourceName as targetName;` |
| Combined satisfaction | ✅ | Structural match + `with` mappings |
| Validation diagnostics | ✅ | Undefined types/members, type mismatches, duplicates |

### C# Lowering

| Feature | Status | Notes |
|---|---|---|
| Traits as C# interfaces | ✅ | Each trait emits a C# interface |
| Trait fields as properties | ✅ | `int x;` → `int x { get; }` |
| Inheritance list injection | ✅ | Satisfying types get trait interface added |
| Explicit implementations | ✅ | `with` mappings emit `int ITraitName.x => this.a;` |

---

## Pattern Matching

| Feature | Status | Notes |
|---|---|---|
| Type patterns (`is Type t`) | 🔲 | C# 7 |
| Constant patterns | 🔲 | C# 7 |
| `var` patterns | 🔲 | C# 7 |
| Positional patterns | 🔲 | C# 8 |
| Property patterns | 🔲 | C# 8 |
| Relational patterns (`> 0`) | 🔲 | C# 9 |
| Logical patterns (`and`, `or`, `not`) | 🔲 | C# 9 |
| List patterns (`[1, 2, ..]`) | 🔲 | C# 11 |

---

## Generics

| Feature | Status | Notes |
|---|---|---|
| Generic classes (`class Foo<T>`) | ✅ | |
| Generic methods (`void Foo<T>()`) | ✅ | |
| Generic interfaces | ✅ | |
| Type constraints (`where T : class`) | 🔲 | |
| Covariance / contravariance | 🔲 | |
| Generic delegates | 🔲 | |

---

## Async / Concurrency

| Feature | Status | Notes |
|---|---|---|
| `async` / `await` | 🔲 | |
| `Task` / `Task<T>` | 🔲 | |
| `ValueTask` / `ValueTask<T>` | 🔲 | |
| `async` streams (`IAsyncEnumerable`) | 🔲 | C# 8 |
| `lock` statement | 🔲 | |
| `Interlocked` / `volatile` | ⛔ | Low-level threading |

---

## Namespaces & Imports

| Feature | Status | Notes |
|---|---|---|
| `namespace` (block-scoped) | ✅ | |
| `namespace` (file-scoped) | ✅ | C# 10 |
| `using` directives | ✅ | |
| `using static` | 🔲 | C# 6 |
| `using` alias (`using X = Y`) | 🔲 | |
| Global using directives | 🔲 | C# 10 |
| `extern alias` | ⛔ | Assembly-level, out of scope |

---

## Comments

| Feature | Status | Notes |
|---|---|---|
| Single-line comments (`//`) | ✅ | |
| Multi-line comments (`/* */`) | 🔲 | |
| XML documentation comments (`///`) | 🔲 | |

---

## Attributes

| Feature | Status | Notes |
|---|---|---|
| Attribute declarations | 🔲 | |
| Attribute usage (`[Attr]`) | 🔲 | |
| Attribute targets (`[assembly: ...]`) | 🔲 | |
| Generic attributes | 🔲 | C# 11 |

---

## Preprocessor Directives

| Feature | Status | Notes |
|---|---|---|
| `#if` / `#else` / `#endif` | ⛔ | Preprocessor, out of scope |
| `#define` / `#undef` | ⛔ | Preprocessor, out of scope |
| `#region` / `#endregion` | ⛔ | IDE feature, out of scope |
| `#pragma` | ⛔ | Compiler hint, out of scope |
| `#nullable` | ⛔ | Preprocessor, out of scope |
| `#line` | ⛔ | Debugging, out of scope |

---

## Unsafe / Low-Level

| Feature | Status | Notes |
|---|---|---|
| `unsafe` blocks | ⛔ | Out of scope |
| Pointer types (`int*`) | ⛔ | Out of scope |
| `fixed` statement | ⛔ | Out of scope |
| `stackalloc` | ⛔ | Out of scope |
| `sizeof` | ⛔ | Out of scope |
| Function pointers | ⛔ | Out of scope |
| `Span<T>` / `Memory<T>` | ⛔ | Out of scope for v1 |
| `ref struct` | ⛔ | Out of scope for v1 |

---

## Ambient .NET Types (C# Backend)

Types available to Mist programs via the C# backend's ambient type resolver.

| Type | Status | Members |
|---|---|---|
| `System.Console` | ✅ | `WriteLine`, `ReadLine`, `Write` |
| `System.String` | ✅ | `Length`, `Substring`, `Contains` |
| `System.Math` | ✅ | `Abs`, `Max`, `Min` |
| `System.Array` | ✅ | `Length` |
| `System.Convert` | ✅ | `ToInt32`, `ToString` |

---

## Type Checking & Diagnostics

| Feature | Status | Notes |
|---|---|---|
| Type assignability checks | ✅ | |
| Arithmetic operator type checks | ✅ | Numeric types required |
| Comparison operator type checks | ✅ | |
| Logical operator type checks | ✅ | Bool required |
| String concatenation with `+` | ✅ | |
| Condition must be `bool` | ✅ | if/while/for conditions |
| Return type validation | ✅ | |
| Entry point validation (`Main`) | ✅ | `static void Main()` or `Main(string[])` |
| Duplicate declaration detection | ✅ | Parameters |
| Undefined namespace detection | ✅ | In `using` directives |
| Member access modifier checks | ✅ | Public/private visibility |
| Implicit numeric conversions | 🔲 | |
| Explicit cast validation | ✅ | |
| Null safety checks | 🔲 | |
| Overload resolution | 🔲 | |
| Generic type inference | 🔲 | |

---

## Summary

| Category | ✅ Implemented | 🔲 Remaining | ⛔ Skipped |
|---|---|---|---|
| Value Types | 14 | 4 | 0 |
| Reference Types | 4 | 6 | 1 |
| Type Declarations | 5 | 10 | 0 |
| Members — Fields | 3 | 3 | 1 |
| Members — Properties | 10 | 1 | 0 |
| Members — Methods | 5 | 12 | 0 |
| Members — Constructors | 3 | 2 | 0 |
| Members — Other | 1 | 4 | 0 |
| Access Modifiers | 3 | 11 | 2 |
| Statements — Declarations | 2 | 6 | 0 |
| Statements — Control Flow | 6 | 7 | 1 |
| Statements — Exceptions | 0 | 4 | 0 |
| Statements — Other | 1 | 5 | 2 |
| Expressions — Literals | 9 | 5 | 2 |
| Expressions — Arithmetic | 6 | 2 | 0 |
| Expressions — Comparison | 6 | 0 | 0 |
| Expressions — Logical | 3 | 0 | 0 |
| Expressions — Bitwise | 0 | 7 | 0 |
| Expressions — Assignment | 1 | 4 | 0 |
| Expressions — Member Access | 4 | 3 | 0 |
| Expressions — Object Creation | 2 | 5 | 0 |
| Expressions — Other | 3 | 13 | 2 |
| Inheritance | 3 | 8 | 0 |
| Structural Typing — Traits | 5 | 0 | 0 |
| Structural Typing — Matching | 5 | 0 | 0 |
| Structural Typing — With Blocks | 4 | 0 | 0 |
| Structural Typing — C# Lowering | 4 | 0 | 0 |
| Pattern Matching | 0 | 8 | 0 |
| Generics | 3 | 3 | 0 |
| Async | 0 | 5 | 1 |
| Namespaces & Imports | 3 | 3 | 1 |
| Comments | 1 | 2 | 0 |
| Attributes | 0 | 4 | 0 |
| Preprocessor | 0 | 0 | 6 |
| Unsafe / Low-Level | 0 | 0 | 8 |
| Ambient Types | 5 | 0 | 0 |
| Type Checking | 11 | 4 | 0 |

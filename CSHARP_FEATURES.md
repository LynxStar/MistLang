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
| Enums | ✅ | |
| Nullable value types (`int?`) | ✅ | |
| Tuples (`(int, string)`) | ✅ | |

### Reference Types

| Feature | Status | Notes |
|---|---|---|
| `string` | ✅ | |
| `object` | ⛔ | |
| `dynamic` | ⛔ | DLR feature, out of scope |
| `unknown` (Mist-specific) | ✅ | Mist top type with storage prohibition + reachability-filtered widening. Distinct from C# `object` (not a direct alias) and from C# `dynamic` (not general runtime dispatch). See "Mist-specific Features" below. |
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
| Enums | ✅ | |
| Delegates | ✅ | `Func<T, TResult>` type family with `func` keyword; no `delegate` keyword |
| Records | 🔲 | C# 9 |
| Record structs | 🔲 | C# 10 |
| Abstract classes | 🔲 | |
| Sealed classes | 🔲 | |
| Static classes | 🔲 | |
| Partial classes | 🔲 | |
| Nested types | 🔲 | |
| Traits (structural typing) | ✅ | Structural typing via `trait` declarations |
| Generic type declarations | ✅ | |
| Generic constraints (`where T : ...`) | ✅ | |

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
| Extension methods | ✅ | Via `extension(Type self) { ... }` blocks |
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
| `foreach` / `in` | 🔲 | Emitted directly — not enforced by Mist compiler |
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
| Regex literals (`` `pattern` ``) | ✅ | Backtick-delimited; maps to `Standard.Regex` (`System.Text.RegularExpressions.Regex`). Compile-time pattern validation, implicit `regex→string` conversion, explicit `string→regex` cast |
| Verbatim strings (`@"..."`) | 🔲 | |
| Raw string literals (`"""..."""`) | 🔲 | C# 11 |
| Char literals | ✅ | With escape sequences |
| `true` / `false` | ✅ | |
| `null` | ⛔ | |
| `default` / `default(T)` | ✅ | Mist divergence: value-based defaults, inferred defaults, type bindings, bare `default` literal |
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
| Target-typed `new()` | ✅ | C# 9 |
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
| `as` operator (safe type conversion) | ✅ | |
| Cast expressions (`(T)expr`) | ✅ | |
| Lambda expressions (`=>`) | ✅ | Expression and block bodies, static, async |
| Anonymous methods (`delegate { }`) | ⛔ | Superseded by lambdas |
| LINQ query expressions | 🔲 | |
| `await` | 🔲 | |
| `stackalloc` | ⛔ | Low-level, out of scope |
| Range expressions (`..`) | 🔲 | C# 8 |
| Index expressions (`^`) | 🔲 | C# 8 |
| `is` pattern matching | ✅ | C# 7+ |
| `switch` expression | 🔲 | C# 8 |
| Discard (`_`) | ✅ | C# 7 |
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
| Type patterns (`is Type t`) | ✅ | C# 7 |
| Constant patterns | ✅ | C# 7 |
| `var` patterns | ✅ | C# 7 |
| Positional patterns | 🔲 | C# 8 |
| Property patterns | ✅ | C# 8 |
| Relational patterns (`> 0`) | ✅ | C# 9 |
| Logical patterns (`and`, `or`, `not`) | ✅ | C# 9 |
| List patterns (`[1, 2, ..]`) | ✅ | C# 11 |

---

## Type Unions

Mist supports TypeScript-style type unions (`int | string`) allowing a value to be one of several types. Union types can appear in all type positions: variables, parameters, return types, fields, and type arguments. Pattern matching narrows union-typed values to specific constituents.

### Syntax & Parsing

| Feature | Status | Notes |
|---|---|---|
| `\|` operator in type contexts | ✅ | `int \| string` |
| Multi-constituent unions | ✅ | `int \| string \| bool` |
| Union types in all type positions | ✅ | Variables, parameters, return types, fields, type arguments |
| Parenthesized grouping | ✅ | `(int \| string)[]` for array of union |
| Precedence below array rank | ✅ | `int[] \| string` = `(int[]) \| string` |

### Semantic Model

| Feature | Status | Notes |
|---|---|---|
| Union type resolution | ✅ | Resolves to `UnionType` with constituent types |
| Nested union flattening | ✅ | `(int \| string) \| bool` → three constituents |
| Duplicate deduplication | ✅ | `int \| int` → `int` |
| Single-constituent reduction | ✅ | Reduces to the type itself |

### Type Checking

| Feature | Status | Notes |
|---|---|---|
| Constituent-to-union assignability | ✅ | `int` → `int \| string` |
| Union-to-wider-union assignability | ✅ | `int \| string` → `int \| string \| bool` |
| Order-independent equality | ✅ | `int \| string` = `string \| int` |
| Common member access | ✅ | Allowed if member exists on all constituents |
| Pattern matching narrowing | ✅ | `is string s` narrows to `string` |
| Non-constituent rejection | ✅ | Diagnostic MIST_TC_0050 |
| Union-to-non-union rejection | ✅ | Diagnostic MIST_TC_0051 |
| Missing member diagnostic | ✅ | Diagnostic MIST_TC_0052 |
| Non-constituent pattern warning | ✅ | Diagnostic MIST_TC_0053 |

### C# Emission

| Feature | Status | Notes |
|---|---|---|
| Helper type generation | ✅ | `Union_{Type1}_{Type2}` struct with tag + value |
| Implicit conversion operators | ✅ | From each constituent to helper type |
| Is/As properties | ✅ | `IsInt32`, `AsInt32` for type checking and extraction |
| Helper type deduplication | ✅ | Same constituents reuse same helper type |
| Union type usage emission | ✅ | Variables, parameters, return types, fields |
| Pattern matching emission | ✅ | `is T t` → discriminator check + extraction |

---

## Refinement Types

Mist supports refinement types — type aliases with pattern-match predicates that constrain valid values. A refinement type defines a subset of its base type (e.g., `type AdultAge = int >= 18;`). The compiler enforces the predicate at compile time for literals and requires pattern matching (`is` expressions) for runtime validation of non-constant values.

### Syntax & Parsing

| Feature | Status | Notes |
|---|---|---|
| Refinement predicate on type alias | ✅ | `type AdultAge = int >= 18;` |
| Compound predicates (`and`) | ✅ | `type Percentage = int >= 0 and <= 100;` |
| Union with per-constituent refinements | ✅ | `type AgeRange = int >= 3 and <= 8 \| byte >= 0 and < 64;` |
| Property pattern refinements | ✅ | `type ValidRange = Range { Start: >= 0 };` |
| Predicate binds tighter than `\|` | ✅ | Refinement attaches to single type, not entire union |

### Semantic Model

| Feature | Status | Notes |
|---|---|---|
| `RefinementType` representation | ✅ | Stores base type + predicate |
| Predicate compatibility validation | ✅ | Relational patterns require comparable base type |
| Union of refinement types | ✅ | Each constituent is a `RefinementType` |

### Type Checking

| Feature | Status | Notes |
|---|---|---|
| Compile-time literal enforcement | ✅ | Rejects literals violating predicate |
| Non-constant assignment rejection | ✅ | Requires pattern matching for runtime values |
| Widening (refinement → base) | ✅ | Always accepted (nominal subtyping) |
| Arithmetic produces base type | ✅ | Refinement erased on arithmetic |
| Subset relationship proving | ✅ | `>= 21` implies `>= 18`, compound `and` implications |
| Readonly member validation | ✅ | Property pattern members must be readonly |
| Flow-sensitive narrowing | ✅ | `if (x >= 18)` narrows to refinement type in then-branch |
| Pattern matching integration | ✅ | `is RefinementType varName` introduces refined variable |

### C# Emission

| Feature | Status | Notes |
|---|---|---|
| Base type erasure | ✅ | Refinement types emit as their base type |
| Runtime predicate checks | ✅ | Pattern matching emits conditional checks |
| Method parameter guards | ✅ | Runtime validation at method entry |

---

## Adhoc Refinement Constraints

Mist supports ad-hoc refinement constraints — `where` clauses that attach refinement predicates directly to dependent type arguments on generic declarations and to parameters on constructors, methods, and indexers. This extends the named refinement type system with inline, anonymous refinement constraints that use the same pattern-match syntax (relational patterns, logical `and` combinators, constant patterns).

| Feature | Status | Notes |
|---|---|---|
| Dependent type argument constraints grammar | ✅ | `where Size >= 0` on type declarations |
| Parameter constraints grammar | ✅ | `where x < 100` on methods, constructors, indexers |
| Compound predicates (`and`) | ✅ | `where Size >= 0 and <= 1024` |
| Multiple `where` clauses per declaration | ✅ | One per constrained name |
| Interleaved with generic constraints | ✅ | `where T : class where Size >= 0` in any order |
| Semantic model registration | ✅ | Predicates attached to dependent type parameters and value parameters |
| Compile-time enforcement (dependent arguments) | ✅ | Rejects literals/consts violating predicate |
| Compile-time enforcement (parameter arguments) | ✅ | Rejects constant arguments violating predicate |
| Runtime guard generation | ✅ | `ArgumentException` guards for non-constant arguments |
| Guard elision optimization | ✅ | Omits guard when all call sites are statically validated |
| Flow narrowing | ✅ | Parameter treated as anonymous refinement type in body |
| Flow narrowing invalidation | ✅ | Reassignment/compound-assignment invalidates narrowing |
| Type identity participation | ✅ | Dependent argument refinements are part of type contract |
| Predicate compatibility validation | ✅ | Relational predicates rejected on `bool` and `string` |
| Predicate implication checking | ✅ | Forwarding requires source constraint to imply target |
| C# emission | ✅ | Runtime guards as negated-condition + throw pattern |

---

## Generics

| Feature | Status | Notes |
|---|---|---|
| Generic classes (`class Foo<T>`) | ✅ | |
| Generic methods (`void Foo<T>()`) | ✅ | |
| Generic interfaces | ✅ | |
| Type constraints (`where T : class`) | ✅ | |
| Dependent type parameters | ✅ | `<Size:int>` syntax for compile-time constant values |
| Covariance / contravariance | 🔲 | |
| Generic delegates | ✅ | `Func` type family supports 0-16 parameters |

---

## Extension Methods

Mist supports extension blocks — a way to add methods and properties to types you do not own. Extension blocks use the syntax `extension(ReceiverType receiverName) { ... }` and can appear at the top level or inside namespaces.

### Basic Syntax

```mist
extension(string self) {
    int WordCount() {
        return self.Split(' ').Length;
    }

    bool IsBlank() {
        return self.Length == 0;
    }
}

// Usage:
var greeting = "hello world";
var count = greeting.WordCount(); // 2
```

The receiver parameter (`self` above) is accessible inside instance member bodies, similar to `this` in a class.

### Generic Extensions

Type parameters declared on the extension block are inferred automatically at call sites:

```mist
<T> extension(List<T> self) {
    T First() {
        return self[0];
    }

    T Last() {
        return self[self.Count - 1];
    }
}

// Usage — T is inferred as int:
var numbers = new List<int> { 1, 2, 3 };
var first = numbers.First(); // 1
```

Multiple type parameters are supported:

```mist
<K, V> extension(Map<K, V> self) {
    List<K> Keys() { ... }
}
```

### Universal Extensions

An extension with receiver type `unknown` provides members on all types:

```mist
extension(unknown self) {
    string Describe() {
        return self.ToString();
    }
}

// Callable on any value:
var x = 42;
var s = x.Describe(); // "42"
```

The standard library uses this mechanism to provide `ToString()`, `GetHashCode()`, and `GetType()` on all types.

### Static Members

Mark individual members with `static` to make them callable on the type name rather than on instances:

```mist
extension(int self) {
    static int Parse(string s) {
        return Convert.ToInt32(s);
    }

    int Doubled() {
        return self * 2;
    }
}

// Static call — no instance needed:
var n = int.Parse("42");

// Instance call:
var d = n.Doubled(); // 84
```

Static members cannot reference the receiver parameter. The compiler emits an error if they do.

### Extension Properties

Extension properties are read-only (get-only). They cannot declare a `set` accessor because extensions have no backing storage.

```mist
// Expression-bodied property:
extension(string self) {
    int DoubleLength => self.Length * 2;
}

// Block-bodied property:
extension(string self) {
    bool IsEmpty {
        get {
            return self.Length == 0;
        }
    }
}

// Usage:
var s = "hello";
var dl = s.DoubleLength; // 10
```

Attempting to declare a `set` or `init` accessor on an extension property produces a compiler error.

### Namespace Scoping

Extensions declared inside a namespace are only visible when that namespace is imported:

```mist
namespace StringUtils;

extension(string self) {
    string Reversed() { ... }
}
```

```mist
// Another file — must import the namespace:
using StringUtils;

var r = "hello".Reversed(); // works

// Without the using directive, Reversed() is not discoverable.
```

Extensions declared at the top level (outside any namespace) are visible from all files in the compilation.

### Shadowing and Specificity

**Own members always win.** If a type declares a member with the same name as an extension member, the type's own member is always used:

```mist
class Greeter {
    string Hello() { return "own"; }
}

extension(Greeter self) {
    string Hello() { return "extension"; }
}

var g = new Greeter();
g.Hello(); // "own" — the extension is shadowed
```

**More-specific receiver types are preferred.** When multiple extensions provide the same member, the one with the most specific receiver type wins:

```mist
extension(unknown self) {
    string Label() { return "any"; }
}

extension(string self) {
    string Label() { return "string"; }
}

var s = "hi";
s.Label(); // "string" — string-specific extension wins over universal
```

The specificity ordering is: exact type match > base type match > universal (`unknown`). If two candidates are equally specific, the compiler emits an ambiguity error.

### Feature Summary

| Feature | Status | Notes |
|---|---|---|
| Extension block syntax | ✅ | `extension(Type name) { ... }` |
| Generic extensions | ✅ | `<T> extension(List<T> self) { ... }` |
| Universal extensions | ✅ | `extension(unknown self) { ... }` |
| Static extension members | ✅ | `static` keyword on individual members |
| Extension methods | ✅ | Instance and static |
| Extension properties | ✅ | Read-only only (no `set` accessor) |
| Namespace scoping | ✅ | Requires `using` to be visible |
| Own members shadow extensions | ✅ | Type's own members always preferred |
| Receiver specificity ordering | ✅ | More-specific receiver type wins |
| Multiple blocks per type | ✅ | Allowed if no duplicate signatures |
| Trait/interface exclusion | ✅ | Extensions do not satisfy traits or interfaces |

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
| `stackalloc` | ✅ | `stackalloc T[n]` produces a `Span<T>` (or `ReadOnlySpan<T>` in a read-only context) with frame-escape safety enforced |
| `sizeof` | ⛔ | Out of scope |
| Function pointers | ⛔ | Out of scope |
| `Span<T>` / `Memory<T>` | ✅ | Projected from ambient assemblies as ref struct types; `Standard.Span<T>` / `Standard.ReadOnlySpan<T>` intrinsics with `stackalloc` support |
| `ref struct` | ✅ | Declaration and consumption; stack-only restrictions enforced |

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

## Mist-specific Features

Features introduced by Mist that don't map directly to a single C# feature. These are Mist's semantic extensions, listed here so the feature matrix is complete.

### The `unknown` Top Type

`unknown` is Mist's top type — any non-`void` value is assignable into an `unknown`-typed slot. The compiler enforces a structured path for getting information back out: pattern-matching via `is` is the designated mechanism. This gives programs an escape hatch for interop and dynamic-origin values while keeping Mist's type checker sound.

**Top-type assignability.** Every non-`void` type is assignable to `unknown`. `void` is not. The reverse direction requires pattern narrowing or a user-defined cast operator declared on the target type.

**Storage prohibition.** `unknown` cannot appear as the declared type of writable storage — fields, properties, indexer elements, `new` expressions, generic-construction type arguments at a storage position. The compiler rejects those declarations with the `MIST_SB_005x` diagnostic family. Read-only positions (method parameters, return types, type arguments in non-storage contexts) are allowed. This keeps the type safely untyped at the boundary without letting it leak into storage that would undermine type checking.

**Reachability-based member filtering on closed generics.** A `Closed_Generic_With_Unknown` receiver (e.g., `List<unknown>`) has its members filtered at member access. The compiler's Reachability Analyzer classifies every member of every generic type by whether it can transitively write to storage of the generic's type parameters. On a `List<unknown>` receiver, members classified `WritesT` are hidden from the call site with diagnostic `MIST_TC_0062`. Read-path members remain accessible, with their result types substituted (so `List<unknown>.Count` returns `int`, `List<unknown>[i]` returns `unknown`).

**Pattern-matching as the designated escape.** `x is Concrete c` is the only way to recover a concrete type from a bare `unknown` or from a `Closed_Generic_With_Unknown`. Inside the narrowed branch, the binding has the concrete type and full member access — including write-path members hidden before the narrow.

**Covariant unknown widening.** For class-generic definitions, `Foo<A> → Foo<unknown>` is an implicit widening conversion. The Mist rule is sound: every write-path member on the widened receiver is filtered out by reachability analysis, so the runtime can't observe a variance violation. The C# emission lowers `Closed_Generic_With_Unknown` to C# `dynamic`, routing member access through the DLR at runtime. This is one of the places where `unknown` is explicitly distinct from C# `dynamic`: Mist's type checker still validates every member access at compile time against the closed generic's public API, and filters writes. C# `dynamic` defers all checking to runtime. Struct-generic widening (e.g., `Optional<int> → Optional<unknown>`) is rejected with `MIST_TC_0058` because C# generic structs cannot be safely widened even under a reachability-filtered read-only access pattern.

**C# emission.** Bare `unknown` lowers to C# `object`. `Closed_Generic_With_Unknown` lowers to C# `dynamic`, with `Microsoft.CSharp.dll` + `System.Linq.Expressions.dll` + `System.Dynamic.Runtime.dll` added to the emitted project's references. No cast is inserted at widening sites — the implicit conversion to `dynamic` is handled by the C# compiler. See `.kiro/specs/unknown-type/design.md` for the full emission strategy rationale (Option C).

---

## Summary

| Category | ✅ Implemented | 🔲 Remaining | ⛔ Skipped |
|---|---|---|---|
| Value Types | 14 | 4 | 0 |
| Reference Types | 5 | 6 | 1 |
| Type Declarations | 6 | 9 | 0 |
| Members — Fields | 3 | 3 | 1 |
| Members — Properties | 10 | 1 | 0 |
| Members — Methods | 6 | 11 | 0 |
| Members — Constructors | 3 | 2 | 0 |
| Members — Other | 1 | 4 | 0 |
| Access Modifiers | 3 | 11 | 2 |
| Statements — Declarations | 2 | 6 | 0 |
| Statements — Control Flow | 6 | 7 | 1 |
| Statements — Exceptions | 0 | 4 | 0 |
| Statements — Other | 1 | 5 | 2 |
| Expressions — Literals | 11 | 3 | 4 |
| Expressions — Arithmetic | 6 | 2 | 0 |
| Expressions — Comparison | 6 | 0 | 0 |
| Expressions — Logical | 3 | 0 | 0 |
| Expressions — Bitwise | 0 | 7 | 0 |
| Expressions — Assignment | 1 | 4 | 0 |
| Expressions — Member Access | 4 | 3 | 0 |
| Expressions — Object Creation | 3 | 4 | 0 |
| Expressions — Other | 6 | 10 | 2 |
| Inheritance | 3 | 8 | 0 |
| Structural Typing — Traits | 5 | 0 | 0 |
| Structural Typing — Matching | 5 | 0 | 0 |
| Structural Typing — With Blocks | 4 | 0 | 0 |
| Structural Typing — C# Lowering | 4 | 0 | 0 |
| Pattern Matching | 7 | 1 | 0 |
| Type Unions — Syntax & Parsing | 5 | 0 | 0 |
| Type Unions — Semantic Model | 4 | 0 | 0 |
| Type Unions — Type Checking | 9 | 0 | 0 |
| Type Unions — C# Emission | 6 | 0 | 0 |
| Refinement Types — Syntax & Parsing | 5 | 0 | 0 |
| Refinement Types — Semantic Model | 3 | 0 | 0 |
| Refinement Types — Type Checking | 8 | 0 | 0 |
| Refinement Types — C# Emission | 3 | 0 | 0 |
| Adhoc Refinement Constraints | 16 | 0 | 0 |
| Generics | 5 | 2 | 0 |
| Async | 0 | 5 | 1 |
| Namespaces & Imports | 3 | 3 | 1 |
| Comments | 1 | 2 | 0 |
| Attributes | 0 | 4 | 0 |
| Preprocessor | 0 | 0 | 6 |
| Unsafe / Low-Level | 1 | 0 | 7 |
| Ambient Types | 5 | 0 | 0 |
| Type Checking | 11 | 4 | 0 |

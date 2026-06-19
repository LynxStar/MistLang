<!-- MIST-MANAGED-STEERING: installed by `mist steering`. Do not edit — this file is overwritten on every reinstall. Do not add this marker to your own steering files; any file containing it is treated as Mist-managed and may be deleted. -->

# Mist Language Reference

Mist compiles to C# and is deliberately C#-like. **Assume every language feature works exactly as it does in C# unless this document says otherwise.** Syntax, semantics, standard library usage, and idioms are inherited from C#. This file documents only the ways Mist *differs* from C#.

If something isn't mentioned here, write it the way you would in C#.

## Companion Steering Files

This file is always loaded. Deeper references live in separate manual-inclusion files in `.kiro/steering/`. Pull the relevant one into context (the user can reference it with `#`, or you can read the file directly) when a task touches its topic:

| File | Read it when the task involves… |
|------|----------------------------------|
| `#mist-ownership` | `owned`/`borrowed`/`frozen`/`readonly ref`, move semantics, lending, `IClonable`, or `linear`/`affine`/`relevant` drop semantics |
| `#mist-advanced-features` | traits, structured context (`with` clauses), refinement types, dependent types, associated types, extension blocks |
| `#mist-gitignore` | setting up version control — what Mist generates and what to ignore |

## Project Model

Mist projects are configured with `mist.toml`, not `.csproj`. Source files use the `.mist` extension and are discovered recursively — there is no file manifest.

```toml
name = "MyApp"                  # required; default namespace root + assembly name
namespace_root = "MyCompany.App"  # optional; overrides namespace root
entry_point = "top-level"       # optional; disambiguates entry point

[references]
SharedLib = { path = "../SharedLib" }  # key MUST match referenced project's name

[global_usings]
namespaces = ["Standard", "MyApp.Models"]
```

- Namespace is inferred from directory structure: `Models/Player.mist` → `{namespace_root}.Models`.
- `Standard` is Mist's core namespace (the equivalent of `System`): `using Standard;` gives you `Console`, `List<T>`, etc.

CLI:

```bash
mist build              # compile
mist run                # compile and execute
mist build ./project    # target a folder
```

## No `null`

Mist has no `null`. The absent value is `void`, used as a first-class value:

```mist
int? maybe = 42;        // T? is sugar for the union T | void
int? absent = void;     // 'void' is the absent value (not 'null')
```

`void` is also a first-class *type*: it can be a generic argument, a variable type, and a return type. The `?.`, `??`, and `??=` operators work exactly as in C#, but short-circuit on `void` instead of `null`.

## `unknown` Is the Top Type

Use `unknown` instead of `object` as the top type. You must narrow it (via `is`) before accessing members — there is no implicit member access on `unknown`.

```mist
unknown box = 42;
if (box is int n) { Console.WriteLine(n); }
```

## No `++` / `--`

Increment and decrement operators do not exist. Write `i = i + 1`.

## `func` Instead of `Func<>` / `Action<>`

Function types use the lowercase `func` keyword. `func<T>` with no result is the `Action` equivalent; `func<..., TResult>` is the `Func` equivalent.

```mist
func<int, int> doubler = x => x * 2;       // Func<int,int>
func<string> getName = () => "Mist";        // Func<string>
func<int, int, int> add = (a, b) => a + b;
```

Lambdas, captures, method-group conversion, and `static` lambdas all behave as in C#. (`Action<T>` and `Func<...>` also exist as aliases if you prefer.)

## Traits (Structural Typing)

Traits are a new concept with no C# equivalent. A trait is a structural contract — any type with matching public members satisfies it automatically, no `implements` needed.

```mist
trait Printable { string Name; int Id; }

// Any type with public Name:string and Id:int satisfies Printable
public class User { public string Name; public int Id; }

void Print(Printable item) { Console.WriteLine(item.Name); }
Print(new User());   // works structurally
```

Use a `with` declaration to map mismatched member names onto a trait:

```mist
with Product : Printable { Title as Name; Sku as Id; }
```

Interfaces, by contrast, remain nominal and work exactly like C#.

## Type Unions

A value can be typed as a union of types. Switch expressions over unions are exhaustive without a wildcard.

```mist
int | string value = 42;
var desc = value switch {
    int n => $"int: {n}",
    string s => $"str: {s}",
};
```

## `type` Declarations: Aliases and Newtypes

The `type` keyword (not C#'s `using` alias) declares aliases and newtypes:

```mist
type Name = string;        // alias — interchangeable with string
type UserId = new int;     // newtype — distinct type, NO implicit conversion to/from int
type Pair<T> = (T, T);     // parameterized alias
```

## Refinement Types

Types carrying a predicate, checked at compile time for literals and at runtime via `is`. No C# equivalent.

```mist
type AdultAge = int >= 18;
type Percentage = int >= 0 and <= 100;
type ValidRange = Range { Start: >= 0 };

AdultAge age = 20;               // compile-time validated
if (input is AdultAge valid) { } // runtime validated
int sum = age + 5;               // arithmetic widens back to the base type (int)
```

A narrower refinement is assignable to a wider one (`int >= 21` → `int >= 18`). See `#mist-advanced-features.md`.

## Dependent Types

Type parameters can be compile-time constant *values*, not just types. No C# equivalent.

```mist
public class FixedBuffer<Size:int> {
    public int GetSize() { return Size; }
}
var buf = new FixedBuffer<128>();
```

`FixedBuffer<5>` and `FixedBuffer<10>` are distinct types. Allowed value kinds: the integer types, `bool`, and `char`. See `#mist-advanced-features.md`.

## Enums Have Arbitrary Underlying Types

Mist enums are value-bearing and can be backed by any type — including `string`, structs, and classes — not just integers. They implicitly convert to their underlying type.

```mist
public enum Direction : string { North: "N", South: "S" }
public enum Color { Red, Green, Blue }                 // auto-assigned byte
public enum CardinalDir : Vec2 { Up: new Vec2(0, 1) }  // struct-backed

string n = Direction.North;   // implicit conversion → "N"
```

## Extension Syntax

Extensions use an `extension(Receiver self) { }` block, not a `static class` with `this` parameters.

```mist
extension(string self) {
    public bool IsEmpty() { return self.Length == 0; }
}

<T> extension(T[] self) {                          // generic
    public T[] Filter(func<T, bool> p) { /* ... */ }
}

extension(unknown self) {                          // universal — applies to all types
    public bool IsNotNull() { return self is not void; }
}
```

Extensions are namespace-scoped; import them with `using`.

## Struct Inheritance

Unlike C#, structs can inherit from other structs (value-type inheritance, no boxing/heap allocation).

```mist
public struct Base { public int X; }
public struct Derived : Base { public int Y; }
```

## `readonly` Blocks and Locals

Beyond C#'s `readonly` fields, Mist has readonly *locals* and readonly *blocks*:

```mist
readonly var x = 42;     // immutable local
readonly { var y = 10; } // every local declared in the block is immutable
```

## Async: Structured Concurrency

Calling an `async` method from synchronous code **auto-awaits** and yields the unwrapped result — you do not get a `Task<T>` back by default.

```mist
public async string Fetch(int id) { return $"item-{id}"; }

string r = Fetch(1);          // auto-awaited → string (NOT Task<string>)
Task<string> t = async Fetch(1);  // 'async' prefix opts into a Task
string v = await t;           // await works as in C#

func<int, Task<int>> lift = id => async ComputeSync(id);  // 'async' lifts a sync call into a Task
```

Tasks that aren't explicitly awaited are awaited automatically at scope exit (structured concurrency).

## `regex` Primitive

`regex` is a built-in primitive type with backtick literals:

```mist
regex pattern = `[0-9]+`;
```

## Ownership, Drop, and Structured Context

Mist adds opt-in systems with no C# equivalent. These are documented in detail in companion files — include them when relevant:

- **Ownership** (`owned`/`borrowed`/`frozen`/`readonly ref`, move semantics, lending, `IClonable`) and **substructural/drop semantics** (`linear`/`affine`/`relevant`): see `#mist-ownership.md`.
- **Structured context** (implicit parameter passing via `with` clauses), refinement and dependent type details, traits, associated types: see `#mist-advanced-features.md`.

By default, bindings are unrestricted and behave like ordinary C# references — you only deal with ownership when you opt in with these keywords.

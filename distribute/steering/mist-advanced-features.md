---
inclusion: manual
---

<!-- MIST-MANAGED-STEERING: installed by `mist steering`. Do not edit — this file is overwritten on every reinstall. Do not add this marker to your own steering files; any file containing it is treated as Mist-managed and may be deleted. -->

# Mist Advanced Features

Deep reference for Mist features that have no C# equivalent. As with the core reference, **assume C# behavior as the baseline** — this file only covers what is genuinely new. Include this context when working with abstract types, structured context, refinement types, dependent types, or associated types.

## Structured Context (Implicit Parameter Passing)

Mist's dependency-injection mechanism. Methods declare context requirements with `with` clauses; callers push values into scope, and they propagate down the call chain automatically. No C# equivalent.

### Declaring Consumers

```mist
public static string GetConfig() with Config cfg { return cfg.Value; }

// Multiple requirements
public static string GetBoth() with string s with int n { return $"{s}/{n}"; }
```

### Pushing Context

```mist
with var greeting = "hello";              // inferred type + local binding
with int count = 42;                      // explicit type + local binding
with Config _ = new Config("prod");       // explicit type, no local binding
with new Locale("en-US");                 // push-only, no binding
```

### Scoped Blocks and Shadowing

```mist
with var x = 42;
Consumers.GetInt();        // 42
with(99) {
    Consumers.GetInt();    // 99 — inner scope shadows
}
Consumers.GetInt();        // 42 — restored on block exit
```

### Propagation

Intermediate methods forward context without naming it:

```mist
public static string Forward() with string s {
    return Consumers.GetString();   // s flows through automatically
}
```

### Constructor With-Clauses

```mist
public class Greeter
{
    public string Greeting;
    public Greeter() with string message { this.Greeting = $"Hello, {message}"; }
}

with("world");
var g = new Greeter();   // pulls "world" from context
```

### Context Key Rules

- Each type is an independent context key.
- Type **aliases** resolve to their underlying type (share its key).
- **Union** types are distinct keys from their constituents.
- **Refinement** types resolve to their base type (share its key).
- **Dependent** types with different parameter values are distinct keys.

## Refinement Types

Base types with attached predicates. Literals are validated at compile time; runtime values via `is`.

```mist
type AdultAge = int >= 18;
type Percentage = int >= 0 and <= 100;
type ValidRange = Range { Start: >= 0 };          // property pattern on a type
type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;  // union with refinements
```

### Behavior

```mist
AdultAge age = 20;             // compile-time OK
// AdultAge bad = 10;          // compile-time ERROR

int input = 25;
if (input is AdultAge valid) { }   // runtime validation, binds 'valid'

private static AdultAge GetDefault() { return 25; }  // as return type
private static string Describe(AdultAge a) { }        // as parameter type

AdultAge a = 20;
int result = a + 5;            // arithmetic produces the BASE type (int)
```

### Subset Assignability and Narrowing

```mist
type DrinkingAge = int >= 21;
DrinkingAge d = 25;
AdultAge wider = d;            // OK: (>= 21) is a subset of (>= 18)

int x = 21;
if (x >= 18) {
    AdultAge narrowed = x;     // OK: compiler proves the predicate in this branch
}
```

## Dependent Types

Type parameters that are compile-time constant *values*.

```mist
public class FixedBuffer<Size:int>
{
    public int GetSize() { return Size; }       // the value is in scope as 'Size'
    public string Describe() { return $"FixedBuffer<{Size}>"; }
}

public class Matrix<Rows:int, Cols:int> { }     // multiple
public class TypedBuffer<T, Size:int> { }        // mixed type + value params
```

Allowed value kinds: `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `bool`, `char`.

```mist
public class BoolFlag<V:bool> { public bool IsSet() { return V; } }
public class CharTag<V:char> { public char Get() { return V; } }
var neg = new IntParam<-1>();   // negative literals allowed for signed types
```

### Type Identity

Distinct dependent values are distinct types:

```mist
public static string AcceptFive(FixedBuffer<5> buf) { return "ok"; }
AcceptFive(new FixedBuffer<5>());    // OK
// AcceptFive(new FixedBuffer<10>()); // ERROR — different type
```

### Dependent Methods and Inheritance

```mist
public static void Repeat<N:int>(string text) { }
public static string Process<N:int>(FixedBuffer<N> buf) { }
Repeat<3>("hi");

public class SmallBuffer : FixedBuffer<10> { }      // fix the value
public class Resizable<N:int> : FixedBuffer<N> { }   // forward the value
```

### Pattern Matching

```mist
if (target is FixedBuffer<5> matched) { }   // dependent value participates in the match
```

## Associated Types

Abstract types can declare associated type slots that implementers fill in. No direct C# equivalent (C# uses generic type parameters instead).

```mist
abstract type IIterator
{
    type Item;            // associated type slot
    Item? Next();
}

public class RangeIterator : IIterator
{
    type Item = int;      // bind the slot
    public int? Next() { /* ... */ }
}

// Constrained associated type slot
abstract type Collection
{
    type Element : IComparable;
    Element First();
}
```

## Ownership Constraints on Generics

Generic constraints work as in C#, with additional ownership/substructural constraints (`owned`, `linear`, `affine`, `relevant`):

```mist
public class OwnedBox<T> where T : owned { }
public class LinearBox<T> where T : linear { }
public class Clonable<T> where T : IClonable<T> { }
```

For the full ownership and drop-semantics model, see `#mist-ownership.md`.

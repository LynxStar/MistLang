# Associated Types

Associated types let interfaces and traits declare abstract type slots — type members whose concrete definition is deferred to implementors. Where generic type parameters are chosen by the *caller* of a generic function or constructor, associated types are chosen by the *implementor* of an interface. This distinction makes associated types ideal for contracts where the implementing type determines a related type that callers shouldn't need to specify explicitly.

The primary motivating use case is zero-allocation iteration: an `Iterable<TValue>` interface declares an associated enumerator type slot, and each collection provides its own concrete struct enumerator. Generic code iterates without knowing the specific enumerator type, yet the compiler resolves it statically — no boxing, no virtual dispatch, no heap allocation for the enumerator.

## Declaration Syntax

An associated type slot is declared inside an interface or trait body using the `type Name : ConstraintList;` syntax. The colon and constraint list specify what the concrete type must satisfy, using the same constraint syntax as generic `where` clauses (see [Generic Constraints](./generic-constraints.md)).

```mist
interface Iterable<TValue> {
    type Enumerator : IEnumerator<TValue>, struct;

    Enumerator GetEnumerator();
}
```

Here, `Enumerator` is an associated type slot. Any type implementing `Iterable<TValue>` must provide a concrete type named `Enumerator` that:
1. Implements `IEnumerator<TValue>`
2. Is a value type (`struct`)

The constraint list supports the same forms as generic constraints: `struct`, `class`, interface references, trait references, base class references, `new()`, and combinations thereof. Multiple constraints are separated by commas.

### Unconstrained Slots

If no constraints are needed, omit the colon entirely:

```mist
interface Container {
    type Element;

    Element Get(int index);
}
```

An unconstrained slot places no restrictions on the concrete type — the implementor can provide any type.

### Generic Type Parameters on Slots

Associated type slots can declare their own type parameters and where clauses:

```mist
interface Transformable<TValue> {
    type Mapper<TResult> : IMapper<TValue, TResult> where TResult : struct;

    Mapper<TResult> GetMapper<TResult>() where TResult : struct;
}
```

The slot's type parameters are independent of the enclosing interface's type parameters, though the constraint list can reference both.

## Implementor Satisfaction

A class or struct satisfies an associated type slot by providing a concrete type alias that names the slot and assigns a concrete type using `=`:

```mist
interface IEnumerator<TValue> {
    bool MoveNext();
    TValue Current { get; }
}

interface Iterable<TValue> {
    type Enumerator : IEnumerator<TValue>, struct;

    Enumerator GetEnumerator();
}

class IntRange : Iterable<int> {
    public struct RangeEnumerator : IEnumerator<int> {
        int _current;
        int _end;

        public RangeEnumerator(int start, int end) {
            _current = start - 1;
            _end = end;
        }

        public bool MoveNext() {
            _current = _current + 1;
            return _current < _end;
        }

        public int Current { get => _current; }
    }

    public type Enumerator = RangeEnumerator;

    int _start;
    int _end;

    public IntRange(int start, int end) {
        _start = start;
        _end = end;
    }

    public RangeEnumerator GetEnumerator() {
        return new RangeEnumerator(_start, _end);
    }
}
```

The compiler verifies that `RangeEnumerator` satisfies both constraints declared on the `Enumerator` slot:
- It implements `IEnumerator<int>` ✓
- It is declared as a `struct` ✓

If either constraint is violated, the compiler emits an error naming the unsatisfied constraint, the concrete type, and the interface requiring it.

### What the Compiler Checks

For each associated type slot in an implemented interface, the type checker verifies:

1. **Existence** — The implementor provides a concrete type member with the same name (and arity, if the slot has type parameters). Missing slots produce a "does not provide a concrete type" error.
2. **Constraint satisfaction** — The concrete type satisfies every constraint in the slot's constraint list. Violations produce a "does not satisfy constraint" error identifying the specific failing constraint.
3. **Signature consistency** — Interface methods that reference the slot name in their signatures must be implemented with the concrete type in the corresponding positions.

## Generic Context Resolution

The real power of associated types emerges in generic code. When a type parameter is constrained by an interface with associated type slots, you can reference the associated type through dot notation on the type parameter:

```mist
fun Sum<TIter>(iter: TIter) : int where TIter : Iterable<int> {
    var enumerator: TIter.Enumerator = iter.GetEnumerator();
    var total: int = 0;
    while (enumerator.MoveNext()) {
        total = total + enumerator.Current;
    }
    return total;
}
```

Inside this function, `TIter.Enumerator` resolves to whatever concrete enumerator type the caller's `TIter` provides. The compiler knows that `TIter.Enumerator` satisfies `IEnumerator<int>` and is a `struct`, so it permits calling `MoveNext()` and accessing `Current` without any casts or boxing.

When `Sum` is called with a concrete type:

```mist
var range = new IntRange(0, 10);
var result = Sum<IntRange>(range);
```

The compiler resolves `TIter.Enumerator` to `IntRange.RangeEnumerator` — a concrete struct. The enumerator lives on the stack, method calls are statically dispatched, and the optimizer can inline the entire iteration loop.

### What You Can Do with Associated Type References

Within a generic context, `T.SlotName` behaves like a type that satisfies the slot's constraints. You can:

- Declare local variables of type `T.SlotName`
- Use `T.SlotName` as a parameter type or return type
- Pass `T.SlotName` values to functions expecting the constraint interfaces
- Use `T.SlotName` as a generic type argument where the constraints are compatible

If you reference a name that isn't an associated type slot of any constraining interface (e.g., `T.NonExistent`), the compiler emits an error indicating that the type parameter doesn't have an associated type with that name.

## Design Rationale: Zero-Allocation Iteration

Traditional iteration patterns in managed languages use interface dispatch:

```
// Conceptual — not Mist syntax
IEnumerator<int> e = collection.GetEnumerator();  // boxed if struct
while (e.MoveNext()) { ... }                       // virtual dispatch
```

This has two costs:
1. **Boxing** — If the enumerator is a struct, returning it through an interface reference boxes it on the heap.
2. **Virtual dispatch** — Each `MoveNext()` and `Current` access goes through a vtable lookup, preventing inlining.

Associated types eliminate both costs. Because the `Enumerator` slot is constrained to `struct`, the compiler knows the concrete type at every call site. The generic lifting strategy emits the enumerator as a type parameter on the C# interface, so the .NET JIT sees the concrete struct type and can:

- **Stack-allocate** the enumerator (no heap allocation, no GC pressure)
- **Inline** `MoveNext()` and `Current` directly into the loop body
- **Eliminate bounds checks** when the JIT can prove the iteration pattern is safe

This is the same approach Rust uses with `IntoIterator` / `Iterator` — the concrete iterator type is monomorphized at each call site, giving zero-cost abstraction over iteration.

### Worked Example: End-to-End

Putting it all together, here's a complete example showing the full pattern from interface declaration through generic usage:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/associated-types/worked-example.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/associated-types/worked-example.cs}}
```
{{#endtab }}
{{#endtabs }}

In this example:
- `IProducer` declares an associated type slot `Output` constrained to `IStringable`
- `IIterator` declares an associated type slot `Cursor` constrained to `struct`
- `MessageProducer` satisfies `IProducer` with a nested class `Output` that implements `IStringable`
- `RangeIterator` satisfies `IIterator` with a nested struct `Cursor`

In the emitted C#, notice how the associated type slots become generic type parameters on the interfaces: `IProducer<TOutput> where TOutput : IStringable` and `IIterator<TCursor> where TCursor : struct`. At the implementation site, the concrete nested type is substituted as the type argument: `MessageProducer : IProducer<MessageProducer.Output>` and `RangeIterator : IIterator<RangeIterator.Cursor>`. This generic lifting strategy gives the .NET JIT full visibility into the concrete types, enabling static dispatch and stack allocation for struct slots.

## See Also

- [Generic Constraints](./generic-constraints.md) — The constraint syntax (`struct`, `class`, interface references, `new()`) used in associated type slot declarations is the same syntax used in generic `where` clauses. If you're unfamiliar with constraint syntax, start there.

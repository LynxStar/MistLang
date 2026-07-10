# Program Model Backlog

## Resolve associated and nested member types by simple name

`TypeReferenceResolver.ResolveNamed` cannot resolve a type declared inside
another type when it is referenced by simple name from a sibling member.
Resolution falls through the scope chain and emits `MIST_PM_UNRESOLVED_TYPE`.

From `Standard/IEnumerable.mist`:

```
abstract IEnumerable<TElement>
{
    type Enumerator : IEnumerator<TElement>;
    Enumerator GetEnumerator();
}
```

The associated type `Enumerator` is declared with
`type Enumerator : IEnumerator<TElement>;` and then used as the return type of
`GetEnumerator()`. Resolving the bare name `Enumerator` finds nothing because
associated-type declarations are not registered into the enclosing type's
scope.

The nested-type case is the same family of gap. From `Array.mist`:

```
intrinsic public struct Enumerator : IEnumerator<TValue> { ... }
intrinsic public Enumerator GetEnumerator();
```

`Array.Enumerator` is a nested struct referenced by simple name from
`GetEnumerator`. Both associated types (abstract `type X : Y;`) and nested
types (concrete nested class or struct) need to participate in scope
resolution.

**Why:**
- `IEnumerable`, `IEnumerator`, and `Array` in the stdlib all depend on
  associated or nested types resolving by simple name
- Directly on the path for the ambient type provider work, which must project
  these types and their members
- Without it, any type that names its own associated or nested type from a
  sibling member fails to construct

**Impact:**
- Associated-type declarations (`type Name : Bound;`) must be registered as
  members of the enclosing type and inserted into its scope
- Nested concrete types must likewise be reachable by simple name within the
  parent's body
- Interacts with generic substitution: `Enumerator` inside
  `IEnumerable<TElement>` carries the enclosing type parameters
- `ResolveNamed` simple-name lookup needs to consult the enclosing type's
  member and nested scope before failing

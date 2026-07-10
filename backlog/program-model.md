# Program Model Backlog

## Accept `intrinsic` on properties, indexers, and fields

The ProgramModel currently rejects the `intrinsic` modifier on any member that
is not a method or constructor. `PropertyBuilder` emits
`MIST_PM_INTRINSIC_NOT_VALID` for intrinsic properties, `ConcreteTypeBuilder`
does the same for fields, and `DelegateResolver` for abstract members:

```csharp
if(modifiers.IsIntrinsic)
{
    context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicNotValidOnMember, blockProp, null, "properties");
}
```

The bootstrap standard library relies on intrinsic properties and indexers to
describe compiler-provided members on built-in types. From
`Standard/Bootstrap/Array.mist`:

```
intrinsic public Int32 Length { get; }
intrinsic public TValue this[ArrayBounds index] { get; set; }
```

The same shape appears on `Span`, `String`, `Regex`, and `Type`, plus `Current`
on the nested `Enumerator`. The classic frontend accepts these, so the stdlib
compiles today; only the new frontend rejects them. This surfaced when running
the experimental pipeline over the stdlib (intrinsic-on-property errors across
the bootstrap files).

**Why:**
- Built-in types (`Array`, `Span`, `String`, and so on) cannot be modeled
  without intrinsic properties and indexers, which have no body because the
  compiler supplies the implementation
- Blocks feeding the standard library through the new frontend at all
- The distinction between "intrinsic method" and "intrinsic property" is not
  meaningful for modeling purposes; both are compiler-provided members

**Impact:**
- `PropertyBuilder` must model an intrinsic property (no accessor bodies,
  compiler-supplied)
- Indexer construction needs the same allowance
- `ConcreteTypeBuilder` field handling must decide whether intrinsic fields are
  valid (the stdlib does not appear to use them, so this may stay an error -
  confirm against the grammar and stdlib)
- The `IntrinsicNotValidOnMember` diagnostic narrows to only the member kinds
  that genuinely cannot be intrinsic


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

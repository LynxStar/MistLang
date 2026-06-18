# Substructural Field Discipline

The previous chapter introduced drop semantics — how each substructural discipline determines when and how often `Drop()` fires. This chapter builds on that foundation to answer a harder question: **which types can a container safely hold as fields?**

A field's lifecycle is bound to its container. When the container is dropped, its fields are dropped too. This means the container's drop guarantee must be compatible with what each field requires. The compiler enforces this through the **discipline-compatibility matrix**.

## The Drop Guarantee

Each discipline makes a specific promise about how many times `Drop()` fires on the container:

| Discipline | Drop fires... |
|---|---|
| `linear` | Exactly once |
| `affine` | 0 or 1 times |
| `relevant` | 1 or more times |
| Unrestricted | 0 to many times |

A field's discipline requires certain guarantees from its container. A `linear` field needs exactly one drop. A `relevant` field needs at least one. An `affine` field needs at most one. An unrestricted field doesn't care.

## The Compatibility Matrix

The matrix follows directly from matching container promises against field requirements:

| Container ↓ \ Field → | `linear` | `affine` | `relevant` | unrestricted |
|---|---|---|---|---|
| **`linear`** | ✓ | ✓ | ✓ | ✓ |
| **`affine`** | ✗ | ✓ | ✗ | ✓ |
| **`relevant`** | ✗ | ✗ | ✓ | ✓ |
| **unrestricted** | ✗ | ✗ | ✗ | ✓ |

- **Linear containers** can hold anything — exactly-once satisfies all invariants.
- **Affine containers** may never be dropped (0 drops is valid), so they cannot hold fields that require at least one drop (linear, relevant).
- **Relevant containers** may be dropped multiple times, so they cannot hold fields that require at most one drop (linear, affine).
- **Unrestricted containers** make no guarantees at all — only unrestricted fields are safe.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/substructural-field-discipline/compatibility-matrix.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/substructural-field-discipline/compatibility-matrix.cs}}
```
{{#endtab }}
{{#endtabs }}

## Drop Guarantee Reasoning

Each rejection in the matrix has a clear justification rooted in what would go wrong at runtime:

**Affine container + linear/relevant field:** An affine value may be forgotten (never dropped). If it holds a linear field, that field's `Drop()` never fires — violating exactly-once. If it holds a relevant field, the at-least-once guarantee is also violated.

**Relevant container + linear/affine field:** A relevant value may be dropped multiple times (it's aliasable). If it holds a linear field, that field's `Drop()` would fire more than once — violating exactly-once. If it holds an affine field, the at-most-once guarantee is violated.

**Unrestricted container + any substructural field:** An unrestricted value has no drop discipline at all. It might never be dropped, or it might be dropped many times. No substructural field can safely live here.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/substructural-field-discipline/drop-guarantee-reasoning.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/substructural-field-discipline/drop-guarantee-reasoning.cs}}
```
{{#endtab }}
{{#endtabs }}

When you attempt an invalid combination, the compiler produces a targeted error message explaining exactly which invariant would be violated:

```
error MIST_DROP_FIELD_NOT_OWNED: Affine type 'MyContainer' cannot contain linear field 'Handle'.
    The container may never be dropped, violating the field's requirement of at least one drop.
```

## Generic Substructural Constraints

Without generics, the matrix is straightforward — you can see the field's discipline directly in the type. But what about `linear class Box<T> { T Value; }`? The compiler doesn't know what `T` is at definition time.

The solution is **substructural constraints**: `where T : linear`, `where T : affine`, or `where T : relevant`. These tell the compiler what discipline the type argument must have, allowing it to verify the matrix at the definition site.

Without a constraint, the compiler rejects the field:

```
error MIST_DROP_FIELD_NOT_OWNED: Linear type 'Box' contains field 'Value' of unconstrained
    type parameter 'T'. Add a substructural constraint (e.g., 'where T : linear') to ensure
    the field's discipline is compatible.
```

With the constraint, the compiler treats `T` as having that discipline for the matrix check:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/substructural-field-discipline/generic-constraints.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/substructural-field-discipline/generic-constraints.cs}}
```
{{#endtab }}
{{#endtabs }}

Substructural constraints can be combined with other constraints — `where T : linear, class` is valid (the type must be linear and a reference type). However, you cannot combine multiple substructural constraints on the same parameter — `where T : linear, affine` is an error.

## Constraint Satisfaction Subtyping

When instantiating a generic type, the type argument must satisfy the constraint. The satisfaction rules follow the discipline lattice:

| Type argument discipline | Satisfies `linear`? | Satisfies `affine`? | Satisfies `relevant`? |
|---|---|---|---|
| `linear` | ✓ | ✓ | ✓ |
| `affine` | ✗ | ✓ | ✗ |
| `relevant` | ✗ | ✗ | ✓ |
| unrestricted | ✗ | ✗ | ✗ |

The key insight: a `linear` type satisfies both `affine` and `relevant` constraints. This is because exactly-once (linear) is a stronger guarantee that implies both at-most-once (affine) and at-least-once (relevant).

This means you can pass a `linear` type to a generic that only requires `affine` or `relevant`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/substructural-field-discipline/constraint-satisfaction.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/substructural-field-discipline/constraint-satisfaction.cs}}
```
{{#endtab }}
{{#endtabs }}

An `affine` type only satisfies `affine` (not `linear` or `relevant`). A `relevant` type only satisfies `relevant` (not `linear` or `affine`). An unrestricted type satisfies no substructural constraint at all.

## Summary

The substructural field discipline ensures that composite types remain sound by matching container drop guarantees against field requirements:

1. **Linear containers** are the most permissive — exactly-once covers everything.
2. **Affine containers** can only hold affine and unrestricted fields — because they might never drop.
3. **Relevant containers** can only hold relevant and unrestricted fields — because they might drop multiple times.
4. **Unrestricted containers** can only hold unrestricted fields — they make no promises.

For generics, substructural constraints (`where T : linear/affine/relevant`) let the compiler verify the matrix at definition time rather than waiting for instantiation. The satisfaction lattice ensures linear types can be used anywhere a weaker guarantee is required.

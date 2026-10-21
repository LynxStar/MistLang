# Stampable Types

**Stampability** is a property of a type. Its valid default can be copied into an array or stack buffer without constructing an object for each element or sharing references between elements. Bare array creation and `stackalloc` both require a stampable element type.

Mist can infer the property from a type's instance storage. You can also write `stampable` to protect a struct declaration against changes that would violate the property, or use `where T : stampable` to make the same guarantee available inside generic code.

## Inference for Concrete and Constructed Types

The declaration modifier is optional. A concrete struct qualifies when its instance fields are recursively stampable. For a closed generic type, Mist substitutes the actual arguments before inspecting the fields: an unmarked `Box<int>` can be stampable while `Box<string>` is not.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/stampability/inferred-construction.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/stampability/inferred-construction.cs}}
```
{{#endtab }}
{{#endtabs }}

The `Box<string>` declaration and construction are valid. It is its use as an element in a bare allocation that fails. Supply explicit elements or an [initializer function](./definite-assignment.md#initializer-based-array-construction) when the element type is not stampable.

The analysis includes inherited fields, automatic-property backing storage, and backing storage created by explicit `field` use in custom accessors. Static fields and computed properties without backing storage do not contribute to an instance. An unused generic parameter does not need a bound merely because it appears in the declaration.

This inference covers source-defined structs, intrinsic primitives, and supported tuples. Arbitrary imported CLR structs remain conservatively non-stampable because the compiler cannot yet prove their complete instance layout.

## An Explicit Declaration Guarantee

The `stampable` modifier requires the compiler to prove the property when it checks the declaration, even if nobody allocates or instantiates the type. The modifier records intent and prevents a later change from silently removing stampability.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/stampability/declaration-guarantee.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/stampability/declaration-guarantee.cs}}
```
{{#endtab }}
{{#endtabs }}

For a marked generic struct storing a field of type `T`, the declaration needs `where T : stampable`. An unconstrained `T` could be `string`, so it cannot satisfy the declaration guarantee. Adding a `string` instance field to either marked declaration also produces a compile-time error. The keyword demands the property; it does not make otherwise invalid storage safe.

The declaration modifier is valid only on ordinary structs. Classes and other reference types never qualify, and ref structs remain excluded.

## A Generic Constraint

`where T : stampable` gives generic code enough information to allocate `T` or a struct containing `T` while the type is still open. Callers may provide an inferred stampable type; its declaration does not need the modifier. Generic callers must carry a directly declared `stampable` bound when they forward their own type parameters.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/stampability/generic-allocation.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/stampability/generic-allocation.cs}}
```
{{#endtab }}
{{#endtabs }}

Calling this method with `string`, a struct containing a reference field, or an unconstrained forwarded parameter fails constraint checking. A `struct` constraint alone is insufficient: a value type can still contain references. `readonly` and `default` constraints also provide different guarantees. An indirect bound such as `where U : T` does not supply stampability proof for `U`.

The C# backend uses `unmanaged` for the representation guarantee needed by generic stack allocation. Mist still checks its own stampability rules; the emitted constraint does not expand the types Mist accepts.

## What Qualifies

| Type or property | Stampable? |
|---|---|
| Primitive value type, such as `int` | Yes |
| Source-defined ordinary struct whose instance storage is recursively stampable | Yes |
| Closed generic with stampable substituted instance storage | Yes |
| Open type parameter with a `stampable` bound | Yes |
| Mutable struct with stampable storage | Yes |
| Reference type or struct containing reference storage | No |
| Ref struct | No |
| Unconstrained stored type parameter | No proof |
| Enum, newtype, or refinement type | Excluded by the current rules |
| Imported CLR struct with complete, recursively stampable instance storage | Yes |
| Imported CLR struct with unknown or incomplete storage | No proof |

Imported storage is checked independently of member visibility. Private fields and compiler-generated property backing fields participate, including their substituted generic types. These fields do not become accessible Mist members. The CLR's implicit `System.ValueType` ancestor contributes no instance field; actual reference-valued storage still prevents stampability. Reference assemblies and unresolved or unsupported metadata can leave the layout unknown, so public fields alone never prove stampability.

CLR inline arrays and fixed buffers currently retain an unknown remainder because a single metadata field can represent repeated storage. Pointer and by-reference fields also prevent a complete value-storage proof.

Stampability is independent of mutability and readonly status. It also does not grant a constructor, create a new default, or replace the `default` constraint. See [Definite Assignment](./definite-assignment.md) for array initialization and [Stack-Only Spans](../advanced/stack-only-spans.md) for stack allocation and lifetime rules.

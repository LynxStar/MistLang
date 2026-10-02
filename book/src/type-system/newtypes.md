# Newtypes

A newtype is a renamed clone of an existing type. `type Jaguar = new Tiger;` builds `Jaguar` from `Tiger`'s own declaration -- the same base type, the same abstract bases, the same fields, properties, methods, constructors, operators and indexers -- with every reference to `Tiger` in that declaration replaced by `Jaguar`. What the newtype does not get is `Tiger`'s identity. A `Jaguar` is never a `Tiger`, statically or at runtime.

That gap is the point. A newtype guards the place where a raw value enters a domain: a `UserId` made from an `int` has to say so, and an `OrderId` cannot be passed where a `UserId` is expected. Leaving the domain costs nothing where a copy of the value is as good as the value itself.

## Declaring a Newtype

A newtype is declared with the `type` keyword, a name, and `= new` followed by the source type. The source can be a class or a struct declared in Mist, or a readonly intrinsic Standard type such as `int`, `double`, `bool` or `string`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/declaration.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/declaration.cs}}
```
{{#endtab }}
{{#endtabs }}

The source may also be named through a type alias, or be a constructed generic type (see [Generic Sources](#generic-sources)). An array, an enum, a union, a function type, a refinement, a type parameter or a CLR type cannot be the source of a newtype, and neither can a sealed class. A tuple source is covered by [Variadics](./variadics.md).

## A Renamed Clone

Because the newtype is its source's declaration bound again under a new name, a member that takes or returns the source takes or returns the newtype. `Tiger.Twin()` returns a `Tiger`, so `Jaguar.Twin()` returns a `Jaguar`. The source's base types come along too: a `Jaguar` is a `Cat` wherever `Tiger` is one.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/renamed-clone.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/renamed-clone.cs}}
```
{{#endtab }}
{{#endtabs }}

The type tests show the other half. A `Jaguar` passes `is Jaguar` and `is Cat`, and fails `is Tiger`, because nothing in the program makes a `Jaguar` a `Tiger`. The emitted C# keeps that true at runtime: the newtype is a class of its own, deriving from `Cat`, not from `Tiger`.

Only what the source's own declaration contains is cloned. A retroactive conformance such as `with Tiger : INamed { ... }`, or an extension block for `Tiger`, does not apply to `Jaguar`. A type nested inside `Tiger` is not cloned either; the clone refers to the same nested type. If a member of the source does not hold once renamed -- a `Tiger` method passing `this` to a parameter of type `Tiger` passes a `Jaguar` in the clone -- the compiler reports it at the newtype declaration, naming the member.

## Newtypes vs Type Aliases

| | Type Alias | Newtype |
|---|---|---|
| **Declaration** | `type Name = SourceType;` | `type Name = new SourceType;` |
| **Type identity** | The source type under another name | A distinct type that is never its source |
| **Members** | The source's | The source's, renamed |
| **Entering from the source** | Nothing to do | A cast, or a literal where the slot's type is in sight |
| **Leaving for the source** | Nothing to do | Implicit for a copyable source, a cast for a clonable class |
| **Emitted C#** | Erased | A type of its own |

Use an alias for a shorter or clearer name. Use a newtype when values that share a representation must not be mixed up.

## Intrinsic Sources

A newtype over an intrinsic Standard type is that type's contract renamed. `type UserId = new int;` has `int`'s operators and members, taking and returning `UserId`, so `first + (UserId)1` is a `UserId`. It also has a public readonly `Value` field holding the backing `int`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/intrinsic-sources.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/intrinsic-sources.cs}}
```
{{#endtab }}
{{#endtabs }}

A boxed `UserId` fails `is int`: the emitted newtype is a struct of its own holding the value, never the `int` itself.

## Copyable Sources

A struct, a readonly intrinsic type and a readonly class are **copyable**: a copy of one cannot be told from the original. A newtype over a copyable source converts in both directions, and the two directions differ on purpose.

- **Leaving is implicit.** A newtype value converts to its source wherever the source is expected, and the result is a copy. The conversion loses nothing, so it never counts as the one user-declared conversion a value may pass through: `int raw = next;` holds, and so does `long wide = next;`.
- **Entering takes a cast.** `(Position)point` calls the newtype's conversion from its source, and the result is the newtype itself, never an optional. The operand is matched as an argument would be, so it may reach the source through a declared implicit conversion first, and `(Meters)feet` works for two newtypes over `double` because a `Feet` leaves for `double` on its way in.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/copyable-conversions.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/copyable-conversions.cs}}
```
{{#endtab }}
{{#endtabs }}

Changing `point.X` leaves `position` untouched: `Point point = position;` copied it. The emitted conversions copy field by field, reaching each field through an `UnsafeAccessor` so neither type's private state is widened, and allocate a class uninitialized so the source's constructor does not run a second time.

Converting is not being. However freely the two convert, a newtype is never its source, so a type test between them is refused wherever the static type shows it -- `is`, a type pattern, a switch arm and `as` alike, in either direction, and for a type only the copy reaches such as `long` for a newtype over `int`:

```text
error MIST_PA_PATTERN_NEVER_MATCHES: A value of type 'Position' can never be 'Point', so this pattern never matches.
error MIST_PM_AS_NO_RELATIONSHIP: A value of type 'Position' can never be 'Point', so 'as' has no result type to produce. One must contain the other, or the source must be a union or unknown holding it; a conversion is not enough, since 'as' answers the value rather than a copy of it.
```

Write the conversion you meant instead: `Point point = position;` leaves, and `(Position)point` enters. A value whose static type cannot show the test is doomed -- one widened to `unknown` -- is not refused, and answers false when the program runs, as [A Renamed Clone](#a-renamed-clone) shows.

One copyable source has no field-by-field copy at all: one with a field whose type names the source. `Node? Next` is `Chain? Next` in the clone, so no field of `Node` stands for the corresponding field of `Chain`, and there is nothing to copy across. Such a source converts only if it says how it is copied, as the next section but one describes; if it does not, both directions are missing and either one is refused, naming the field:

```text
error MIST_PA_COPY_CONVERSION_DOES_NOT_HOLD: Cannot convert 'Node' to 'Chain'. The copy is field for field, and it does not hold at 'Next': a 'Node | void' in the source is a 'Chain | void' in the newtype. 'Node' implements no 'IClonable<Node>' to say how it is copied.
```

Declaring the newtype is still fine. Only converting is refused.

Without a cast, any other value is refused where a newtype is expected -- the source, and another newtype over the same source alike:

```text
error MIST_PA_CAST_REQUIRED: Cannot implicitly convert type 'int' to 'UserId'. An explicit cast exists (are you missing a cast?)
error MIST_PA_CAST_REQUIRED: Cannot implicitly convert type 'Feet' to 'Meters'. An explicit cast exists (are you missing a cast?)
```

## Literals in Sight

A literal enters a newtype without a cast when the type of the slot it is written into is in sight. `UserId id = 7;` names `UserId` right there, so the intent is stated. The positions that qualify are:

- the initializer of a local declared with the newtype;
- the right-hand side of a simple assignment to a variable, field, property or element of the newtype, including an object initializer entry;
- the initializer of a field or property declared with the newtype, and an entry in a `default` block for such a field;
- a `return` from a method, getter or operator declared to return the newtype;
- an element of an array literal written in one of those positions for an array of the newtype.

The literal has to be one the source accepts: `300` cannot enter a newtype over `byte`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/literals.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/literals.cs}}
```
{{#endtab }}
{{#endtabs }}

Everywhere else a literal needs its cast. A call argument is the case that matters most: with `Describe(UserId id)`, the call `Describe(9)` shows no `UserId` at the call site, and two swapped literal arguments are exactly the mistake a newtype exists to catch.

```text
error MIST_PA_CAST_REQUIRED: Cannot implicitly convert type 'int' to 'UserId'. An explicit cast exists (are you missing a cast?)
```

The same goes for an operand of an operator, a branch of a `switch` expression, and the result of a lambda.

## Clonable Class Sources

A class that is not readonly has no copy Mist could define for it: shallow or deep, private state, owned fields are all the class's own business. So a newtype and a mutable class source convert only when the class says how it is copied, by implementing `IClonable` of itself.

When it does, both conversions are explicit, and each runs a `Clone` body with the operand as `this`:

- `(Jaguar)tiger` runs `Jaguar`'s renamed `Clone`, so it reads `tiger`'s state and constructs a `Jaguar`.
- `(Tiger)jaguar` runs `Tiger`'s own `Clone`, reading `jaguar`'s state and constructing a `Tiger`.

Only the receiver changes. A static member the body names belongs to the type whose `Clone` it is, and the body reaches every member of either type that it reads, private ones included, since it is the source's own code.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/clonable-classes.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/clonable-classes.cs}}
```
{{#endtab }}
{{#endtabs }}

The two casts into `Jaguar` count `made` to 2 in `Jaguar`'s statics, while the cast back counts 1 in `Tiger`'s. In the emitted C#, each conversion is an operator on the newtype whose parameter stands for `this`, and a member C# would refuse from the newtype -- a private field of `Tiger` -- is reached through an `UnsafeAccessor` declared beside it.

A class with no `IClonable` of itself, including one that only inherits `IClonable` of a base, defines no copy, and a cast either way is refused:

```text
error MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY: Cannot cast 'Entity' to 'Actor'. 'Entity' defines no copy to convert with; a newtype and its mutable class source convert only when the class implements 'IClonable<Entity>' with a Clone of its own.
```

A `Clone` body can also fail to hold with the other type as `this`. A field declared `Tiger? Mate` is `Jaguar? Mate` in the clone, so a body that **shares** `Mate` -- copies the reference across rather than cloning it -- passes a `Tiger` where a `Jaguar` is needed, and the cast is refused, naming the member:

```text
error MIST_PA_CLONE_CONVERSION_DOES_NOT_HOLD: Cannot cast 'Tiger' to 'Jaguar'. The cast runs 'Jaguar.Clone' with a 'Tiger' as this, and it does not hold at 'Mate': Cannot implicitly convert type 'Tiger | void' to 'Jaguar | void'.
```

A body that **clones** the field holds, and that is how a source with a field naming itself converts at all. Within a conversion, cloning such a field *is* the conversion: `this.Next?.Clone()` converts `Next` rather than cloning it and then needing a second step, and answers the newtype. A type test naming the result type tests for the operand, so the deep-`Clone` spelling `if(this.Next is Node next) { ... next.Clone() ... }` works too -- without that rule the test would ask whether a `Node` is already a `Chain`, which is never true, and the conversion would quietly copy only the first element.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/self-typed-field.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/self-typed-field.cs}}
```
{{#endtab }}
{{#endtabs }}

`chain 1 2` holds both elements, and mutating `nodes`' second element afterwards does not reach it: the whole chain was copied. In the emitted C#, the operator's body casts where the Mist wrote `Clone()`, calling itself once per element, and an absent `Next` answers null rather than reaching the operator.

## Generic Sources

The source of a newtype can be a constructed generic type, and a newtype can declare type parameters of its own. `type IntBox = new Box<int>;` is `Box`'s declaration with `T` bound to `int`, so `IntBox.Get()` returns an `int`. `type Boxed<T> = new Box<T>;` is a generic clone.

Inside the clone, the source named with type arguments is the newtype exactly when some instance of the newtype is a clone of that construction. In `IntBox`, `Box<T>` is `IntBox`, while `Box<string>` stays a `Box<string>`, since no `IntBox` clones it. In `Boxed<T>`, `Box<string>` is `Boxed<string>`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/generic-sources.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/generic-sources.cs}}
```
{{#endtab }}
{{#endtabs }}

A constraint on the source's type parameter moves to the newtype's parameter it is bound to. A newtype over a constructed source gets the same conversions as any other: `IntBox` over a clonable `Box<int>`, or a newtype over a constructed struct, converts as described above.

An instance of a generic newtype converts with the matching instance of its source, and with no other: with `type Twin<T> = new Pair<T, T>;`, a `Twin<int>` leaves to a `Pair<int, int>` and enters from one by a cast, while a `Pair<string, string>` needs a `Twin<string>`. A literal enters such an instance in the same visible slots, so `Id<User> id = 5;` is valid over `type Id<T> = new int;`.

> **Not yet implemented:** an *operator* on an instance of a generic newtype, such as `id + id` with `type Id<T> = new int;`, is refused. It waits on operators declared on generic types being found for their constructed instances, which no generic type has yet. A newtype that declares a pack, or whose source does, converts nothing yet either.

## How Newtypes Compile to C#

A newtype is never lowered to its source, because a value of the source's C# type would pass the type test the newtype must fail. The emitted shape follows the source:

- **A class source** becomes a class named after the newtype, with the source's base list, members and bodies renamed. A clonable source adds an explicit conversion operator in each direction, emitted only when some cast calls it.
- **A struct or readonly class source** does the same, adding an implicit conversion to the source and an explicit one from it, each copying every field.
- **An intrinsic source** becomes a struct holding the backing value in `Value`, supplying each member of the source's contract by forwarding to the backing value and wrapping a result of the newtype.

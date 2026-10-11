# Dependent Types

> **⚠️ Unvetted Content:** From this chapter onward, the book content is auto-generated from spec documents and sample code using Opus and has not been manually reviewed for accuracy. Descriptions may be incorrect or misleading. All code snippets are at least valid compilable Mist, and the `samples/` directory in the repository contains working programs that fully demonstrate each feature end-to-end — trust the code over the prose when in doubt.

Dependent types let you parameterize a type by a compile-time constant value rather than another type. Where generics say "this works for any type T," dependent types say "this is specifically sized to 5" or "this has exactly 3 rows." The value becomes part of the type's identity, giving you stronger compile-time guarantees about dimensions, capacities, and configurations without runtime overhead.

Mist dependent types are related to but distinct from refinement types. Refinement types constrain the *range* of values an existing type can hold (e.g., "an int that is positive"). Dependent types parameterize the *type itself* by a constant value, creating a family of distinct types indexed by that value.

## Dependent Parameter Syntax

A dependent type parameter uses the `Name:ValueType` syntax inside the type parameter list. The colon separates the parameter name from its value type — this distinguishes it from a regular generic type parameter.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/syntax.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/syntax.cs}}
```
{{#endtab }}
{{#endtabs }}

In the emitted C#, the dependent parameter `Size:int` becomes a generic type parameter `TSize` constrained to the `IConstIntegral` witness interface. The compiler generates a public static property `Size` that reads the value through the carrier type and converts it to the declared `int`. This encoding preserves type identity at the .NET level while giving Mist the ability to treat the value as a compile-time constant.

## Allowed Value Types

Dependent parameters support all integral types, `bool`, and `char`. These are the types that can serve as compile-time constants:

- `int`, `uint`, `long`, `ulong`
- `short`, `ushort`, `byte`, `sbyte`
- `bool`
- `char`

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/allowed-types.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/allowed-types.cs}}
```
{{#endtab }}
{{#endtabs }}

The eight integral types share one witness, `IConstIntegral`, which holds the value as an `Int128`; `bool` and `char` each have their own (`IConstBool`, `IConstChar`). A carrier struct is named for the value itself (`Const_42`, `Const_Neg1`, `Const_True`, `Const_Char_0x0058`), so one value is one C# type whichever slot it fills, and each read converts it back to the reading parameter's declared type. The compiler only emits the witnesses for the kinds a parameter declares.

Every integral type admits its whole range, `ulong` included. Spellings of one value are one type argument: `Mask<0xFFFF_FFFF_FFFF_FFFF>` and `Mask<18446744073709551615>` are the same type, as `Size<5>`, `Size<5L>` and `Size<0b101>` are.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/full-range.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/full-range.cs}}
```
{{#endtab }}
{{#endtabs }}

The three kinds never mix, and a value must fit its slot's type. Each mismatch is refused where the type argument is written, whether in a construction, a method call, an alias, or a base clause:

```text
error MIST_DT_0003: Dependent type argument '256' of type 'int' is not assignable to dependent parameter 'N' of type 'byte'.
error MIST_DT_0003: Dependent type argument 'true' of type 'bool' is not assignable to dependent parameter 'N' of type 'int'.
error MIST_DT_0003: Dependent type argument 'A' of type 'char' is not assignable to dependent parameter 'N' of type 'int'.
```

A `char` never fills an integral slot, even though a `char` expression widens to `int`: `'A'` and `65` would then be two spellings of one type argument interned as two types.

## Using Dependent Parameters as Values

Inside the type body, a dependent parameter name acts as a static value. You can use it in expressions, return it from methods, embed it in string interpolation, or pass it to other methods — anywhere a value of that type is expected.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/using-values.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/using-values.cs}}
```
{{#endtab }}
{{#endtabs }}

Multiple dependent parameters each get their own carrier type parameter and static property. The values are available as static members, so they don't occupy instance storage.

A dependent parameter read as a value has its declared type, so `var doubled = Size * 2;` infers an `int`, and it answers that type's members: `Size.ToString()`.

## Reading a Dependent Value from Outside the Type

Each dependent parameter of a class, struct or abstract type is also a public static get-only property of that type, named after the parameter and typed by its annotation. Read it through a constructed type, through an alias of one, or through any type deriving from one:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/properties.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/properties.cs}}
```
{{#endtab }}
{{#endtabs }}

The read is spelled on the construction that declares the property, so `SmallBuffer.Size` becomes `FixedBuffer<Const_10>.Size`. An abstract type's property is read the same way through a type implementing it. The property belongs to the type rather than to its instances, and it cannot be assigned:

```text
error MIST_PM_UNRESOLVED_MEMBER: 'FixedBuffer<5>' has no member named 'Size'.
error MIST_PA_NO_SET_ACCESSOR: Cannot assign to 'FixedBuffer.Size': it has no 'set' or 'init' accessor.
```

The first is `buffer.Size` on an instance, the second `FixedBuffer<5>.Size = 6;`. An ordinary type parameter has no such property, a dependent parameter may not share its name with a member of its own type (`MIST_DT_0005`), and two parameters of one list may not share a name (`MIST_PM_DUPLICATE_TYPE_PARAMETER`).

A type alias projects its own dependent parameters too, under the names the alias declares. `Sized<6>.Count` is `6`, and so is `Tagged<7>.Count` though the alias stands for `string` and never names the value: the read is the value the use supplies, so it is emitted as that value, `((int)6)`, rather than as a member of the target. The alias is still transparent everywhere else -- `Sized<6>` is `FixedBuffer<6>`, whose own `Size` reads through it -- and an alias forwarding to another keeps only its own names, so `type Outer<P:int> = Sized<P>;` answers `Outer<4>.P` and not `Outer<4>.Count`.

## Dependent Types on Classes

Classes are the most common host for dependent type parameters. You can mix regular type parameters with dependent parameters in the same parameter list.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/classes.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/classes.cs}}
```
{{#endtab }}
{{#endtabs }}

Regular type parameters (`T`) pass through unchanged. Dependent parameters (`Size:int`) are transformed into constrained carrier parameters (`TSize : IConstIntegral`). At the call site, `new TypedBuffer<string, 16>("hello")` supplies `string` for `T` and a carrier struct `Const_16` for `TSize`.

## Dependent Types on Structs

Structs support dependent type parameters with the same syntax and semantics as classes.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/structs.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/structs.cs}}
```
{{#endtab }}
{{#endtabs }}

Since structs are value types, a `FixedSpan<3>` carries no heap allocation. The dependent value is accessed through the static property, adding zero per-instance overhead.

## Dependent Types on Abstract Types

Abstract types can declare dependent type parameters to define contracts that depend on a constant value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/abstracts.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/abstracts.cs}}
```
{{#endtab }}
{{#endtabs }}

When a class implements an abstract type with a dependent parameter, it supplies a concrete value (here `8`). The compiler emits the corresponding carrier struct (`Const_8`) as the type argument, and `TinyCollection.Size` reads `8`.

## Dependent Types on Methods

Methods can declare their own dependent type parameters, independent of the enclosing class. This is useful for operations that need a compile-time constant without requiring the entire class to be parameterized.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/methods.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/methods.cs}}
```
{{#endtab }}
{{#endtabs }}

At the call site, you supply the constant value as a type argument: `DependentMethods.Repeat<3>("hello")`. The compiler resolves `3` to the carrier struct `Const_3`. The `Process` method demonstrates forwarding a dependent parameter — it accepts any `FixedBuffer<N>` and the `N` flows through as a type parameter.

## Forwarding and Widening

A dependent parameter can fill another type's dependent slot, as `N` fills `FixedBuffer<N>` above or a derived class forwards its own parameter to its base. The forwarded parameter's type must widen to the slot's: an `int` parameter may fill a `long` slot.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/forwarding.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/forwarding.cs}}
```
{{#endtab }}
{{#endtabs }}

Because a carrier names the value rather than the slot, `NarrowCounter<7>` derives from exactly the `WideCounter<7>` written directly: the assignment compiles, and an `is WideCounter<7>` test on a `NarrowCounter<7>` is true at runtime. Narrowing is refused at the forwarding site, as is any forward between `bool`, `char` and the integral types:

```text
error MIST_DT_0004: Dependent type parameter 'M' of type 'long' on 'Derived' cannot be forwarded to parameter 'N' of type 'int' on 'Base' because 'long' is not implicitly convertible to 'int'.
```

A slot whose type is a refinement also asks the forwarded parameter's own refinement to imply it (`MIST_PA_DEPENDENT_FORWARD_UNPROVEN`).

## Relationship to Refinement Types

Dependent types and refinement types serve different purposes:

- **Refinement types** constrain the *values* a variable can hold at runtime. A `int where > 0` is still an `int`, but the compiler and runtime enforce that it's always positive. Arithmetic on a refined type produces the base type.

- **Dependent types** parameterize the *type itself* by a compile-time constant. A `FixedBuffer<5>` is a fundamentally different type from `FixedBuffer<10>` — they cannot be assigned to each other. The constant is baked into the type's identity.

Use refinement types when you want to restrict what values flow through a variable. Use dependent types when the constant should be part of the type's structure — dimensions, capacities, protocol versions, or any value that distinguishes one instantiation from another at the type level.

## Refinement Type Constraints

While the previous section explained the *conceptual* difference between refinement types and dependent types, there is a direct syntactic connection between the two: you can use a refinement type as a dependent parameter constraint.

When you write `Count:int`, you're saying "Count is a compile-time int." But you can also write `Count:PositiveInt` where `PositiveInt` is a refinement type alias — the compiler resolves the refinement's base type and uses that as the dependent parameter's value type. This lets you document intent at the type-parameter level: the parameter isn't just any int, it's specifically a positive int.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/refinement-constraints.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/refinement-constraints.cs}}
```
{{#endtab }}
{{#endtabs }}

The refinement type's base type must be in the set of allowed dependent value types (integral types, `bool`, and `char`). In the emitted C#, the constraint resolves to the same carrier-interface pattern as a plain primitive constraint — `PositiveInt` has base type `int`, so the emitted code uses `IConstIntegral` and reads the value as an `int`.

## Type Identity Semantics

Different dependent values produce distinct types. `FixedBuffer<5>` and `FixedBuffer<10>` are not interchangeable — you cannot assign one to the other, and a function expecting `FixedBuffer<5>` will not accept a `FixedBuffer<10>`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/type-identity.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/type-identity.cs}}
```
{{#endtab }}
{{#endtabs }}

The function `AcceptFive` only accepts `FixedBuffer<5>`. Passing a `FixedBuffer<10>` would be a compile-time error because `Const_5` and `Const_10` are different types — the .NET type system enforces the distinction. This gives you compile-time guarantees about dimensions and capacities that would otherwise require runtime checks.

You can also use pattern matching to check dependent type identity at runtime:

```mist
{{#include ../../snippets/type-system/dependent-types/pattern-matching.mist}}
```

### Testing a sized array or span

Arrays and spans are dependent on their length too: `int[3]` is `Array<int, 3>`, and `Span<int, 3>` carries its length the same way. Their C# spelling drops it -- `int[3]` is `int[]` in the emitted code, and `Span<int, 3>` is `System.Span<int>` -- so a runtime test cannot be spelled as the C# type alone, or a five-element array would pass `is int[3]` and bind as one. The compiler writes the length into every runtime test instead: `int[] { Length: 3 } three`. Distinct sized switch arms stay distinct, and an unsized arm after them still catches every other length.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/sized-patterns.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/sized-patterns.cs}}
```
{{#endtab }}
{{#endtabs }}

The same check reaches a length inside a tuple, `(int[3], string)`, as `{ Item1: { Length: 3 } }`, and `as int[3]` answers `void` for any other length. A test of a sequence whose elements carry their own length, such as `int[3][2]`, is refused (`MIST_BE_UNSUPPORTED_SIZED_ELEMENT_TEST`): no C# pattern can check every element.

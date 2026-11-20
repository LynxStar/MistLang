# Compound Assignment and Increment

Mist has the compound assignment operators `+=`, `-=`, `*=`, `/=` and `%=`, and the increment and decrement operators `++` and `--`. Each is shorthand for an ordinary assignment: `x op= e` means `x = x op e`, and `x++` as a statement means `x = x + 1`, with the 1 of `x`'s own type. The shorthand selects the same operator, produces the same result type and follows the same rules as the long form, so whatever is valid for `x = x op e` is valid for `x op= e`, and nothing else is.

## Compound Assignment

The target can be anything an ordinary [assignment](variables.md) can write: a local, a field, a property with a setter, or an element reached through an indexer.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/compound-assignment/compound-operators.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/compound-assignment/compound-operators.cs}}
```
{{#endtab }}
{{#endtabs }}

`text += 3` is string concatenation, exactly as `text = text + 3` is. `text -= "b"` is refused, because strings have no `-` operator. Like `=`, a compound assignment can be used as a value: `var y = (x += 2);` gives `y` the value written.

When the target has a refined type, the value written must meet its refinement, as it must for `=`. With `type Percentage = int >= 0 and <= 100;` and `Percentage p = 50;`, `p += 10` is valid, and a following `p += 60` is refused because it would write `120`. See [Refinement Types](../type-system/refinement-types.md#proof-follows-the-program) for how the compiler proves it.

## The Target Is Evaluated Once

The target's receiver and index are evaluated once, and the element they name is both read and written. The order is the receiver and index, then the value on the right, then the operation, then the write.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/compound-assignment/evaluated-once.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/compound-assignment/evaluated-once.cs}}
```
{{#endtab }}
{{#endtabs }}

`picker.Next()` runs once per statement, so the first statement adds 5 to `items[0]` and the second multiplies `items[1]` by 3. The log shows the index evaluated before the value.

## Division and Remainder

`/=` and `%=` follow the [division rules](../standard-library/results.md) of `/` and `%`. When the compiler can prove the division safe, the result is an ordinary number and is written back. When it cannot, `x / y` is a `Result<int, DivisionError>`, which an `int` cannot hold, so the compound form is refused exactly as the long form is:

```text
error MIST0157: Cannot implicitly convert type 'Result<int, DivisionError>' to 'int'.
```

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/compound-assignment/division.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/compound-assignment/division.cs}}
```
{{#endtab }}
{{#endtabs }}

Inside `if (divisor > 0)` the divisor is proven positive, so `pool /= divisor` is valid. A constant divisor, as in `total /= 4` above, needs no check.

## User-Declared Operators

When the operand types declare the operator, the compound form calls it.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/compound-assignment/user-operator.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/compound-assignment/user-operator.cs}}
```
{{#endtab }}
{{#endtabs }}

`position += new Vec2(10, 20)` calls `Vec2`'s `+` and stores the result in `position`.

## Increment and Decrement

`++` adds one and `--` subtracts one, using the variable's own type for the one, as [Narrow Types](#narrow-types) shows. As a statement, `x++` and `++x` mean `x = x + 1`, and `x--` and `--x` mean `x = x - 1`. Inside an expression they behave as in C#: postfix yields the value before the write, and prefix yields the value after it.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/compound-assignment/increment.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/compound-assignment/increment.cs}}
```
{{#endtab }}
{{#endtabs }}

An increment inside a larger expression yields a value the compiler knows only by its type. That is enough to index a `List`, but an array or span index must be proven in bounds, so `items[k++]` on an array is refused:

```text
error MIST0275: Index 'k++' is not proven within the bounds of 'items', which needs >= 0 and <= 2 (fails when k++ <= -1).
```

Read with `items[k]` and increment `k` in its own statement. As a statement, `k++` records that `k` is one more than before whenever that cannot wrap, which is how a loop index stays proven; see [Loops](../type-system/refinement-types.md#loops).

## Narrow Types

The short form means the long form on narrow integral types too. `byte + byte` is a `byte` that wraps, so a `byte` operand works:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/compound-assignment/narrow-types.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/compound-assignment/narrow-types.cs}}
```
{{#endtab }}
{{#endtabs }}

`250 + 10` wraps to `4`. A literal operand is an `int`, though, so `level + 10` selects `int`'s operator, and `level += 10` is refused because an `int` does not fit back into a `byte` without a cast. The same holds for `sbyte`, `short` and `ushort`. On a `uint`, adding an `int` produces a `long`, with the same result.

`++` and `--` are different: they add or subtract 1 of the variable's own type through that type's own operator, so `top++` is `top = top + (byte)1`. They work on every integral type except `char`, and on `float` and `double`, and wrap as the type's own addition does: `top` goes from 255 to 0. `char` has no addition, and `decimal`'s addition produces a `Result`, so neither has `++` or `--`.

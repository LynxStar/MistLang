# Primitive Types

Mist provides a full set of primitive types for working with numbers, text, and boolean logic. These types map directly to their .NET counterparts, so the emitted C# uses the same type names and literal syntax you'd expect.

## Integer Types

Mist supports all standard .NET integer types. Each type has a fixed size and range.

| Type | Size | Range |
|------|------|-------|
| `byte` | 8-bit unsigned | 0 to 255 |
| `sbyte` | 8-bit signed | -128 to 127 |
| `short` | 16-bit signed | -32,768 to 32,767 |
| `ushort` | 16-bit unsigned | 0 to 65,535 |
| `int` | 32-bit signed | -2³¹ to 2³¹-1 |
| `uint` | 32-bit unsigned | 0 to 2³²-1 |
| `long` | 64-bit signed | -2⁶³ to 2⁶³-1 |
| `ulong` | 64-bit unsigned | 0 to 2⁶⁴-1 |

{{#tabs }}
{{#tab name="Mist" }}
```mist
int i = 42;
uint ui = 100u;
long l = 100L;
ulong ul = 999UL;
short s = 16;
ushort us = 32;
byte b = 255;
sbyte sb = -128;
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
int i = 42;
uint ui = 100u;
long l = 100L;
ulong ul = 999UL;
short s = 16;
ushort us = 32;
byte b = 255;
sbyte sb = -128;
```
{{#endtab }}
{{#endtabs }}

## Floating-Point and Decimal Types

For fractional numbers, Mist provides `float` (32-bit), `double` (64-bit), and `decimal` (128-bit high-precision).

{{#tabs }}
{{#tab name="Mist" }}
```mist
float f = 2.5f;
double d = 3.14;
decimal m = 99.99m;
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
float f = 2.5f;
double d = 3.14;
decimal m = 99.99m;
```
{{#endtab }}
{{#endtabs }}

A numeric literal without a suffix defaults to `int` for whole numbers and `double` for numbers with a decimal point.

## Numeric Literal Suffixes

Suffixes tell the compiler which numeric type a literal represents:

| Suffix | Type |
|--------|------|
| `u` | `uint` |
| `L` | `long` |
| `UL` | `ulong` |
| `f` | `float` |
| `m` | `decimal` |

Without a suffix, integer literals are `int` and floating-point literals are `double`.

## Hex Literals

Integer values can be written in hexadecimal using the `0x` prefix:

{{#tabs }}
{{#tab name="Mist" }}
```mist
int hex = 0xFF;
Console.WriteLine($"hex={hex}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
int hex = 0xFF;
Console.WriteLine($"hex={hex}");
```
{{#endtab }}
{{#endtabs }}

The value `0xFF` is the integer 255. Hex literals can be used anywhere an integer literal is expected.

## Bool

The `bool` type represents a truth value — either `true` or `false`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
bool flag = true;
bool done = false;
Console.WriteLine($"flag={flag} done={done}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
bool flag = true;
bool done = false;
Console.WriteLine($"flag={flag} done={done}");
```
{{#endtab }}
{{#endtabs }}

## Char

The `char` type holds a single Unicode character, written with single quotes:

{{#tabs }}
{{#tab name="Mist" }}
```mist
char ch = 'Z';
Console.WriteLine($"char={ch}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
char ch = 'Z';
Console.WriteLine($"char={ch}");
```
{{#endtab }}
{{#endtabs }}

## String

The `string` type holds a sequence of characters, written with double quotes:

{{#tabs }}
{{#tab name="Mist" }}
```mist
string greeting = "Hello, Mist!";
Console.WriteLine(greeting);
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
string greeting = "Hello, Mist!";
Console.WriteLine(greeting);
```
{{#endtab }}
{{#endtabs }}

### Escape Sequences

Strings support standard escape sequences for special characters:

| Escape | Meaning |
|--------|---------|
| `\n` | Newline |
| `\t` | Tab |
| `\\` | Backslash |
| `\"` | Double quote |

{{#tabs }}
{{#tab name="Mist" }}
```mist
string escaped = "Line1\nLine2\tTabbed";
Console.WriteLine(escaped);
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
string escaped = "Line1\nLine2\tTabbed";
Console.WriteLine(escaped);
```
{{#endtab }}
{{#endtabs }}

### String Interpolation

Prefixing a string with `$` enables interpolation. Expressions inside `{` and `}` are evaluated and inserted into the string:

{{#tabs }}
{{#tab name="Mist" }}
```mist
int i = 42;
string interp = $"The answer is {i}";
string interpExpr = $"Doubled: {i * 2}";
Console.WriteLine(interp);
Console.WriteLine(interpExpr);
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
int i = 42;
string interp = $"The answer is {i}";
string interpExpr = $"Doubled: {i * 2}";
Console.WriteLine(interp);
Console.WriteLine(interpExpr);
```
{{#endtab }}
{{#endtabs }}

Interpolated strings can contain any expression, including method calls and arithmetic. The expression result is converted to a string using its `ToString()` method.

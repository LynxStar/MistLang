# Primitive Types

Mist provides a full set of primitive types for working with numbers, text, and boolean logic.

## Type Keywords and Standard Library Types

The primitive type keywords in Mist are reserved aliases that always resolve to types in the Standard Library, regardless of what other types might exist with the same name in user-defined namespaces. Even if you declare your own `Standard.Int32`, the keyword `int` still refers to the compiler's built-in Standard Library type.

| Keyword | Standard Library Type |
|---------|----------------------|
| `int` | `Standard.Int32` |
| `uint` | `Standard.UInt32` |
| `long` | `Standard.Int64` |
| `ulong` | `Standard.UInt64` |
| `short` | `Standard.Int16` |
| `ushort` | `Standard.UInt16` |
| `byte` | `Standard.Byte` |
| `sbyte` | `Standard.SByte` |
| `float` | `Standard.Single` |
| `double` | `Standard.Double` |
| `decimal` | `Standard.Decimal` |
| `bool` | `Standard.Boolean` |
| `char` | `Standard.Char` |
| `string` | `Standard.String` |
| `regex` | `Standard.Regex` |

These are not normal type aliases that participate in name resolution — they are hard-wired by the compiler. Writing `int x = 5;` is identical to writing `Standard.Int32 x = 5;`, and no user code can change what `int` means.

## Integer Types

Mist supports signed and unsigned integer types at various bit widths. Each type has a fixed size and range.

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
{{#include ../../snippets/basics/primitive-types/integer-types.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/primitive-types/integer-types.cs}}
```
{{#endtab }}
{{#endtabs }}

## Floating-Point and Decimal Types

For fractional numbers, Mist provides `float` (32-bit), `double` (64-bit), and `decimal` (128-bit high-precision).

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/primitive-types/floating-point.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/primitive-types/floating-point.cs}}
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
{{#include ../../snippets/basics/primitive-types/hex-literals.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/primitive-types/hex-literals.cs}}
```
{{#endtab }}
{{#endtabs }}

The value `0xFF` is the integer 255. Hex literals can be used anywhere an integer literal is expected.

## Binary Literals

Integer values can also be written in binary using the `0b` prefix:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/primitive-types/binary-literals.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/primitive-types/binary-literals.cs}}
```
{{#endtab }}
{{#endtabs }}

Binary literals are useful for bit masks, flags, and hardware-related code where the individual bit pattern matters.

## Digit Separators

Numeric literals can include underscores (`_`) as visual separators to improve readability. The compiler ignores them — they don't affect the value:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/primitive-types/digit-separators.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/primitive-types/digit-separators.cs}}
```
{{#endtab }}
{{#endtabs }}

Underscores work in decimal, hex, and binary literals. Place them wherever they help you read the number — between digit groups, after prefixes, or to separate nibbles in binary.

## Bool

The `bool` type represents a truth value — either `true` or `false`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/primitive-types/bool.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/primitive-types/bool.cs}}
```
{{#endtab }}
{{#endtabs }}

## Char

The `char` type holds a single Unicode character, written with single quotes:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/primitive-types/char.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/primitive-types/char.cs}}
```
{{#endtab }}
{{#endtabs }}

## String

The `string` type holds a sequence of characters, written with double quotes:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/primitive-types/string.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/primitive-types/string.cs}}
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
{{#include ../../snippets/basics/primitive-types/escape-sequences.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/primitive-types/escape-sequences.cs}}
```
{{#endtab }}
{{#endtabs }}

### String Interpolation

Prefixing a string with `$` enables interpolation. Expressions inside `{` and `}` are evaluated and inserted into the string:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/primitive-types/string-interpolation.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/primitive-types/string-interpolation.cs}}
```
{{#endtab }}
{{#endtabs }}

Interpolated strings can contain any expression, including method calls and arithmetic. The expression result is converted to a string using its `ToString()` method.

## Regex

The `regex` type provides first-class support for regular expressions. Regex literals use backtick delimiters, making patterns easy to write without excessive escaping.

> **Note:** Mist does not define its own regex flavor or engine. The regex syntax and matching behavior are determined entirely by the backend. With the current C# transpiler backend, regex literals use .NET's `System.Text.RegularExpressions` engine. Future backends may use a different engine with different capabilities.

### Regex Literal Syntax

A regex literal is written by enclosing a pattern in backticks. The compiler infers the type as `regex`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/regex-type/literal-syntax.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/regex-type/literal-syntax.cs}}
```
{{#endtab }}
{{#endtabs }}

The backtick syntax avoids the double-backslash escaping that string-based regex patterns require. Inside backticks, `\d` means the regex digit metacharacter directly.

### Explicit Type Annotation

You can declare a variable with an explicit `regex` type annotation:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/regex-type/explicit-type.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/regex-type/explicit-type.cs}}
```
{{#endtab }}
{{#endtabs }}

### IsMatch (Partial Match)

The `IsMatch` method checks whether the pattern matches anywhere within the input string. It returns `true` if any substring of the input matches the pattern:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/regex-type/is-match.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/regex-type/is-match.cs}}
```
{{#endtab }}
{{#endtabs }}

In this example, `\bcat\b` matches the word "cat" as a whole word. It finds a match in `"the cat sat"` and `"cat"`, but not in `"concatenate"` because the word boundaries don't align.

### IsFullMatch (Full Match)

The `IsFullMatch` method checks whether the entire input string matches the pattern from start to end. Unlike `IsMatch`, a partial match is not sufficient:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/regex-type/is-full-match.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/regex-type/is-full-match.cs}}
```
{{#endtab }}
{{#endtabs }}

The pattern `\d{3}` matches exactly three digits. `IsFullMatch("123")` returns `true` because the entire string is three digits. `IsFullMatch("1234")` returns `false` because the string has four characters, even though it contains a three-digit substring.

# Standard Library

The Mist standard library provides foundational types and functions that are available to all Mist programs without explicit import. These definitions ship alongside the compiler and are automatically included in every compilation — you never need a `using Standard;` declaration to access them.

The standard library serves two purposes:

1. **Intrinsic type definitions** — Declares the built-in numeric types, strings, booleans, and other fundamental types with their operators and conversions.
2. **Core abstractions** — Provides utility types like `Optional<T>`, the `Void` value type, universal methods available on all values, and runtime reflection via `Type`.

## Namespace

All standard library types live in the `Standard` namespace. Because the compiler injects them automatically, they are accessible from any Mist source file without qualification.

## Modules

The standard library is organized into two logical groups:

| Module | Purpose |
|--------|---------|
| **Bootstrap** | Intrinsic types that map directly to .NET CLR types, providing operators and implicit conversions. |
| **Core** | Higher-level types and extensions written in Mist itself (or declared as intrinsic entry points) that build on the bootstrap types. |

## Bootstrap Types

The bootstrap module defines the intrinsic types that form the foundation of the Mist type system. Each type is declared as an `intrinsic` struct or class, meaning the compiler maps it directly to the corresponding .NET type at emit time.

### Numeric Types

| Type | Category | Description |
|------|----------|-------------|
| `Int32` (`int`) | Struct | 32-bit signed integer with arithmetic, comparison, and unary negation operators. Widens implicitly to `Int64`, `Single`, `Double`, and `Decimal`. |
| `Int64` (`long`) | Struct | 64-bit signed integer with arithmetic, comparison, and unary negation operators. Widens implicitly to `Single`, `Double`, and `Decimal`. |
| `Int16` (`short`) | Struct | 16-bit signed integer with arithmetic, comparison, and unary negation operators. Widens implicitly to `Int32`, `Int64`, `Single`, `Double`, and `Decimal`. |
| `Byte` (`byte`) | Struct | 8-bit unsigned integer with arithmetic and comparison operators. Widens implicitly to all larger numeric types. |
| `SByte` (`sbyte`) | Struct | 8-bit signed integer with arithmetic, comparison, and unary negation operators. Widens implicitly to `Int16`, `Int32`, `Int64`, `Single`, `Double`, and `Decimal`. |
| `UInt32` (`uint`) | Struct | 32-bit unsigned integer with arithmetic and comparison operators. Widens implicitly to `Int64`, `UInt64`, `Single`, `Double`, and `Decimal`. |
| `UInt64` (`ulong`) | Struct | 64-bit unsigned integer with arithmetic and comparison operators. Widens implicitly to `Single`, `Double`, and `Decimal`. |
| `UInt16` (`ushort`) | Struct | 16-bit unsigned integer with arithmetic and comparison operators. Widens implicitly to `Int32`, `UInt32`, `Int64`, `UInt64`, `Single`, `Double`, and `Decimal`. |
| `Single` (`float`) | Struct | 32-bit IEEE 754 floating-point with arithmetic, comparison, and unary negation operators. Widens implicitly to `Double`. |
| `Double` (`double`) | Struct | 64-bit IEEE 754 floating-point with arithmetic, comparison, and unary negation operators. |
| `Decimal` (`decimal`) | Struct | 128-bit high-precision decimal with arithmetic, comparison, and unary negation operators. |

All numeric types support the standard arithmetic operators (`+`, `-`, `*`, `/`, `%`) and comparison operators (`<`, `>`, `<=`, `>=`, `==`, `!=`). Signed types additionally support unary negation (`-`); `byte`, `ushort`, `uint`, `ulong`, and `char` do not. Logical not (`!`) applies to `bool`. A class or struct can support these unary operations by declaring the corresponding operator; otherwise the compiler reports `MIST_PA_OPERATOR_NOT_APPLICABLE` at the expression. An `unknown` operand must first be narrowed to a type that supports the operation.

User-defined operators must be `public static`. The existing `pure` effect modifier and `intrinsic` declarations on intrinsic types remain supported; `sealed`, `readonly`, `owned`, and `affine` operator modifiers are refused. An operator signature consists of its symbol and parameter types, including arity; changing only its return type does not create another overload. A duplicate signature is diagnosed at the later declaration and identifies the earlier declaration.

An operator expression calls an operator declared on one of its operand types or on that type's base classes. A generic type's operators apply at the construction they are found on: on a `Tag<int>`, an operator `Tag<T>` declares over `T` takes `Tag<int>` operands and returns `int`, and an operator declared on a generic base is reached through a derived type with the arguments its base clause supplies. When several operators apply, the compiler chooses as it chooses among method overloads: an exact parameter beats a converted one, a narrower parameter beats a wider one, an integer constant reaches a smaller integral parameter of a declared operator that holds its value, and a parameter fixed at its declaration beats one the owner's type argument supplies. Operators that remain equally applicable, such as one signature declared on both operand types, are refused with `MIST_PM_AMBIGUOUS_OVERLOAD`, which names each candidate with the type that declares it.

### Text and Character Types

| Type | Category | Description |
|------|----------|-------------|
| `String` (`string`) | Class | Immutable text sequence with concatenation (`+`), equality (`==`, `!=`), and instance members `Length`, `Substring`, and `Contains`. |
| `Char` (`char`) | Struct | Single Unicode character with comparison and equality operators. Widens implicitly to numeric types (`UInt16`, `Int32`, `UInt32`, `Int64`, `UInt64`, `Single`, `Double`, `Decimal`). |

### Logic Type

| Type | Category | Description |
|------|----------|-------------|
| `Boolean` (`bool`) | Struct | Logical value supporting `&&` (and), `||` (or), `!` (not), and equality operators. |

### Collection Types

| Type | Category | Description |
|------|----------|-------------|
| `Array<T>` | Class | Generic fixed-size collection with a `Length` property. Created using bracket literal syntax (`[1, 2, 3]`). |
| `List<T>` | Class | Growable collection over .NET's `List<T>`. Its constructor is `pure`, its readers are `local readonly` and its adding and removing members are `local`, as [Standard's Promises](../advanced/purity.md#standards-promises) lists. |

### Pattern and Reflection Types

| Type | Category | Description |
|------|----------|-------------|
| `Regex` | Class | Regular expression compiled from a backtick literal (`` `pattern` ``). Provides `IsMatch` (partial match) and `IsFullMatch` (full string match) methods, a `Pattern` property, and conversions to/from `String`. |
| `Type` | Class | Runtime type descriptor with a `Name` property and a static `Of(unknown value)` method that returns the runtime type of any value. |

## Core Types

The core module provides higher-level abstractions built on top of the bootstrap types.

### Void

| Type | Category | Description |
|------|----------|-------------|
| `Void` | Struct | Unit type with a single instance, obtained by writing the `void` keyword in expression position. Used as a first-class value when a function logically returns nothing but the type system requires a value. Supports equality operators where all instances are equal. |

### Optional

| Type | Category | Description |
|------|----------|-------------|
| `Optional<T>` | Class | Generic container that either holds a value of type `T` or is empty. Provides `HasValue` (bool property indicating presence) and `Value` (the contained value). Constructed with `new Optional<T>(value)` for a present value or `new Optional<T>()` for an empty container. |

### Universal Extensions

The standard library declares extension methods on the `unknown` type, making them available on every value in a Mist program regardless of its type:

| Method | Return Type | Description |
|--------|-------------|-------------|
| `ToString()` | `string` | Returns a string representation of the value. |
| `GetHashCode()` | `int` | Returns a hash code for the value. |
| `GetType()` | `Type` | Returns the runtime `Type` descriptor for the value (equivalent to calling `Type.Of(self)`). |

These methods mirror the universal operations available on all .NET objects, bridged into Mist's type system through the `unknown` top type. A type that declares formatting overloads such as `ToString(string)` or implements `IFormattable.ToString(string, IFormatProvider)` still has the universal zero-argument `ToString()` surface. Its formatting overloads remain callable with their declared arguments, and a user-declared zero-argument implementation keeps its own selection. Universal formatting uses Mist's existing invariant-culture behavior.

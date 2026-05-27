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

All numeric types support the standard arithmetic operators (`+`, `-`, `*`, `/`, `%`) and comparison operators (`<`, `>`, `<=`, `>=`, `==`, `!=`). Signed types additionally support unary negation (`-`).

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
| `Void` | Struct | Unit type with a single instance (`Void.Void`). Used as a first-class value when a function logically returns nothing but the type system requires a value. Supports equality operators where all instances are equal. |

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

These methods mirror the universal operations available on all .NET objects, bridged into Mist's type system through the `unknown` top type.

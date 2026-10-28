# Introduction

> Follow [What's New](updates/index.md) for weekly updates on the language, tooling, and compiler.

Mist is a programming language and idea that has been bouncing around my head since 2015 in different forms. Every single feature of the language arose from pushing the type system of existing languages to their limits. I was programming and I ran into some limitation and wanted something that didn't exist. I am definitely a craftsperson over an academic. Most of the things concepts were things I've been bouncing around my head for a while without concern for the underlying computer science behind it. It is most heavily influenced by C# followed closely by Typescript with a sprinkling of Haskell. While many other language may have the same features it is not derived from them, and then instead more of a coincidence, and consequently deviated without concern.

## Tenets

- **C# Foundation** - When in doubt default to the C# way of doing things. Assume if it's part of C# it has a place in Mist. Anything removed or modified must have a rationale on why it doesn't belong. If something in Mist is semantiaclly the same as C# we should default to the C# syntax. When introducing new concepts we should respect the C# origin as much as possible. A C# developer should be able to self discover how the features work.
- **Comptime Focused** - I strongly believe that the purpose of a programming language is to allow for the programmer to express guarentees that they want the compiler to enforce at compile time so that they do not need to reason about it at runtime. With that in mind all new features should be things that can reasoned about at comptime. We aim to make taking unproven actions comptime errors and couple with a type system to arm the developer to express this properly.
- **Composable** - Language features should be building blocks that interact and compose with other language features. We should be weary of islands of functionality that exist on their own.
- **Conservative Keywords** - Less is more when it comes to keywords. We should be hestitant to introduce new keywords when existing ones can be leveraged. A new keyword should signal something that is a novel addition from something C# can do to help bring attention that there is no parallel in the parent language.

## The Language

Some properties of Mist:

- **Static** - The type of objects cannot change at runtime. Once something is allocated it is that type forever.
- **Strong** - An alias to an object is always typed. You can only perform actions that are comptime provable on the object. The type of the alias must be comptime provable. There is no free casting like C#, there is no any type like Typescript. You can only interact with the information that you've proven is valid at that point in time.
- **Kinds** - Mist supports the follow kinds
  - **Concrete Types** - 
    - **Struct** - Value type. Pass by value. Lives directly at the address behind the alias; could be the stack or within another object. 
    - **Class** - Reference type. Pass by reference. It's alias is a pointer to it's location.
    - **Enum** - A reaonly named list of objects associated to a nominal type. The enum is a type itself and has a nominal parent. This is significantly more powerful than it's C# cousin.
  - **Abstract Types** -
    - **Abstract** - Nominal types that provide a contract an implementer is guaranteed to have met.

Significant Features

- **Ownership** — A tiered ownership system that tracks how values are shared and mutated, catching use-after-move and aliasing errors at compile time.
- **Type Aliases** — Transparent alternative names for existing types (`type Name = ExistingType;`) that the compiler erases during transpilation. Values of the alias and the underlying type are fully interchangeable without casts.
- **Newtypes** — Distinct types cloned from an existing type (`type Name = new SourceType;`). A newtype has its source's members but never its identity: a raw value enters it only through a cast, or as a literal written where the newtype is in sight, so a `UserId` cannot be confused with an `OrderId` or a plain `int`.
- **Type Unions** — First-class union types (`A | B`) that let a value hold one of several types, narrowed safely via pattern matching.
- **Refinement Types** — Types constrained by predicates (e.g., `int where > 0`) that are enforced at compile time and runtime boundaries.
- **Dependent Types** — Types parameterized by a compile-time constant value rather than another type (e.g., `FixedBuffer<Size:int>`), creating a family of distinct types indexed by that value. A `FixedBuffer<5>` and a `FixedBuffer<10>` are incompatible types, giving compile-time guarantees about dimensions and capacities.
- **Null Removed** — Mist eliminates null entirely. Every type must have a well-defined default value (0 for numerics, `""` for strings, a fully-initialized instance for classes and structs). The `default` keyword and optional `default` blocks let types specify their zero-state, removing null-reference errors by construction.
- **Associated Types** — Abstract type slots declared inside abstract types whose concrete definition is provided by implementors. Where generic type parameters are chosen by the caller, associated types are chosen by the implementor — enabling patterns like zero-allocation iteration where the compiler resolves the concrete enumerator struct statically, eliminating boxing and virtual dispatch.
- **Structured Context** — A mechanism for passing values through a call chain without threading them as explicit parameters, using a per-type context stack and `with` clauses. The compiler statically verifies that every required context value is available at each call site, giving the ergonomics of dependency injection with compile-time safety.

## Target Audience

This book is written for my friends that are trying out the language and want to poke at it. It is currently written assuming you have a working knowledge of C# and will not explain how anything in C# work only deviations from the bsae language. Use clankers to supplement your knowledge if there is a C# concept you need a quick explanation of first if you're missing knowledge.

Mist's compiler has a strong separation between its frontend and backend. At the moment it only has a C# transpiler as its backend. Mist gets emitted to C# which is then AOT to native code.

## A Taste of Mist

Here is a small Mist program and the C# code the compiler emits:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../snippets/introduction/greeter.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../snippets/introduction/greeter.cs}}
```
{{#endtab }}
{{#endtabs }}

This small example shows three features working together: a **type alias** (`Percentage`) gives a readable name to `int` and is erased in the output, an **adhoc refinement** (`where score >= 0 and <= 100`) is proven at every call site at compile time, and a **type union** (`string | int`) emits as a discriminated struct that is narrowed via pattern matching. The compiler verifies these stronger guarantees and then emits standard C# that runs anywhere .NET does.

## What This Book Covers

The book is organized into seven parts, each building on the previous:

- **Getting Started** — Install the toolchain, write your first program, and understand project layout.
- **Basics** — Built-in types, variables, namespaces, control flow, methods, arrays, tuples, and regex literals.
- **Object Model** — Classes, inheritance, structs, properties, abstract types, enums, overloading, and extension methods.
- **Type System** — Generics, constraints, type unions, aliases, newtypes, pattern matching, switch expressions, cast operators, refinement types, and dependent types.
- **Advanced Features** — Ownership tiers, ref parameters, readonly contexts, structured context, the unknown type, default values, and nested scoping.
- **Standard Library** — An overview of the modules and types available out of the box.
- **Tooling** — CLI usage, editor support, and project configuration.

# Introduction

Mist is a programming language that transpiles to C#, targeting the .NET runtime. It offers the familiar syntax and ecosystem of C# while introducing stronger compile-time safety guarantees through features like ownership tracking, structural typing, and refinement types. You write Mist, and the compiler emits idiomatic C# that integrates seamlessly with existing .NET projects.

## Design Goals

Mist extends the C# programming model with the following key features:

- **Ownership** — A tiered ownership system that tracks how values are shared and mutated, catching use-after-move and aliasing errors at compile time.
- **Traits** — Structural typing contracts that a type satisfies by having matching members, without requiring an explicit `implements` clause.
- **Type Unions** — First-class union types (`A | B`) that let a value hold one of several types, narrowed safely via pattern matching.
- **Refinement Types** — Types constrained by predicates (e.g., `int where > 0`) that are enforced at compile time and runtime boundaries.
- **Pattern Matching** — Expressive `is` patterns and switch expressions for decomposing values by type, structure, and content.

## Target Audience

This book is written for developers familiar with C# or similar statically-typed languages who want stronger compile-time safety guarantees without leaving the .NET ecosystem. General programming experience is assumed, but no prior Mist knowledge is required.

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

Mist's syntax is intentionally close to C#. The compiler's job is to verify stronger guarantees — ownership, trait conformance, refinement predicates — and then emit standard C# that runs anywhere .NET does.

## What This Book Covers

The book is organized into seven parts, each building on the previous:

- **Getting Started** — Install the toolchain, write your first program, and understand project layout.
- **Basics** — Primitive types, variables, namespaces, control flow, functions, arrays, tuples, and regex literals.
- **Object Model** — Classes, inheritance, structs, properties, interfaces, traits, enums, overloading, and extension methods.
- **Type System** — Generics, constraints, type unions, aliases, newtypes, pattern matching, switch expressions, cast operators, refinement types, and dependent types.
- **Advanced Features** — Ownership tiers, ref parameters, readonly contexts, structured context, the unknown type, default values, and nested scoping.
- **Standard Library** — An overview of the modules and types available out of the box.
- **Tooling** — CLI usage, editor support, and project configuration.

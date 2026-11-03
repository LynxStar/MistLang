# Mist

Mist brings a richer type system to familiar C# syntax: express more of your program's rules in its types, and let the compiler check them. It compiles to C# and runs on .NET.

This book assumes some C# knowledge and focuses on what Mist adds and changes. Start with a small program, then explore the features that make Mist its own language.

**[Install Mist](getting-started/installation.md)** · **[Write your first program](getting-started/hello-world.md)**

## A Slice of Mist

Two small programs explore the same ticketing scenario. First, a desk that checks seat labels, prevents double booking, and closes automatically. Then, a variadic record that turns differently shaped data into seat labels using pure transformations.

### Reserve a Seat

The desk's type carries its number of rows and seats per row. Its nested `Seat` type accepts labels such as `A7`; `Reserve` checks that the seat exists in this venue, that there is room, and that the seat is available. The third reservation below reaches the capacity guard.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../snippets/introduction/ticket-desk.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../snippets/introduction/ticket-desk.cs}}
```
{{#endtab }}
{{#endtabs }}

```text
Mist Meetup: guest 42, seat A1
Mist Meetup: guest 7, seat A2
Sold out
Desk closed with 2 reservations
```

The [newtype](type-system/newtypes.md) keeps user IDs distinct from ordinary integers, while the [generic alias](type-system/type-aliases.md) fixes the request dictionary's key and leaves its value type open. Numeric [refinements](type-system/refinement-types.md) bound the venue to rows A–Z and seats 1–9; the [dependent type](type-system/dependent-types.md) carries the chosen dimensions. The `is` pattern checks each incoming string against the nested regex refinement before it reaches `Reserve`.

[Structured context](advanced/structured-context.md) supplies the event name. The [linear discipline](advanced/drop-semantics.md#linear-types) calls `Drop()` exactly once when the desk leaves scope, reporting its final state.

### Build and Check Seat Labels

One [variadic](type-system/variadics.md) record holds either a row and seat number, or a row, seat number, and offset. `Map` expands those fields into a callback with the matching parameters. The [`pure` type](advanced/purity.md#promises-on-a-type) makes its fields readonly and its methods pure; requiring a pure callback keeps external side effects out of these transformations.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../snippets/introduction/seat-labels.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../snippets/introduction/seat-labels.cs}}
```
{{#endtab }}
{{#endtabs }}

```text
A7
A8
Expected a seat such as A7
Expected a seat such as A7
```

`ReadSeat` accepts [`unknown`](advanced/unknown-type.md): a formatted label, a malformed string, or even an integer. Its [switch](type-system/switch-expressions.md) checks the regex refinement and returns a [union](type-system/type-unions.md) containing either a proven `Seat` or an error string. Both alternatives print directly. Each program starts at its marked [top-level statements](getting-started/top-level-statements.md).

## Get Started

1. **[Install the toolchain](getting-started/installation.md).** Set up the .NET SDK and the Mist compiler.
2. **[Write and run Hello, World!](getting-started/hello-world.md)** Create a `mist.toml` and a source file, then run your project with `mist run`.
3. **[Set up your editor](tooling/editor-support.md).** Add syntax highlighting for Mist files.
4. **[Grow your project](getting-started/project-structure.md).** Organize source files and reference other Mist projects.

The examples throughout this book have an **Emitted C#** tab so you can see how Mist's features translate. The [CLI reference](tooling/cli-usage.md) covers the commands you'll use as you build.

## Explore the Language

Follow the book in order, or start with the part you're curious about:

- **Express more in types:** [newtypes](type-system/newtypes.md) give values a distinct identity, [refinements](type-system/refinement-types.md) constrain their values, and [dependent types](type-system/dependent-types.md) make constants part of a type.
- **Make alternatives explicit:** use [type unions](type-system/type-unions.md), [optional types](type-system/optional-types.md), and [pattern matching](type-system/pattern-matching.md) to describe and handle the values a computation can produce.
- **Control how values are used:** explore [ownership](advanced/ownership.md), [drop semantics](advanced/drop-semantics.md), and [pure and local members](advanced/purity.md).
- **Connect the pieces:** supply dependencies through [structured context](advanced/structured-context.md), let implementors choose [associated types](type-system/associated-types.md), and use existing libraries through [.NET interop](advanced/dotnet-interop.md).

Read the [design tenets](tenets.md) for the thinking behind Mist, or follow [What's New](updates/index.md) for updates on the language, tooling, and compiler.

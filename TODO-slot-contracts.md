# TODO: Slot/Contract System (Long-Term Override Replacement)

## Problem

C# frameworks (EF Core, ASP.NET, etc.) expose their SDK via "derive and override" patterns. Mist intentionally has no `override` or `protected` because:

- Concrete types should be fully static dispatch — you cannot change a type's behavior via inheritance
- There is no visibility difference between a type and its children (Mist's `private` already behaves like C#'s `protected`)

But frameworks declare extension points (abstract/empty virtual methods) that are effectively **slots** — "here's a hole, you fill it."

## Insight

An abstract method / empty virtual is NOT behavioral substitution. It's the same concept as an associated type but at the function level:

- Associated types: "this slot needs a type, you provide it"
- Abstract methods: "this slot needs a function, you provide it"

Both are: **the base declares a hole, the child fills it.**

This does NOT violate LSP because there is no pre-existing behavior to substitute against. The base is saying "call me back here," not "here's behavior you can alter."

## Design Direction

Think of this as a first-class contract/slot mechanism:

```mist
// Conceptual — exact syntax TBD
// The base declares what it needs (a slot)
// The derived type provides what goes there (a concrete implementation)

// This is closer to an associated type than behavioral override:
interface ICompute {
    type Implementer : ICompute;
    func<Implementer, func<int, int>> GetComputeMethod();
}
```

The key properties:
1. The slot is declared upfront and honestly — "this thing is defined elsewhere"
2. Filling a slot is declarative, not behavioral substitution
3. No LSP violation possible — you're providing, not replacing
4. Works naturally with Mist's static dispatch model

## Open Questions

- What syntax makes slots feel natural alongside traits and associated types?
- Should slots be a property of interfaces/traits, or can classes declare them too?
- How does this compose with the existing trait `with` system?
- Can we unify "associated type" and "abstract method slot" under one concept?
- How do we distinguish "you MUST fill this" (abstract) from "you MAY fill this" (virtual with default)?

## Current Workaround (Short-Term)

The C# backend handles this transparently via implicit override matching (see implementation). No Mist syntax changes required for CLR interop. This buys time to design the proper language-level mechanism.

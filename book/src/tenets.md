# Design Tenets

Mist grows out of a desire to express guarantees that existing type systems make difficult or impossible to state. These tenets guide how its features fit together.

## C# Foundation

When in doubt, default to the C# way of doing things. Assume that a feature of C# has a place in Mist; removing or changing it needs a reason. When something has the same semantics as C#, use the same syntax. New concepts should respect that foundation so a C# developer can discover how they work.

## Compile-Time Focused

A programming language should let programmers express guarantees that the compiler enforces, reducing what they need to reason about at runtime. New features should support compile-time reasoning. The aim is to make unproven actions compile-time errors and give developers a type system that can express the proof.

## Composable

Language features should be building blocks that work together. Be wary of islands of functionality that only make sense on their own. A feature becomes more useful when it composes with the rest of the language.

## Conservative Keywords

Less is more when it comes to keywords. Prefer an existing keyword when it can clearly express the idea. A new keyword should draw attention to a concept that has no parallel in C#.

## Where Mist Comes From

Mist has been taking shape in my head since 2015. Its features grew out of programming: pushing existing type systems to their limits and wanting to express something they couldn't. I approach it as a craftsperson more than an academic.

C# is the strongest influence, followed by TypeScript and a sprinkling of Haskell. Many other languages have related features, but Mist's versions grew out of those practical needs and may take different forms.

To see the ideas in code, return to [A Slice of Mist](introduction.md#a-slice-of-mist), or [get started with the toolchain](getting-started/installation.md).

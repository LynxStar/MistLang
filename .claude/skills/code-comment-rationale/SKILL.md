---
name: code-comment-rationale
description: The reasoning behind MistLang's one-comment-per-file budget, with worked examples of each failure mode and guidance on the comment-auditor agent. Read this when a comment rule seems wrong or too strict - not before writing.
---


# Code Comment Policy: Rationale

Reference for `code-comments.md`. Read it when one of those rules seems wrong, or when
deciding a hard case. Not needed before writing.

The rules there are deliberately mechanical and countable, because this document is the
version that did not work. It ran as always-on steering while the tree grew to 23% comment
lines, which is the evidence that a well-argued policy loses to the examples in the code being
edited. Arithmetic binds where argument does not.

That figure is comment lines over non-blank lines in `Source/` and `Test/`, excluding
`bin/`, `obj/` and `Generated/` -- 93,329 of 408,207 when this was written. Over ALL lines it
is 20%, and including `Generated/` changes neither to within a tenth of a point. Stated so the
number can be re-measured rather than taken on faith.

## The default is no comment

Code is expected to speak for itself. A comment is the exception, warranted only where there
is something a reader could not discern quickly from the code in front of them.

The burden runs one way. A comment has to earn its existence; not writing one needs no
justification. Most members carry none, and that includes most public ones -- a bare method
with a clear name and a readable body is the finished state, not an unfinished one.

> A comment earns its place only if it says something that neither the code nor another
> comment already says.

Comments carry the reasoning that code cannot: why this approach and not the obvious
alternative, what constraint from outside the file forces this shape, what breaks if someone
changes it. Anything else is upkeep with no payoff, and it decays into a lie the first time
the code moves underneath it.

## When the failures happen

Each in a predictable moment. Recognising the moment is what makes a rule fire while
writing, rather than during review, which is too late to have saved the effort.

**Having just changed something,** while the reasoning that led there is still fresh and
feels worth preserving. It is not: the diff and the commit message already hold it. This
produces the history failure.

**Writing a new declaration.** The surrounding file has `<summary>` on everything, so a new
member pulls one into existence by style-matching. A summary that *must* exist gets filled
with whatever is true, and what is true is usually the identifier in prose. This is the most
common failure by a wide margin, and the correct output for a new private helper is very
often no doc comment at all.

**Moving or consolidating code.** The urge is to explain the move -- at the new site, at the
old one, or at a call site that restates what the thing it calls already documents.

## Why: no history

The reader is looking at the current code and needs to understand it. A now-fixed bug is not
part of that -- it is a distraction that costs them a paragraph to discover is irrelevant.
History has homes already: the commit message, and the matching Carbonek task's evidence. A
comment is the one place it does not belong.

The tell is past or counterfactual tense: *previously*, *used to*, *no longer*, *without
this*, *had been*, *this now fixes*. A diagnostic ID or a scratch-program repro of a bug that
no longer exists is the same smell in a longer form.

### Forbidden

```csharp
/// Crossing a boundary only ever NARROWS what is admissible, so a type-position request travels
/// through unchanged. That inversion is a real bug rather than a theoretical one --
/// `Paint p = new Paint(7);` inside a static method of a type declaring a static member named
/// `Paint` resolved to the member and reported MIST_PM_UNRESOLVED_TYPE, because the type-only
/// request was widened on its way out of the enclosing type.
```

### Approved

State the rule and its consequence in the present tense. The same information survives; the
story does not.

```csharp
/// Only ever NARROWS. A type-position request travels through unchanged: it admits no member at
/// all, so it has no receiver to lose, and widening it to StaticOnly would let a static member
/// sharing a type's name mask that type from a type position that has to reach past the
/// enclosing type to find it.
```

## Why: no restating

Restatement is worse than absence. It doubles the reading for no information, and it implies
there was something subtle enough to warrant explaining -- so the reader looks for the
subtlety and finds none.

### Forbidden

```csharp
/// <summary>
/// Validates a member's NAME against its enclosing type, independent of member kind.
/// One rule so far: a member cannot share its enclosing type's name.
/// </summary>
/// <param name="memberName">The member's declared name.</param>
/// <param name="locationNode">The declaration node to attach the diagnostic to.</param>
/// <param name="diagnostics">The diagnostic sink.</param>
```

The class is called `MemberNameValidation`, the body is
`if(string.Equals(memberName, enclosingTypeName))`, and the three `<param>` tags echo their
own parameter names.

Other reliable instances of the same mistake:

- A `<summary>` that is the method name with spaces in it.
- `// A Statement subclass with no case above` on a `default:` label.
- `// Only a MemberType is checked` directly above `if(host is MemberType)`.
- `Never null` on a non-nullable declaration.
- `<param>` tags that restate the parameter name for every parameter. Document the one
  parameter with a non-obvious contract and leave the rest bare.

### Approved

Keep the part the code cannot state, and put it where the question occurs to the reader:

```csharp
public static void Validate(string memberName, IMethodHost host, ...)
{
    // An extension block's name is its receiver type's, and its members are emitted into a
    // separate static class, so sharing that name never produces CS0542.
    if(host is MemberType)
```

## Why: one home

Duplicated reasoning is worse than duplicated code, because nothing forces the copies to
agree. One gets updated, the other quietly becomes wrong, and there is no way for a reader to
tell which is which.

This is the hardest failure to catch, because each copy looks correct in isolation. It shows
up when a diagnostic descriptor explains a rule and then the reporting site explains it
again, or when a helper class re-derives the semantics of the method it calls.

### Forbidden

The descriptor documents the rule:

```csharp
/// Reported even though the emitted comment is harmless C#: a comment is legal where a statement
/// was, so an unreported one leaves a program that compiles and runs with a piece of itself missing.
public static readonly DiagnosticDescriptor UnsupportedStatement = ...
```

and the call site says it a second time:

```csharp
case UnsupportedStatement unsupported:
    // A comment is legal where a statement was, so an unreported one leaves a program that
    // compiles and runs with a piece of itself missing.
    diagnostics.ReportAtNode(BackendDiagnostics.UnsupportedStatement, unsupported.Node, null);
```

### Approved

The descriptor keeps the rule. The call site names the descriptor, which is already a
reference:

```csharp
case UnsupportedStatement unsupported:
    diagnostics.ReportAtNode(BackendDiagnostics.UnsupportedStatement, unsupported.Node, null);
```

Where a cross-reference genuinely helps, make it a link rather than a copy:

```csharp
/// Enforces <see cref="ProgramModelDiagnostics.MemberNameMatchesEnclosingType"/>, which documents
/// why the name is refused.
```

### Choosing the home

- A user-facing rule belongs on the diagnostic descriptor, next to the message the user reads.
- A design decision belongs on the type or method that embodies it.
- A local choice ("this node, not the enclosing one") belongs inline at that line.

## Why: no XML escapes

A comment is read in the editor, as source. That is the only form most readers ever see it
in, so source is the form that has to be legible. `///` is XML, so `<` and `>` inside it have
to be written `&lt;` and `&gt;`, which means a code sample containing a generic type arrives
like this:

```csharp
/// A group used as a VALUE does survive -- <c>Func&lt;int, int&gt; f
/// = Add;</c> never invokes anything
```

Nobody can read that. The escaping is most of it; the line wrap through the middle of the
snippet finishes the job.

The escape buys nothing a reader wants. It is there to keep a doc-XML parser happy, and this
project sets no `GenerateDocumentationFile`, so nothing consumes that XML except an IDE hover
on types that are almost all `internal`. The cost lands entirely on the person reading the
file.

The rendered snippet is also, nearly always, decoration. What earns a comment's place is the
mechanism it describes; the sample illustrates it. Removing the sample and stating the
mechanism is shorter *and* readable, which is why it is the first option rather than the last.

### How to comply, in order of preference

1. **Drop the sample and state the mechanism.** Usually the prose already carries it and the
   sample was restating it in a second notation.
2. **Choose a sample that needs no angle brackets.** `Action f = Log;` illustrates a method
   group as a value as well as `Func<int, int>` does. Most illustrations have a bracket-free
   form.
3. **Use `//` instead of `///`.** A non-doc comment is not XML, so the sample sits there
   literally and reads fine. This is the answer when the brackets are load-bearing, and it
   costs nothing on an `internal` member.

For a type *reference* rather than a code sample, `<see cref="..."/>` already takes braces
where C# takes angle brackets, and that is the idiomatic form:
`<see cref="IReadOnlyList{Method}"/>`.

### Forbidden

```csharp
/// A constructed type interpolates its arguments' names, so <c>Buffer&lt;255&gt;</c> over an
/// <c>int</c> and over a <c>byte</c> compare equal here.
```

### Approved

```csharp
/// A constructed type interpolates its argument's name, so the same lifted value over an
/// <c>int</c> and over a <c>byte</c> produces the same name and compares equal here.
```

The rendering went, the mechanism stayed, and the line is shorter than the one it replaced.

## On auditing

`comment-auditor` still exists and still applies this policy from a context that did not write
the code, which is the only place the judgement is reliable. It is expensive: it unitises at
one agent per member, and each delegate greps the tree per comment, so a single file with
twenty members costs twenty dispatches and forty-odd tree sweeps.

So it is on request, not a default. Reach for it on a change that added a lot of comment prose,
or on a file being cleaned up deliberately. Not on every change -- the budget in
`code-comments.md` is the mechanism that is supposed to hold the line, and an audit is what you
do when it did not.

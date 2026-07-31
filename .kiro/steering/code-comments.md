# Code Comment Policy

## The default is no comment

Code is expected to speak for itself. A comment is the exception, warranted only where there is something a reader could not discern quickly from the code in front of them.

The burden therefore runs one way. A comment has to earn its existence; not writing one needs no justification. Most members carry none, and that includes most public ones — a bare method with a clear name and a readable body is the finished state, not an unfinished one.

> A comment earns its place only if it says something that neither the code nor another comment already says.

Comments carry the reasoning that code cannot: why this approach and not the obvious alternative, what constraint from outside the file forces this shape, what breaks if someone changes it. Anything else is upkeep with no payoff, and it decays into a lie the first time the code moves underneath it.

Three failure modes keep recurring. All three pass a casual read, because each one is individually plausible and only looks like noise once you ask what it tells you.

### When they happen

Each in a predictable moment. Recognising the moment is what makes the rule fire while writing, rather than during review, which is too late to have saved the effort.

**Having just changed something,** while the reasoning that led there is still fresh and feels worth preserving. It is not: the diff and the commit message already hold it. This produces Rule 1.

**Writing a new declaration.** The surrounding file has `<summary>` on everything, so a new member pulls one into existence by style-matching. A summary that *must* exist gets filled with whatever is true, and what is true is usually the identifier in prose. This produces Rule 2, and it is the most common failure by a wide margin. The correct output for a new private helper is very often no doc comment at all.

**Moving or consolidating code.** The urge is to explain the move — at the new site, at the old one, or at a call site that restates what the thing it calls already documents. This produces Rule 3.

## Rule 1: Do not narrate the change

A comment describes the code as it stands. It is not a changelog. Do not record what used to happen, what was broken, or what a fix did.

### Why

The reader is looking at the current code and needs to understand it. A now-fixed bug is not part of that -- it is a distraction that costs them a paragraph to discover is irrelevant. History has homes already: the commit message, and the relevant `backlog/*.yaml` register. A comment is the one place it does not belong.

The tell is past or counterfactual tense: *previously*, *used to*, *no longer*, *without this*, *had been*, *this now fixes*. A diagnostic ID or a scratch-program repro of a bug that no longer exists is the same smell in a longer form.

### Forbidden

```csharp
/// Crossing a boundary only ever NARROWS what is admissible, so a type-position request travels
/// through unchanged. That inversion is a real bug rather than a theoretical one --
/// `Paint p = new Paint(7);` inside a static method of a type declaring a static member named
/// `Paint` resolved to the member and reported MIST_PM_UNRESOLVED_TYPE, because the type-only
/// request was widened on its way out of the enclosing type.
```

```csharp
/// Reported at the point of EMISSION rather than binding. Without this, the gap surfaced as a
/// Roslyn syntax error naming a generated .cs file, with no way back to the Mist source.
```

### Approved

State the rule and its consequence in the present tense. The same information survives; the story does not.

```csharp
/// Only ever NARROWS. A type-position request travels through unchanged: it admits no member at
/// all, so it has no receiver to lose, and widening it to StaticOnly would let a static member
/// sharing a type's name mask that type from a type position that has to reach past the
/// enclosing type to find it.
```

```csharp
/// Reported at the point of EMISSION rather than binding, which keeps the error on the Mist
/// source instead of on a generated file the user never wrote.
```

## Rule 2: Do not restate the code

If a reader learns nothing from the comment that the line below it does not already say, delete the comment.

### Why

Restatement is worse than absence. It doubles the reading for no information, and it implies there was something subtle enough to warrant explaining -- so the reader looks for the subtlety and finds none.

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

The class is called `MemberNameValidation`, the body is `if(string.Equals(memberName, enclosingTypeName))`, and the three `<param>` tags echo their own parameter names.

Other reliable instances of the same mistake:

- A `<summary>` that is the method name with spaces in it.
- `// A Statement subclass with no case above` on a `default:` label.
- `// Only a MemberType is checked` directly above `if(host is MemberType)`.
- `Never null` on a non-nullable declaration.
- `<param>` tags that restate the parameter name for every parameter. Document the one parameter with a non-obvious contract and leave the rest bare.

### Approved

Keep the part the code cannot state, and put it where the question occurs to the reader:

```csharp
public static void Validate(string memberName, IMethodHost host, ...)
{
    // An extension block's name is its receiver type's, and its members are emitted into a
    // separate static class, so sharing that name never produces CS0542.
    if(host is MemberType)
```

## Rule 3: Give each piece of reasoning exactly one home

When the same rationale would fit in two places, write it once and reference it from the other.

### Why

Duplicated reasoning is worse than duplicated code, because nothing forces the copies to agree. One gets updated, the other quietly becomes wrong, and there is no way for a reader to tell which is which.

This one is the hardest to catch, because each copy looks correct in isolation. It shows up when a diagnostic descriptor explains a rule and then the reporting site explains it again, or when a helper class re-derives the semantics of the method it calls.

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

The descriptor keeps the rule. The call site names the descriptor, which is already a reference:

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

## What To Keep

The above *is* an argument for fewer comments. It is not an argument against the ones that carry weight. Write those freely when they explain:

- Why an approach was chosen over the obvious alternative.
- A constraint imposed from outside the file -- a C# restriction the emitter must respect, a grammar shape, an ordering the pipeline requires.
- An invariant a reader could break without the compiler noticing.
- Why something is deliberately absent: no diagnostic here, no narrowing here, no validation here.
- A deliberate-looking oddity that would otherwise read as a mistake, such as emitting output known to be invalid.

## Self-Check Before Presenting

Re-read every comment you added and ask, in order:

1. Would deleting it lose anything the code does not say? If not, delete it. Read each `<summary>` against the identifier above it: if it is the name with spaces in it, delete it.
2. Does it describe the past? Delete or rewrite in the present.
3. Does another comment already say it? Keep one, reference it from the other.

The first question is the default, so it comes first. The other two only matter for comments that survive it.

## Applies To

- All comments in `Source/` and `Test/`, both `//` and `///`.
- Comments in `.mist` files, including `Source/Standard/` and `samples/`.
- Commit messages are exempt: narrating the change is exactly their job.
- `backlog/*.yaml` registers are exempt. Their `evidence` fields exist to record what was measured and what was tried.

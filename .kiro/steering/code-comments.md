# Code Comment Policy

## Budget

**A change adds at most one comment per file it touches. Zero is the normal number.**

To add a second comment to the same file, name the first one in your response and say why
both are needed. There is no exemption for public members, subtle code, or long methods.

Only comments you add count. Existing ones do not.

## Rules

1. **No restating.** If the comment is the identifier with spaces in it, or says what the
   line below it says, it does not go in. A bare member is finished, not unfinished.
2. **No history.** Present tense only. Not what used to happen, what was broken, or what a
   fix did. That is what the commit message is for.
3. **One home.** Each piece of reasoning is written once. Point at it with
   `<see cref="..."/>` from anywhere else it would fit.
4. **No XML escapes.** No `&lt;` or `&gt;` in any comment, and no code sample split across
   lines. Drop the sample, pick one without angle brackets, or use `//` instead of `///`.

## What earns the one comment

Only reasoning the code cannot carry:

- Why this approach and not the obvious alternative.
- A constraint imposed from outside the file -- a C# restriction, a grammar shape, an
  ordering the pipeline requires.
- An invariant a reader could break without the compiler noticing.
- Why something is deliberately absent: no diagnostic here, no narrowing here.

Not what the code does, not how it does it, not that it is correct.

## Applies To

- `Source/` and `Test/`, both `//` and `///`.
- `.mist` files, including `Source/Standard/` and `samples/`.
- Exempt: commit messages, and `backlog/*.yaml` registers whose `evidence` fields exist to
  record what was measured.

The reasoning behind these rules, worked examples of each failure, and when to reach for the
`comment-auditor` agent are in `code-comments-rationale.md`. Read it when a rule seems wrong,
not before writing.

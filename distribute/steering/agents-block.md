## Mist

This is a Mist project. Mist compiles to C# and behaves like C# except where its references say otherwise. Before writing or reviewing Mist code, read `.mist/agents/mist-language.md`. Read the others in `.mist/agents/` when a task touches their topic:

| File | Read it when the task involves… |
|------|----------------------------------|
| `mist-ownership.md` | `owned`/`borrowed`/`frozen`/`readonly ref`, move semantics, lending, `IClonable`, or `linear`/`affine`/`relevant` drop semantics |
| `mist-advanced-features.md` | abstract types, default implementations, `with` blocks (retroactive conformance), structured context (`with` clauses), refinement types, dependent types, associated types, extension blocks |
| `mist-interop.md` | adding NuGet packages, framework references, consuming C#/.NET types, named delegate types, delegate-func conversion, `mist inspect`, `mist restore`, or diagnosing dependency resolution |
| `mist-testing.md` | setting up test projects, writing `[Fact]`/`[Theory]` methods, `mist test` workflow, xUnit packages |
| `mist-gitignore.md` | setting up version control — what Mist generates and what to ignore |

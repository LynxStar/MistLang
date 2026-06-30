# Implementation Plan: Compiler Driver Refactoring

## Overview

Prep work for the v2 pipeline (ProgramModel + new emitter). Extract stages into focused methods, move misplaced concerns to their correct homes, and create the insertion point for the new pipeline.

## Tasks

- [ ] 1. Extract Stage 1 (Loading) into its own method
  Extract Steps 1a–1d (user source loading, stdlib loading, project reference resolution, combining units) into a method like `LoadCompilationUnits(...)` that returns a `MistCompilation`.

- [ ] 2. Extract Stage 1.5 (Package Resolution) into its own method
  Extract NuGet package and framework reference resolution into `ResolvePackages(...)` that returns the resolved assembly paths and `LoadedAssemblyInfo`.

- [ ] 3. Extract Stage 2 (Lexing) into its own method
  Extract tokenization loop into `Lex(...)` that takes `MistCompilation` and returns the token arrays.

- [ ] 4. Extract Stage 3 (Parsing) into its own method
  Extract parsing loop into `Parse(...)` that takes token arrays and returns ASTs plus metrics.

- [ ] 5. Extract Stage 4 (Semantic Model) into its own method
  Extract semantic model building + inference engines (4b, 4c.5, 4d) into `BuildSemanticModel(...)` that takes ASTs and returns `ISemanticModel`.

- [ ] 6. Extract Stage 5 (Analysis) into its own method
  Extract analyzer runner invocation into `RunAnalysis(...)` that takes `ISemanticModel` and returns diagnostics.

- [ ] 7. Extract Stage 6 (Emit) into its own method
  Extract backend emit + csproj patching into `Emit(...)` that takes `ISemanticModel` and options, returns `BackendResult`.

- [ ] 8. Move `PatchCsprojWithReferences` to the C# emitter backend
  This is a post-emit fixup on the generated .csproj -- it belongs in the emitter, not the driver. The emitter's `Emit` method should handle package/framework reference injection internally.

- [ ] 9. Move `PatchCsprojWithProperties` to the C# emitter backend
  Same rationale -- MSBuild property patching is an emitter concern. Move it into the backend so the driver just calls `backend.Emit(...)` and gets a complete result.

- [ ] 10. Add insertion point for v2 pipeline
  After Stage 3, add a conditional branch where the new pipeline (ProgramModelBuilder -> new analyzers -> new emitter) can be invoked. Initially gated behind a flag or config option. Both pipelines receive the same ASTs from Stage 3.

## Task Dependency Graph

```json
{
  "waves": [
    [1],
    [2],
    [3],
    [4],
    [5],
    [6],
    [7],
    [8, 9],
    [10]
  ]
}
```

## Notes

- Tasks 1-7 are pure mechanical extractions with no behavior change. The `Compile()` method becomes a thin orchestrator.
- Tasks 8-9 are independent of each other but depend on task 7 (the emit stage needs to be its own method before we move things into the backend).
- Task 10 depends on the stages being separated so the fork point after Stage 3 is clean.

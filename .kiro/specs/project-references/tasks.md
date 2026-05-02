# Implementation Plan: Project References

## Overview

Add multi-project support to the Mist compiler. A project declares dependencies via a `[[references]]` array in `mist.toml`. The compiler resolves each reference, recompiles the referenced project's sources from scratch, and makes its public symbols available to the referencing project's semantic model. Transitive references are compiled but not directly visible unless explicitly declared.

## Tasks

- [x] 1. Add `ProjectReference` record and update `ProjectConfiguration`
  - Create `Source/Mist/Driver/Contracts/ProjectReference.cs` with the `ProjectReference(string Path)` sealed record
  - Update `Source/Mist/Driver/Contracts/ProjectConfiguration.cs` to add a third parameter `ImmutableArray<ProjectReference> References` (defaulting to `ImmutableArray<ProjectReference>.Empty` at existing call sites)
  - Update all existing call sites that construct `ProjectConfiguration` to pass the empty array explicitly
  - _Requirements: 1.6, 2.1–2.5_

- [x] 2. Add `OriginProjectPath` property to `CompilationUnit`
  - Add `public string? OriginProjectPath { get; init; }` to `Source/Mist/Driver/Contracts/CompilationUnit.cs`
  - Property is `null` for stdlib units and the top-level user project's own units; set to the canonical `mist.toml` path for referenced project units
  - _Requirements: 6.1, 6.2, 6.4_

- [x] 3. Add project-reference diagnostic IDs to `DiagnosticId.cs`
  - Add the five new `DiagnosticId` constants to `Source/Mist/Common/Diagnostics/DiagnosticId.cs`:
    - `ProjectReferenceMissingPath` → `"MIST_PROJECT_REFERENCE_MISSING_PATH"`
    - `ProjectReferenceEmptyPath` → `"MIST_PROJECT_REFERENCE_EMPTY_PATH"`
    - `ProjectReferenceNotFound` → `"MIST_PROJECT_REFERENCE_NOT_FOUND"`
    - `ProjectReferenceNoProjectFile` → `"MIST_PROJECT_REFERENCE_NO_PROJECT_FILE"`
    - `ProjectCircularReference` → `"MIST_PROJECT_CIRCULAR_REFERENCE"`
  - _Requirements: 1.3, 1.4, 2.6, 2.7, 2.8, 4.1, 7.1–7.3_

- [x] 4. Update `ProjectFileParser.Parse` to read `[[references]]`
  - After the existing `namespace_root` parsing (step 8), add step 9 to read the optional `[[references]]` TOML array-of-tables
  - If the key is absent → `References = ImmutableArray.Empty` (no error)
  - If present but not a `TomlTableArray` → report `MIST_PROJECT_TOML_ERROR`, return `null`
  - For each entry: check `path` key present and is a string (else `MIST_PROJECT_REFERENCE_MISSING_PATH`, return `null`); check non-empty/non-whitespace (else `MIST_PROJECT_REFERENCE_EMPTY_PATH`, return `null`)
  - Normalize path: relative → `Path.GetFullPath(Path.Combine(projectFolder, rawPath))`; absolute → `Path.GetFullPath(rawPath)`; if normalized path is a directory → append `"mist.toml"`
  - Do NOT check existence here — deferred to resolver
  - Return `ProjectConfiguration(name, namespaceRoot, references.ToImmutable())`
  - _Requirements: 1.1–1.6, 2.1–2.5, 7.3, 8.1–8.3_

- [x] 5. Unit tests for `ProjectFileParser` reference parsing
  - [x] 5.1 Add reference-parsing unit tests to `Test/Mist.Tests/Backend/ProjectFileParserTests.cs`
    - Missing `path` field → `MIST_PROJECT_REFERENCE_MISSING_PATH`
    - Empty string `path` → `MIST_PROJECT_REFERENCE_EMPTY_PATH`
    - No `[[references]]` section → `References` is empty, parse succeeds
    - `[[references]]` with a direct `mist.toml` path → path stored as-is (after canonicalization)
    - `[[references]]` with a directory path → `mist.toml` appended
    - Absolute path in `path` field → used directly after canonicalization
    - Relative path with `..` segments → canonicalized correctly
    - _Requirements: 1.1–1.5, 2.1–2.5_

  - [x] 5.2 Write property test: round-trip fidelity (Property 1)
    - **Property 1: mist.toml references round-trip**
    - Generator: random list of 1–5 valid path strings (valid name characters); write `mist.toml` with those paths as `[[references]]` entries; parse with `ProjectFileParser.Parse`; assert `result.References` paths match input paths after normalization
    - No process spawning — pure file I/O + in-process parsing
    - Add to `Test/Mist.Tests/Backend/ProjectFileParserPropertyTests.cs`
    - **Validates: Requirements 1.1, 1.6, 8.1, 8.2**

  - [x] 5.3 Write property test: whitespace path rejection (Property 2)
    - **Property 2: Empty/whitespace reference paths are rejected**
    - Generator: strings of 1–64 whitespace characters; write `mist.toml` with that string as a `path` value; assert `MIST_PROJECT_REFERENCE_EMPTY_PATH` is reported and result is `null`
    - No process spawning
    - Add to `Test/Mist.Tests/Backend/ProjectFileParserPropertyTests.cs`
    - **Validates: Requirements 1.4, 7.3**

  - [x] 5.4 Write property test: unknown fields ignored (Property 3)
    - **Property 3: Unknown fields in [[references]] entries are ignored**
    - Generator: valid path string + random set of unknown key-value pairs; write `mist.toml` with the entry including unknown fields; assert parse succeeds and path is preserved
    - No process spawning
    - Add to `Test/Mist.Tests/Backend/ProjectFileParserPropertyTests.cs`
    - **Validates: Requirements 8.3**

- [x] 6. Create `ResolvedProjectReference` record and `ProjectReferenceResolver` class
  - Create `Source/Mist/Driver/Contracts/ResolvedProjectReference.cs` with the `ResolvedProjectReference(string CanonicalTomlPath, string ProjectFolder, ProjectConfiguration Configuration, ImmutableArray<string> SourcePaths)` sealed record
  - Create `Source/Mist/Driver/ProjectReferenceResolver.cs` with a static `Resolve(string rootProjectFolder, ImmutableArray<ProjectReference> references, DiagnosticBag diagnostics)` method returning `ImmutableArray<ResolvedProjectReference>`
  - Implement DFS with `visited` (fully processed) and `inProgress` (on DFS stack) `HashSet<string>` for cycle detection and deduplication
  - For each node: check existence (`File.Exists`), check filename is `mist.toml` (else `MIST_PROJECT_REFERENCE_NOT_FOUND`), check directory without `mist.toml` (else `MIST_PROJECT_REFERENCE_NO_PROJECT_FILE`)
  - Cycle detection: if path is in `inProgress`, report `MIST_PROJECT_CIRCULAR_REFERENCE` with full chain string (e.g. `A → B → A`), return for that branch
  - Post-order DFS: recurse into transitive references before adding current project to result (deepest-dependency-first)
  - Collect source paths via `Directory.GetFiles(projectFolder, "*.mist", SearchOption.AllDirectories)` sorted by `StringComparer.Ordinal`
  - Continue processing independent branches after errors (accumulate all diagnostics)
  - _Requirements: 2.6–2.8, 3.1–3.3, 4.1–4.3, 5.4–5.5, 7.1, 7.2, 7.4_

- [x] 7. Unit tests for `ProjectReferenceResolver`
  - [x] 7.1 Create `Test/Mist.Tests/Driver/ProjectReferenceResolverTests.cs` with example-based unit tests
    - Non-existent path → `MIST_PROJECT_REFERENCE_NOT_FOUND`
    - Directory without `mist.toml` → `MIST_PROJECT_REFERENCE_NO_PROJECT_FILE`
    - File not named `mist.toml` → `MIST_PROJECT_REFERENCE_NOT_FOUND`
    - Invalid referenced `mist.toml` → error propagated from inner parse
    - Diamond dependency (A→B, A→C, B→D, C→D) → each project appears exactly once in result
    - Direct self-reference (A → A) → `MIST_PROJECT_CIRCULAR_REFERENCE`
    - Two-node cycle (A → B → A) → `MIST_PROJECT_CIRCULAR_REFERENCE` with both nodes in message
    - Multiple independent invalid references → all errors reported (not just first)
    - _Requirements: 2.6–2.8, 3.2, 3.3, 4.1–4.3, 7.1, 7.2, 7.4_

  - [x] 7.2 Write property test: cycle detection for cycles of any length (Property 4)
    - **Property 4: Cycle detection for cycles of any length**
    - Generator: cycle length N from 1 to 5; create N temp directories each with a `mist.toml` referencing the next, with the last referencing the first; call `ProjectReferenceResolver.Resolve` in-process
    - Assert `MIST_PROJECT_CIRCULAR_REFERENCE` is reported and message contains all node paths in the cycle chain
    - No process spawning — resolver is pure in-process logic
    - Create `Test/Mist.Tests/Driver/ProjectReferencePropertyTests.cs`
    - **Validates: Requirements 4.1, 4.2, 4.3, 7.2**

  - [x] 7.3 Write property test: deduplication (Property 5)
    - **Property 5: Deduplication — each project parsed exactly once**
    - Generator: diamond graph with 1–3 shared dependencies; call `ProjectReferenceResolver.Resolve` in-process; assert each project's canonical path appears exactly once in the result
    - No process spawning
    - Add to `Test/Mist.Tests/Driver/ProjectReferencePropertyTests.cs`
    - **Validates: Requirements 3.3**

  - [x] 7.4 Write property test: all independent errors reported (Property 9)
    - **Property 9: All independent reference errors are reported**
    - Generator: N (2–5) independent non-existent reference paths; call `ProjectReferenceResolver.Resolve` in-process; assert exactly N diagnostics are reported
    - No process spawning
    - Add to `Test/Mist.Tests/Driver/ProjectReferencePropertyTests.cs`
    - **Validates: Requirements 7.4**

- [x] 8. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Update `CompilerDriver.Compile` to resolve and inject reference units
  - After loading stdlib units (Step 1b) and before combining with user units, add Step 1c:
    - Parse the root project's `mist.toml` via `ProjectFileParser.Parse(options.ProjectFolder, refBag)`
    - If `projectConfig` is not null and `References` is non-empty, call `ProjectReferenceResolver.Resolve`
    - For each `ResolvedProjectReference`, read each source path and create a `CompilationUnit` with `NamespaceRoot`, `ProjectFolder`, and `OriginProjectPath = proj.CanonicalTomlPath`
    - If `refBag.HasErrors`, return early with failure result
  - Update Step 1d to combine: `stdlib → refUnits → userUnits`
  - Guard the entire reference resolution block with `!string.IsNullOrEmpty(options.ProjectFolder)`
  - _Requirements: 3.1, 3.2, 5.1–5.5_

- [x] 10. Integration tests for `CompilerDriver` with references
  - [x] 10.1 Create `Test/Mist.Tests/Driver/CompilerDriverReferenceIntegrationTests.cs`
    - Single reference: A references B; B's types are visible in A (compilation succeeds, no undefined-type diagnostics for B's types)
    - Invalid reference in A → compilation aborts with correct diagnostic (`MIST_PROJECT_REFERENCE_NOT_FOUND`)
    - Ordering: units from a referenced project C appear before units from B (which references C) in `compilation.Units`
    - _Requirements: 3.1, 3.2, 5.1–5.5_

  - [x] 10.2 Write property test: referenced unit metadata is correct (Property 6)
    - **Property 6: Referenced unit metadata is correct**
    - Generator: referenced project with random valid name/namespace root and 1–3 source files; resolve and load units via `CompilerDriver` in-process (shared static `CompilerDriver` instance); assert all units from the referenced project have correct `NamespaceRoot` and `ProjectFolder`
    - No process spawning — use `CompilerDriver.Compile()` directly
    - Add to `Test/Mist.Tests/Driver/ProjectReferencePropertyTests.cs`
    - **Validates: Requirements 5.2, 5.3**

  - [x] 10.3 Write property test: topological ordering (Property 7)
    - **Property 7: Topological ordering — deepest dependency first**
    - Generator: reference chain of depth 2–4 (A→B→C→...); compile in-process via shared `CompilerDriver.Compile`; assert units from deeper projects appear before units from shallower projects in `compilation.Units`
    - No process spawning
    - Add to `Test/Mist.Tests/Driver/ProjectReferencePropertyTests.cs`
    - **Validates: Requirements 5.4, 5.5**

- [x] 11. Update semantic model to enforce transitive visibility
  - Add the set of directly-referenced canonical paths to the semantic model build context (either via a new parameter on `SemanticModelBuilder.Build` or via `CompilerOptions`)
  - In `SemanticModelBuilder`, when registering symbols from a `CompilationUnit` whose `OriginProjectPath` is not null and not in the direct-reference set, register those symbols in a "transitive-only" scope that is excluded from name resolution for the user project
  - A unit is directly visible if `OriginProjectPath` is null (stdlib or user's own code) OR `OriginProjectPath` is in the set of direct reference canonical paths
  - _Requirements: 6.1, 6.2, 6.4_

- [x] 12. Integration tests for transitive visibility
  - [x] 12.1 Add transitive-visibility tests to `Test/Mist.Tests/Driver/CompilerDriverReferenceIntegrationTests.cs`
    - Transitive reference: A→B→C; C's types visible in B, not in A (attempting to use C's type from A produces a diagnostic)
    - Explicit transitive: A→B, A→C, B→C; C's types visible in both A and B
    - _Requirements: 6.1, 6.2, 6.4_

  - [x] 12.2 Write property test: transitive types are not directly visible (Property 8)
    - **Property 8: Transitive types are not directly visible**
    - Generator: A→B→C where C declares a type with a random valid name; compile A in-process via shared `CompilerDriver.Compile`; attempt to resolve C's type from A's semantic model; assert the type is not found (or a diagnostic is reported)
    - No process spawning — use `CompilerDriver.Compile()` directly
    - Add to `Test/Mist.Tests/Driver/ProjectReferencePropertyTests.cs`
    - **Validates: Requirements 6.2, 6.4**

- [x] 13. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- All property-based tests use in-process compilation via `CompilerDriver.Compile()` or direct `ProjectFileParser`/`ProjectReferenceResolver` API calls — no `Process.Start` or `dotnet run` per iteration
- A shared static `CompilerDriver` instance MUST be reused across all iterations within a property test class (stateless, safe to reuse)
- Each property test is tagged with `// Feature: project-references, Property N: <property text>` per project convention
- Requirement 6.3 (diagnostic when A uses a type from C via B's public API) is noted in the design as a follow-up; Task 11 implements the simpler form (types from C's source files are not directly resolvable by A)
- Checkpoints ensure incremental validation before moving to the next phase

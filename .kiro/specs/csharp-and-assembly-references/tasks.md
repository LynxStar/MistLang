# Implementation Plan: C# and Assembly References

## Overview

Add `CSharp_Project` and `Assembly` reference kinds to `[references]`, decided from the path; build
referenced C# projects before the frontend; emit a type-forwarding facade for every composed Mist project a
referenced C# assembly was compiled against. Each task leaves the solution building with zero warnings and
`dotnet format --verify-no-changes` clean. Tiers follow the root `AGENTS.md` "Tests" table; a test's tier is
declared with `[Trait("Tier", "N")]`.

Carbonek project MIST is the work record; this plan is the delivery order, not a queue.

## Tasks

- [x] 1. Reference kinds and classification
  - [x] 1.1 Add `ReferenceKind` and the `Classification` records under `Source/Mist/Driver/Contracts/`
    - `MistProject`, `CSharpProject`, `Assembly`; `Classified`, `NoCandidate`, `Ambiguous`, `UnsupportedExtension`
    - _Requirements: 1.7_
  - [x] 1.2 Add `ReferenceClassifier.Classify(path, isDirectory, directoryFileNames)` in `Source/Mist/Driver/`
    - Pure over strings; extension comparison case-insensitive; candidates sorted ordinally
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.6, 2.1, 2.2, 2.3, 2.5, 2.7_
  - [x] 1.3 Tier 1 `ReferenceClassifierTests`
    - Each single-candidate kind, no candidate, two of one kind, one each of two kinds, a listing that would include a subdirectory name and must ignore it, `.CSPROJ` casing, a `.txt` direct path
    - _Requirements: 11.1_
  - [x] 1.4 Extend `ProjectReference` with `Kind`; fix construction sites in parser and tests
    - _Requirements: 1.7_

- [x] 2. Manifest parsing and resolution
  - [x] 2.1 `ProjectFileParser` step 9: classify file paths; report `MIST_REFERENCE_UNSUPPORTED_PATH`; leave directory paths for the resolver
    - Add the descriptor to `DriverDiagnostics`
    - _Requirements: 1.4, 10.1, 10.3_
  - [x] 2.2 `ProjectReferenceResolver`: list a directory at depth one and classify; dispatch by kind; return external assemblies beside Mist references
    - Reword `MIST_PROJECT_REFERENCE_NO_PROJECT_FILE`; add `MIST_REFERENCE_FILE_NOT_FOUND` and `MIST_REFERENCE_AMBIGUOUS`
    - Key check for `Assembly` immediately; for `CSharpProject` deferred to task 3
    - _Requirements: 1.5, 2.4, 2.5, 2.6, 3.2, 3.4, 10.1, 10.2_
  - [x] 2.3 Tier 3 additions to `ProjectReferenceResolverTests`: one, none, two candidates in a temp directory; assembly key mismatch
    - _Requirements: 2.3, 2.4, 2.5, 3.2_
  - [x] 2.4 Update `ProjectConfigurationRoundTripPropertyTests` so the serializer emits a `Kind`-bearing reference and round-trips it
    - _Requirements: 1.7_

- [x] 3. Building referenced C# projects
  - [x] 3.1 Add `BuildChain` (read, extend, contains) and the `MIST_REFERENCE_CYCLE` descriptor; check the chain at the top of `CompilerDriver.Compile`
    - _Requirements: 4.6, 4.7, 10.1_
  - [x] 3.2 Tier 1 `BuildChainTests`
    - _Requirements: 11.4_
  - [x] 3.3 Add `ExternalAssembly` and `CSharpProjectBuilder` modelled on `DotnetRestoreProcess`: `dotnet build -c Debug -f <tfm> --nologo -v:quiet`, then `-getProperty:TargetPath`; closure and runtime config discovery; 600 s timeout; `MIST_CSHARP_PROJECT_BUILD_FAILED`
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.8_
  - [x] 3.4 Wire the builder into `CompilerDriver.Compile` before `ResolvePackages`: deepest manifest first, declaration order, once per project; complete the `CSharpProject` key check against the built name
    - Populate `CompilerOptions.AssemblyPaths` and a new `ExternalAssemblies` option; carry them in `PipelineInput`
    - _Requirements: 3.3, 4.5, 6.1_
  - [x] 3.5 Tier 4 `CompilerDriverExternalAssemblyTests` with a fixture assembly under `Test/Fixtures/` and no MSBuild
    - _Requirements: 11.6_

- [x] 4. Frontend
  - [x] 4.1 `AmbientTypeProviderV2Factory.Create`: accept resolver-only directories; skip registration of any assembly whose simple name is a composed project's name
    - _Requirements: 6.2, 6.4, 6.5_
  - [x] 4.2 Tier 2 `ExternalAssemblyShadowingTests` over a stub provider: skipped registration; a signature naming a declared type binds to it
    - _Requirements: 6.3, 6.4, 11.5_

- [x] 5. Facades
  - [x] 5.1 `AssemblyReferenceScan` over `PEReader`: referenced simple names; assembly version
    - _Requirements: 7.1, 8.2_
  - [x] 5.2 Tier 1 `AssemblyReferenceScanTests` with `MetadataBuilder` images
    - _Requirements: 11.3_
  - [x] 5.3 `FacadeEmitter`: type selection from the program model; sorted `TypeForwardedTo` source; deterministic library compilation named after the composed project with the primary output's version
    - _Requirements: 7.2, 7.3, 7.7, 7.8_
  - [x] 5.4 Tier 1 `FacadeTypeSelectionTests`
    - _Requirements: 11.2_
  - [x] 5.5 `CSharpCompilationDriver.Compile`: scan, exclude shadowed paths, root-name cycle, emit facades, reference them; add `MIST_REFERENCE_CYCLE` at the backend stage in `BackendDiagnostics`
    - _Requirements: 7.4, 7.5, 7.6, 10.1, 10.3_

- [x] 6. Deployment
  - [x] 6.1 Copy external assemblies and closures; version conflict check; write facades; framework check against `framework_references`
    - Add `MIST_REFERENCE_ASSEMBLY_CONFLICT` and `MIST_REFERENCE_FRAMEWORK_MISSING`
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 10.1_
  - [x] 6.2 Tier 1 `AssemblyConflictRuleTests` over `(name, version)` pairs
    - _Requirements: 11.3_

- [x] 7. CLI
  - [x] 7.1 `restore`: `dotnet restore` per C# reference and a kind line per reference; `inspect`: build first; Stage 1 detail line
    - _Requirements: 9.1, 9.2, 9.3, 9.4_

- [x] 8. Samples and system test
  - [x] 8.1 Add `samples/CSharpReferences`: `Lib/` (Mist library), `Bridge/` (C# project depending on `Lib` through a targets file, no packages), `App/` (Mist executable referencing both); README
    - The directory has no top-level `.mist` files, so the memoization corpus test collects nothing from it
    - _Requirements: 11.7_
  - [x] 8.2 Tier 5 `CSharpReferences_RunsFromACleanTree` in `SamplesCompileAndMatchBaselineTest` style, through the prebuilt CLI with `--no-build`; assert output and the facade in `bin/`
    - _Requirements: 11.7_
  - [x] 8.3 Update `samples/EfCoreMigrations`: `App/mist.toml` references `App.Migrations`; `Main` calls `Migrate()`; README drops the "cannot" paragraph and keeps `run.sh` for the native library
    - _Requirements: 11.8_

- [x] 9. Book and close-out
  - [x] 9.1 Project Configuration chapter: the three kinds, the bare-directory rule, the key rule, the facade in one paragraph
  - [ ] 9.2 Completion report per the Carbonek contract: acceptance probes before and after, tests by tier, ledger run, build and format results

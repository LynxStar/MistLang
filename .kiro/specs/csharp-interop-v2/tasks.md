# Implementation Plan

## Overview

This plan closes the six CLR/C# interop gaps in dependency order. Tasks 2–4 build the project model and resolution primitives; task 5 fixes filtering/ref-structs; task 6 fixes cross-assembly inheritance; task 7 wires resolution into the pipeline; task 8 adds the CLI commands; tasks 9–10 deliver the sample and docs. Every area is independently testable and the pipeline stays green between tasks. All tests are in-process except task 9.2, the approved single-spawn sample integration test.

## Tasks

- [x] 1. Investigation: pin the per-overload "all hidden" root cause
  - Reproduce the user's report (an overload group where one overload uses an unsupported type) against the current `VisibilityFilter`/`TypeProjector`/overload-resolution path; determine whether the cascade is at projection, `GetMembers`, or overload resolution.
  - Confirm no Mist-declared type or emitter path depends on `StructType.IsRefStruct` for projected CLR types (so it can be removed in task 5).
  - Record findings in the design's Design Decisions section; adjust tasks 5/7 if the root cause differs from the assumed projection-layer location.
  - _Requirements: 8.1, 9.1_

- [x] 2. Project model: framework references and target framework
  - [x] 2.1 Add `FrameworkReference` record and extend `ProjectConfiguration` with `FrameworkReferences` and `TargetFramework` (default `net10.0`).
    - _Requirements: 4.1, 4.2, 4.3_
  - [x] 2.2 Parse `[framework_references]` (and optional `target_framework`) in `ProjectFileParser`, with `MIST_FWREF_TABLE_EXPECTED`, `MIST_FWREF_INVALID_VALUE`, `MIST_FWREF_INVALID_NAME` diagnostics; trim names, enforce 1–256 chars, collect-and-continue on entry errors.
    - _Requirements: 4.1, 4.4, 4.5, 4.6, 4.7, 4.8_
  - [x] 2.3 In-process tests: true/false entries, absent table, non-table value, non-boolean value, empty/whitespace name, >256-char name, duplicate-`true` dedupe.
    - _Requirements: 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8_

- [x] 3. Package resolution: structured result and stable diagnostics
  - [x] 3.1 Change `PackageResolver.ResolvePackages` to return `PackageResolutionResult` (`AssemblyPaths`, `Resolved` infos with name/version/TFM/assembly-count, `FailureCount`); record zero-assembly packages as `AssemblyCount == 0`.
    - _Requirements: 1.7, 2.1, 2.4_
  - [x] 3.2 Standardize resolver error diagnostics on `MIST_PKG_001` (no compatible TFM, lists offered TFMs), `MIST_PKG_002` (not found, names every feed), `MIST_PKG_003` (connection/network failure or restore timeout at 100s, indicates category); exactly one per failure; continue on failure with one diagnostic per failing package.
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.6_
  - [x] 3.3 In-process tests for the structured result shape and each diagnostic category (using invalid versions / unknown names / cached packages; no new network-heavy property tests).
    - _Requirements: 3.1, 3.2, 3.6_

- [x] 4. Framework reference resolution
  - [x] 4.1 Add `FrameworkReferenceResolver` returning `FrameworkResolutionResult`; locate `{dotnetRoot}/packs/{Name}.Ref/{version}/ref/{tfm}/` via `SdkDiscovery`'s root discovery; success requires the dir to exist with ≥1 ref assembly; select highest pack whose major matches the TFM major (integer major→minor→patch).
    - _Requirements: 5.1, 5.6_
  - [x] 4.2 Emit `MIST_FWREF_001` (known but not installed; names framework/TFM/searched path) and `MIST_FWREF_002` (unknown name, via known-framework allow-list); resolve each framework independently with one diagnostic per failure.
    - _Requirements: 5.4, 5.5, 5.7_
  - [x] 4.3 In-process tests: resolution against the locally-installed `Microsoft.AspNetCore.App` when present (else assert the not-installed path), version selection, `MIST_FWREF_001`, `MIST_FWREF_002`.
    - _Requirements: 5.1, 5.4, 5.5, 5.6_

- [x] 5. Ref-struct exclusion and per-overload filtering
  - [x] 5.1 Add ref-struct (`IsByRefLikeAttribute`) exclusion to `VisibilityFilter.IsTypeExcluded`; verify `ReferencesExcludedType`/`IsTypeExcludedInSignature` covers all positions incl. nested generics; ensure exclusion is per-`MemberInfo` so sibling overloads survive.
    - _Requirements: 8.1, 9.1, 9.2, 9.3_
  - [x] 5.2 Stop projecting ref structs: `AssemblyTypeProvider.ResolveType` returns null for `IsByRefLike` types; remove the `StructType.IsRefStruct` stack-only projection path for CLR types (guided by task 1's findings).
    - _Requirements: 9.4, 9.5_
  - [x] 5.3 Add `VisibilityFilter.Classify(MemberInfo) → MemberClassification` (`MemberHiddenReason` + offending type names) for the inspect command; keep `IsMemberExcluded` boolean for the build path.
    - _Requirements: 9.4, 10.5_
  - [x] 5.4 In-process tests (extend `Test/Mist.Tests.InteropFixtures` with a mixed Span/non-Span overload fixture): Span sibling hidden + `byte[]` sibling kept; `Encoding.GetBytes(string)`, `Socket.Send(byte[])`, `Console.ReadLine()` retained; all-excluded group → no symbol; `Classify` reasons + offending names; no ref-struct type projected.
    - _Requirements: 8.2, 8.3, 8.4, 8.5, 8.6, 9.1, 9.4_

- [x] 6. Cross-assembly inheritance and inherited members
  - [x] 6.1 Inject a type-reference resolver delegate (CLR `Type` → cached projected `IType`) from `AssemblyTypeProvider` into `TypeProjector`; route `ProjectTypeReference`/`ProjectSimpleTypeReference` through it so base types and interfaces resolve to the cached, fully-projected instances.
    - _Requirements: 6.1, 6.2, 6.3_
  - [x] 6.2 Connect `ClassType.BaseType`/`StructType` base and interface lists to the resolved cached types (dedupe interfaces); ensure cache-mediated recursion safety for cyclic/in-progress references.
    - _Requirements: 6.2, 6.3_
  - [x] 6.3 Handle unresolvable relationships: omit only that relationship, record a diagnostic indication (relationship + unresolved type name), continue projecting the type and remaining members/relationships.
    - _Requirements: 6.6_
  - [x] 6.4 Inherited-member projection: ensure most-derived visible signature is exposed for overrides/shadows; fall back to nearest visible ancestor when the most-derived definition is hidden; verify flattened reflection enumeration + connected base chain make inherited members callable.
    - _Requirements: 7.1, 7.3, 7.4, 7.5, 7.8_
  - [x] 6.5 In-process tests against genuinely cross-assembly SDK types: `NetworkStream : Stream` and `IPEndPoint : EndPoint` assignability + both-direction casts; inherited `Read`/`Write`/`Length`/`CanRead` callable; hidden inherited member not invocable; unresolved-relationship fallback.
    - _Requirements: 6.1, 6.4, 6.5, 7.1, 7.7_

- [ ] 7. Pipeline wiring and the package-resolution stage
  - [x] 7.1 Change `ICompilationProgressReporter` stage identifier `int → string`; update `ConsoleProgressReporter`, `NullProgressReporter`, and all `CompilerDriver` call sites (stages `"0".."6"`). Build with zero warnings.
    - _Requirements: 1.4_
  - [x] 7.2 Extend `CompilerOptions` with `Packages`, `FrameworkReferences`, `TargetFramework`, `VerboseResolution`.
    - _Requirements: 2.4, 2.5_
  - [x] 7.3 Add the Package Resolution stage (`"1.5"`) to `CompilerDriver.Compile` between Loading and Lexing: run only when deps are declared; call `PackageResolver` + `FrameworkReferenceResolver`; merge assembly paths; add diagnostics to the shared bag; emit stage header/detail/summary/elapsed-ms only when `VerboseResolution`; never emit header/detail on quiet success; continue the build on resolution failure.
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2.6, 5.2, 5.3, 5.8_
  - [x] 7.4 Change `IBackend.GetAmbientTypeResolver()` → `(string targetFramework, ImmutableArray<string> additionalAssemblyPaths)`; in `CSharpTranspilerBackend`, discover SDK refs for the TFM, union + dedupe with additional paths, key the provider cache on the merged set; pass resolved paths from the driver's Semantic stage.
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 5.2, 5.3_
  - [x] 7.5 Wire CLI `CompileProject` to map `config.Packages`/`FrameworkReferences`/`TargetFramework`/`cliArgs.Verbose` into `CompilerOptions`.
    - _Requirements: 2.1, 5.2_
  - [x] 7.6 In-process tests: declared package type resolves through the provider (pre-cached package, no network); quiet build emits no `1.5` header/detail; verbose build emits header + detail + summary; resolution error surfaces in both modes; unresolved type → diagnostic without aborting.
    - _Requirements: 1.3, 1.6, 1.9, 2.2, 2.3_

- [ ] 8. CLI commands: `--verbose`, `mist restore`, `mist inspect`
  - [x] 8.1 Add `--verbose` flag and `Inspect`/`Restore` commands + `InspectTypeName` to `CliArguments`/`CliArgumentParser`; make `--verbose` and `--quiet` mutually exclusive (parse error); update usage text.
    - _Requirements: 1.3, 10.9, 13.9_
  - [x] 8.2 Implement `RunRestoreCommand`: require a project file; run resolution; print per-package/per-framework detail + zero-assemblies notes + success/failure summary unconditionally; emit `MIST_PKG_*`/`MIST_FWREF_*` on failure; exit 0 on success, non-zero on any failure or outside a project folder; no lex/typecheck/emit.
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7, 13.8, 13.9_
  - [x] 8.3 Implement `RunInspectCommand` + an inspection report builder: require a project file; resolve the same SDK+package+framework assemblies as a build; print FQN + kind, visible properties/methods (ascending ordinal, declaration-order params) with projected Mist types, hidden members with `Classify` reasons, inherited-member declaring base FQN; empty type → header + "no members"; unresolved/omitted-name/outside-project → guidance + non-zero exit.
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8, 10.9_
  - [x] 8.4 In-process tests for restore and inspect (invoke handlers/report builder directly, no spawning): restore success/failure output + exit codes; inspect listing/ordering/hidden-reasons/inherited base FQN/unresolved/missing-name.
    - _Requirements: 10.2, 10.5, 10.7, 13.2, 13.5, 13.8_
    - Evidence (2026-10-03): `RunRestoreCommand` now calls `Mist.Driver.DependencyRestore.Run`, and `RestoreCommandInProcessTests` call it too instead of a copy of the command's formatting. Added: two invalid packages sharing `MIST_PKG_002` and an unknown framework are reported as three diagnostics after `0 succeeded, 3 failed` (Tier 2, no process: `FrameworkReferenceResolver` now looks up the SDK root only for a known framework name), and `DependencyRestore.Report` over supplied results writes the detail, the zero-assembly note and the summary to output and the diagnostics to error (Tier 1). The two Newtonsoft.Json cases still run a real `dotnet restore` (Tier 4). Restore still never lexes, and its diagnostics still follow its summary. `inspect` is unchanged: it resolves dependencies but does not print their diagnostics.

- [ ] 9. Sample application
  - [x] 9.1 Create `samples/CSharpInteropV2/Program.mist` (Requirement 12 proof code verbatim) and `mist.toml` declaring `Newtonsoft.Json = "13.0.3"` and `[framework_references] "Microsoft.AspNetCore.App" = true`; Allman braces, `var` per policy; do NOT wire into AllFeatures.
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.7_
  - [x] 9.2 Add the dedicated end-to-end integration test (APPROVED single-spawn exception): restore + compile + run the sample, assert exit code 0 and byte-for-byte stdout against the proof output; configure UTF-8 stdout/stderr encoding.
    - _Requirements: 12.6, 12.8_

- [ ] 10. Documentation
  - [x] 10.1 Update `book/src/advanced/dotnet-interop.md`: quiet-by-default builds, `--verbose`, `mist restore`, `[framework_references]` (AspNetCore example), cross-assembly inheritance assignability + inherited-member callability, individual-overload hiding, ref structs unmapped, `mist inspect` (invocation + sample output); document `MIST_PKG_001/002/003` and `MIST_FWREF_001/002` with trigger + meaning. Add compiled snippets (SnippetEmitter) and link the chapter in `book/src/SUMMARY.md`.
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8_
  - [x] 10.2 Update `distribute/steering/mist-interop.md` (and `mist-language.md`/`mist-advanced-features.md` as needed): add `[framework_references]`; correct the false "ref structs projected as stack-only structs" statement to "ref structs not projected; members using them hidden per-overload"; document individual-overload hiding, cross-assembly inheritance, `mist inspect`, `mist restore`, quiet/`--verbose`; preserve the `MIST-MANAGED-STEERING` marker and front matter; keep the companion-file index accurate.
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 14.7_

- [x] 11. Full verification
  - Build the solution with zero warnings; run the full test suite (in-process tests + the single approved sample-spawn test); report the duration of the sample integration test and flag if it exceeds 5s.
  - _Requirements: 1.1, 6.1, 8.1, 9.1, 10.1, 12.8, 13.1_

## Task Dependency Graph

```json
{
  "waves": [
    { "wave": 1, "tasks": ["1", "2", "3", "4"], "rationale": "Investigation plus independent project-model and resolution primitives; no cross-dependencies." },
    { "wave": 2, "tasks": ["5"], "rationale": "Filtering/ref-struct changes depend on task 1's root-cause findings." },
    { "wave": 3, "tasks": ["6", "7"], "rationale": "Inheritance projection builds on corrected filtering (5); pipeline wiring needs the project model (2) and both resolvers (3, 4)." },
    { "wave": 4, "tasks": ["8"], "rationale": "CLI commands reuse the resolution wiring (7) and inspection classification (5)." },
    { "wave": 5, "tasks": ["9"], "rationale": "Sample proof code exercises inheritance (6), package/framework wiring (7), and the commands (8) together." },
    { "wave": 6, "tasks": ["10"], "rationale": "Documentation and steering describe the shipped behavior from waves 2–5." },
    { "wave": 7, "tasks": ["11"], "rationale": "Final zero-warning build and full test-suite verification." }
  ]
}
```

```
1 (investigation)
2 ──┐
3 ──┼──► 7 (pipeline wiring) ──► 8 (CLI commands) ──► 9 (sample)
4 ──┘                                                    │
1 ──► 5 (filtering/ref-structs) ──► 6 (inheritance) ────┤
                                                         ▼
                                              10 (docs) ──► 11 (verify)
```

- Task 1 precedes 5 (informs ref-struct removal and the filtering root cause).
- Tasks 2, 3, 4 are independent and all feed task 7 (the stage needs the project model + both resolvers).
- Task 5 precedes 6 (inheritance projection relies on the corrected member filtering) and feeds 8.3 (inspect uses `Classify`).
- Task 7 precedes 8 (commands reuse the resolution wiring) and 9 (the sample needs end-to-end package + framework resolution).
- Task 9 depends on 6, 7, 8 (the proof code exercises inheritance, package wiring, and overload filtering together).
- Tasks 10 and 11 come last; 10 documents the shipped behavior, 11 verifies the whole build.

## Notes

- **Zero-warnings policy:** task 7.1 changes a public interface signature; build and resolve any `CS*`/SDK warnings before proceeding, raising tradeoffs if suppression seems necessary.
- **Process spawning:** only task 9.2 spawns a process (approved single-spawn exception). All other tests use in-process compilation via a shared `CompilerDriver` and direct `AssemblyTypeProvider`/handler invocation.
- **Network/framework dependency:** task 9.2 performs a real NuGet restore and requires `Microsoft.AspNetCore.App` installed; report its measured duration and flag if >5s per the spec process-spawning approval policy.
- **Proof code is immutable:** the Requirement 12 proof code must compile and run verbatim; if it cannot, stop and report the gap rather than weakening the sample.
- **Diagnostic ID migration:** task 3.2 standardizes resolver IDs on `MIST_PKG_001/002/003`; update any existing tests asserting the old `DiagnosticId.PackageNotFound`/`PackageNetworkError`/`PackageNoCompatibleTfm` values.
```

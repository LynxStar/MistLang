# Design Document

## Overview

This feature closes six CLR/C# interop gaps in the Mist compiler's C# transpiler backend and driver. The work spans the compilation pipeline (`CompilerDriver`), the project model (`ProjectFileParser`/`ProjectConfiguration`), dependency resolution (`PackageResolver` + a new `FrameworkReferenceResolver`), the assembly type provider stack (`AssemblyTypeProvider`/`TypeProjector`/`VisibilityFilter`), and the CLI (`Mist.CLI`). It also updates user-facing documentation (the book and the distributed steering files) and adds a comprehensive sample.

The design is grounded in the current code. Key confirmed facts that shape it:

- `ProjectConfiguration.Packages` is parsed but never consumed; `PackageResolver.ResolvePackages` is never called; `CSharpTranspilerBackend.GetAmbientTypeResolver()` loads SDK reference assemblies only and takes no arguments. There is no channel today to pass package/framework assemblies into the provider.
- The pipeline reports integer-numbered stages (0 Cleaning … 6 Emit) through `ICompilationProgressReporter`. There is no package-resolution stage.
- `TypeProjector.ProjectSimpleTypeReference` builds a member-less, disconnected base-type shell. `ClassType.BaseType` is therefore not the cached, fully-projected type, breaking cross-assembly assignability and inherited-member lookup.
- `VisibilityFilter` already evaluates exclusion per individual `MemberInfo` (per-overload). It does **not** exclude ref-struct types today; `TypeProjector.ProjectStruct` projects them as `StructType.IsRefStruct = true`.
- The CLI dispatches `build`/`run`/`steering` via a hand-rolled parser; flags include `--quiet`. There is no `--verbose`, `inspect`, or `restore`.

## Goals and Non-Goals

### Goals

- Wire resolved NuGet package and framework-reference assemblies into the type provider.
- Resolve cross-assembly base types and interfaces so derived types are assignable to base types and inherited members are callable.
- Project ref-struct types (e.g., `Span<T>`) as Mist stack-only structs so they remain usable, and hide only the individual overloads that use genuinely-unsupported types (pointer params, `ref readonly` returns).
- Add a quiet-by-default package-resolution stage with `--verbose` opt-in, a dedicated `mist restore` command, and a `mist inspect <TypeName>` command.
- Add `[framework_references]` to `mist.toml` and resolve enabled shared frameworks.
- Update the book and distributed steering files; add a sample that proves the behavior end-to-end.

### Non-Goals

- Full first-party ref-struct safety (stackalloc plus escape/lifetime analysis) in Mist. Ref structs are projected as stack-only structs and remain usable, but Mist does not analyze their escape/lifetime; the downstream C# compiler is the safety backstop for misuse. First-party ref-struct safety is deliberate future work (Requirement 9).
- NuGet version-range support (exact versions only, unchanged).
- New Mist source syntax (no lexer/parser/TextMate-grammar changes).

## Architecture

### Dependency resolution moves into the driver

Today the backend silently discovers SDK assemblies inside `GetAmbientTypeResolver()`. This design moves **dependency resolution** into a dedicated, observable pipeline stage owned by the `CompilerDriver`, so it can report progress and surface diagnostics. The backend continues to own SDK discovery and provider construction, but now receives the target framework and the additional (package + framework-reference) assembly paths from the driver.

```
mist.toml ──► ProjectFileParser ──► ProjectConfiguration
                                      │  (Packages, FrameworkReferences, TargetFramework)
                                      ▼
                              CompilerOptions  (carries declared deps + Verbose flag)
                                      │
              ┌───────────────────────┴───────────────────────┐
              ▼                                                 ▼
   CompilerDriver: Stage "Loading" (1)              CompilerDriver: Stage "Package Resolution" (1.5)
                                                      │  PackageResolver.ResolvePackages(...)
                                                      │  FrameworkReferenceResolver.Resolve(...)
                                                      │  → ResolvedDependencies { AssemblyPaths, reports }
                                                      │  (verbose → reporter prints detail; errors → diagnostics always)
                                                      ▼
                            CompilerDriver: Stage "Semantic Model" (4)
                                      │  backend.GetAmbientTypeResolver(targetFramework, resolved.AssemblyPaths)
                                      ▼
                            AssemblyTypeProvider(SDK refs ∪ resolved.AssemblyPaths)
```

### Stage numbering: string stage labels

`ICompilationProgressReporter.ReportStageStart(int, string)` cannot express "Stage 1.5". The design changes the stage identifier from `int` to `string` across the reporter interface and its two implementations (`ConsoleProgressReporter`, `NullProgressReporter`) and the `CompilerDriver` call sites. Existing stages pass `"0".."6"`; the new stage passes `"1.5"`. This keeps the integer stages stable and avoids renumbering downstream stages or breaking baseline output ordering.

This is a contained, mechanical change. No suppression of warnings is expected; all call sites are updated. (Per the Zero-Warnings policy, if any `CS*` warning surfaces from the signature change it will be raised before proceeding.)

### One MetadataLoadContext spans all assemblies

`AssemblyTypeProvider` already loads every supplied assembly path into a single `MetadataLoadContext`. Because the driver now feeds SDK + package + framework-reference paths into one provider, `clrType.BaseType` and implemented interfaces (which may live in different assemblies) are all reachable `Type` instances in the same context. Cross-assembly inheritance resolution therefore becomes a matter of routing base/interface references through the provider's existing type cache rather than building disconnected shells.

## Components and Interfaces

### C1. Project model: framework references and target framework

**Files:** `Source/Mist/Driver/Contracts/ProjectConfiguration.cs`, a new `Source/Mist/Driver/Contracts/FrameworkReference.cs`, `Source/Mist/Driver/ProjectFileParser.cs`.

- Add `ImmutableArray<FrameworkReference> FrameworkReferences = default` and `string TargetFramework = "net10.0"` to `ProjectConfiguration`. (`TargetFramework` is added now so resolution is project-driven; an optional `target_framework` key may be parsed, defaulting to `net10.0`.)
- New record `FrameworkReference(string Name)`.
- `ProjectFileParser.Parse` gains a step (after `[packages]`) that reads the optional `[framework_references]` table:
  - Key = framework name, trimmed, 1–256 chars after trimming.
  - Value must be a TOML boolean. `true` includes the name (exactly once); `false` excludes it.
  - Diagnostics (severity Error), following the existing `MIST_PKG_* (historical scheme)` parser-diagnostic style:
    - `MIST0088` — `framework_references` present but not a table → empty list.
    - `MIST0091` — value not a boolean → exclude entry.
    - `MIST0089`, `MIST0090` — empty/whitespace name, or name > 256 chars → exclude entry.
  - Absent table → empty list, no diagnostic.
  - Like `[packages]`, entry-level errors `continue` (collect all) rather than abort.

> Note: the parser-level TOML-shape diagnostics above are distinct from the resolution diagnostics `MIST0034/002` (C3), which are emitted later when an enabled framework cannot be located.

### C2. Package resolution: stable diagnostics and a structured result

**File:** `Source/Mist/Driver/PackageResolver.cs`.

- Standardize resolver error diagnostic IDs on the stable identifiers from the requirements:
  - `MIST_PKG_001 (held back, no number)` — package provides no assemblies compatible with the Target_Framework (lists the TFMs the package offers).
  - `MIST0036` — package name/version not found on any configured feed (names every searched feed).
  - `MIST0037` — connection/network failure **or** restore timeout; the message indicates which category. Restore timeout bound set to **100 seconds** (the existing 60s download timeout is folded into this).
- Each failure emits exactly one diagnostic; resolution continues for the remaining packages (one independent diagnostic per failing package).
- Change the return shape from `ImmutableArray<string>` to a structured result so the stage/commands can report per-package outcomes:

```csharp
public sealed record PackageResolutionResult(
    ImmutableArray<string> AssemblyPaths,
    ImmutableArray<ResolvedPackageInfo> Resolved,   // name, version, selected TFM, assembly count
    int FailureCount);

public sealed record ResolvedPackageInfo(
    string Name, string Version, string SelectedTfm, int AssemblyCount);
```

A package that resolves to zero compatible assemblies but produces no error is recorded with `AssemblyCount == 0` so the verbose stage and `mist restore` can print the "contributed no assemblies" note.

### C3. Framework reference resolution (new)

**File:** `Source/Mist/Driver/FrameworkReferenceResolver.cs` (new), alongside `SdkDiscovery`.

```csharp
public static class FrameworkReferenceResolver
{
    public static FrameworkResolutionResult Resolve(
        ImmutableArray<FrameworkReference> frameworks,
        string targetFramework,
        DiagnosticBag diagnostics);
}

public sealed record FrameworkResolutionResult(
    ImmutableArray<string> AssemblyPaths,
    ImmutableArray<ResolvedFrameworkInfo> Resolved,
    int FailureCount);

public sealed record ResolvedFrameworkInfo(string Name, string Version);
```

- Locates the reference pack at the installed shared-framework pack path for the Target_Framework, reusing `SdkDiscovery`'s SDK-root discovery. The reference pack path mirrors the SDK ref pattern: `{dotnetRoot}/packs/{FrameworkName}.Ref/{version}/ref/{tfm}/`.
- Success requires the pack directory to exist and contain at least one reference assembly.
- Version selection: among installed packs whose **major** version matches the Target_Framework's major version, pick the greatest by integer major→minor→patch comparison.
- Diagnostics:
  - `MIST0034` — enabled framework not installed for the Target_Framework (names framework, TFM, searched pack path).
  - `MIST0035` — framework name is not a known shared framework. A small allow-list of known shared frameworks (`Microsoft.AspNetCore.App`, `Microsoft.WindowsDesktop.App`, `Microsoft.NETCore.App`) distinguishes "unknown name" (002) from "known but not installed" (001).
- Each enabled framework resolves independently; one diagnostic per failing framework.

### C4. Pipeline wiring

**Files:** `Source/Mist/Driver/Contracts/CompilerOptions.cs`, `ICompilationProgressReporter.cs`, `Source/Mist/Driver/CompilerDriver.cs`, `Source/Mist/Backends/Contracts/IBackend.cs`, `Source/Mist/Backends/CSharpTranspiler/CSharpTranspilerBackend.cs`, `Source/Mist.CLI/Program.cs`.

- `CompilerOptions` gains:
  - `ImmutableArray<NuGetPackageReference> Packages = default`
  - `ImmutableArray<FrameworkReference> FrameworkReferences = default`
  - `string TargetFramework = "net10.0"`
  - `bool VerboseResolution = false`
- `ICompilationProgressReporter`: stage identifier `int → string` (see Architecture).
- `CompilerDriver.Compile` adds the **Package Resolution stage ("1.5")** between Loading and Lexing:
  - Runs only when `options.Packages` or `options.FrameworkReferences` is non-empty.
  - Calls `PackageResolver.ResolvePackages` and `FrameworkReferenceResolver.Resolve`, accumulating diagnostics into `ResolutionDiagnostics`, a bag kept apart from the one the entry gate reads. Once both return, it delivers each diagnostic to the `IResolutionDiagnosticListener` the caller gave the driver, in report order and before Stage 2 begins; the result still carries them, joined back in stage order (Requirements 1.9, 3.5). The CLI's listener, `CompileFeedback`, writes them to stderr as they arrive and skips those instances when it writes the result's diagnostics after the summary.
  - When `VerboseResolution` is true, calls `reporter.ReportStageStart("1.5", "Package Resolution")` and `ReportStageComplete("1.5", elapsed, details)` where `details` contains one `Restored <name> <ver> (<tfm>)` line per package, one `Resolved <fw> <ver>` line per framework, any zero-assemblies notes, and a summary line with success/failure counts. When false, no reporter calls are made for this stage.
  - Stores the merged resolved assembly paths so the Semantic stage can pass them to the backend.
- `IBackend.GetAmbientTypeResolver()` → `GetAmbientTypeResolver(string targetFramework, ImmutableArray<string> additionalAssemblyPaths)`. The backend discovers SDK refs for `targetFramework`, unions with `additionalAssemblyPaths`, de-duplicates by assembly identity/path, and constructs the provider. The provider cache key becomes the full merged path set.
- `CompilerDriver` passes `options.TargetFramework` and the resolved paths to the backend at Stage 4.
- CLI `CompileProject` maps `config.Packages`, `config.FrameworkReferences`, `config.TargetFramework`, and `cliArgs.Verbose` into `CompilerOptions`.

### C5. Cross-assembly inheritance and inherited members

**Files:** `Source/Mist/Backends/CSharpTranspiler/TypeProjector.cs`, `AssemblyTypeProvider.cs`.

Root fix: base-type and interface references must resolve to the **cached, fully-projected** type rather than a member-less shell.

- `AssemblyTypeProvider` exposes a type-reference resolver to the projector: a delegate `Func<Type, IType?>` that maps a CLR `Type` to its cached projected `IType` via the provider's existing `(namespace, name, arity)` cache (the same path `ResolveType` uses). This is injected into `TypeProjector` (constructor parameter) so `ProjectTypeReference`/`ProjectSimpleTypeReference` can delegate to it.
- `ProjectClass`/`ProjectStruct` set `BaseType` and interface lists to the resolved cached types. The base chain is therefore connected: `NetworkStream.BaseType == Stream` (same instance the provider returns for `System.IO.Stream`), and `Stream.BaseType == MarshalByRefObject`, up to the root.
- Cycle/recursion safety: resolution is cache-mediated (the provider inserts the in-progress type into its cache before projecting members, as it does today for reference equality), so a base/interface reference back to an in-progress type returns the cached instance without infinite recursion.
- Unresolvable relationship (base/interface not in any loaded assembly): omit only that relationship, record a diagnostic indication (which relationship + unresolved type name), and continue projecting the derived type and its remaining members/relationships (Requirement 6.6).
- Inherited members: reflection enumeration already uses `Public | Instance | Static` **without** `DeclaredOnly`, so leaf types flatten inherited members. With the base chain now connected, the semantic model's assignability and member lookup through `BaseType` work across assemblies. Override/shadow: project only the most-derived visible signature (reflection returns the most-derived by default for flattened enumeration; de-duplicate by signature, preferring the most-derived declaring type). If the most-derived definition is hidden but an ancestor's is visible, project the nearest visible ancestor signature (Requirement 7.4).

### C6. Per-overload filtering, ref-struct exclusion, and hidden reasons

**Files:** `Source/Mist/Backends/CSharpTranspiler/VisibilityFilter.cs`, `TypeProjector.cs`, and a new inspection support type.

- `VisibilityFilter.IsTypeExcluded` excludes only non-public types. Ref-struct types (carrying `IsByRefLikeAttribute`) are **not** excluded — they are projected. `ReferencesExcludedType` therefore returns true only for signatures that reference a genuinely-excluded type (e.g., a non-public type, a pointer, or a `ref readonly` return), not for `Span<T>`/`ReadOnlySpan<T>`.
- Because `IsMemberExcluded` is evaluated per `MethodInfo`, only genuinely-offending overloads are excluded; sibling overloads using only supported types are retained. This satisfies Requirement 8 (per-overload). `Encoding.GetBytes(string)`, `Socket.Send(byte[])`, and `Console.ReadLine()` are retained, and their `Span` siblings are now projected alongside them rather than dropped.
- Project ref structs as stack-only structs: `TypeProjector.ProjectStruct` sets `StructType.IsRefStruct = true` for `IsByRefLike` CLR types (detected via `IsByRefLikeAttribute` metadata, since `Type.IsByRefLike` is unreliable under `MetadataLoadContext`), preserving the prior, working behavior and the `RefStructs` sample. `AssemblyTypeProvider.ResolveType` returns the projected `Span<T>`/`ReadOnlySpan<T>` type (non-null). Mist performs no escape/lifetime analysis for these projected types; the transpiled-C# compiler is the backstop, and full first-party ref-struct safety is future work.
- Hidden-member reasons for inspection: a classification API used only by the inspect command (the hot build path stays boolean):

```csharp
public enum MemberHiddenReason { None, NonPublic, Event, OutParameter, RefReadonlyReturn, Pointer, RefStructType, OtherExcludedType }

public MemberClassification Classify(MemberInfo member);
// MemberClassification { bool Hidden; MemberHiddenReason Reason; ImmutableArray<string> OffendingTypeNames; }
```

`RefStructType` is retained in the enum for compatibility but is no longer triggered by projection (ref structs are projected, not hidden). For `OtherExcludedType`, `OffendingTypeNames` lists each excluded type by name (e.g., a non-public type), enabling reason text like `HIDDEN: signature uses <Type>`.

### C7. CLI: `--verbose`, `mist inspect`, `mist restore`

**Files:** `Source/Mist.CLI/Args/CliArguments.cs`, `CliArgumentParser.cs`, `Source/Mist.CLI/Program.cs`, new `Source/Mist.CLI/Commands/` handlers, new `Source/Mist/Backends/CSharpTranspiler/TypeInspector.cs` (inspection report builder) or a CLI-side equivalent that consumes the provider + `Classify`.

- `CliArguments`: add `bool Verbose = false`; add `CliCommand.Inspect`, `CliCommand.Restore`; add `string? InspectTypeName`.
- `CliArgumentParser`: handle `--verbose`; match literals `inspect` and `restore`; for `inspect`, capture the next positional as the type name.
- `mist build`/`mist run`: when `Verbose`, the build uses the `ConsoleProgressReporter` and sets `CompilerOptions.VerboseResolution = true`. Default builds leave `VerboseResolution = false` (no package-stage header/detail), but errors still print via the diagnostic path: the resolution listener, which no progress setting replaces. (The existing `--quiet` behavior is preserved; `--verbose` and `--quiet` are mutually exclusive — supplying both is a parse error.)
- `mist restore` (`RunRestoreCommand`): requires a project file in the folder; parses config; runs `PackageResolver` + `FrameworkReferenceResolver`; prints per-package and per-framework detail and any zero-assemblies notes **unconditionally**, plus a success/failure summary; emits `MIST_PKG_* (historical scheme)`/`MIST_FWREF_* (historical scheme)` on failure; exits 0 on full success, non-zero on any failure or when run outside a project folder. Does not lex/type-check/emit.
- `mist inspect <TypeName>` (`RunInspectCommand`): requires a project file; resolves the same SDK + package + framework assemblies a build would use; constructs the provider; resolves `<TypeName>` (fully-qualified namespace + name). Prints the FQN and kind (exactly one of class/struct/interface/enum); lists visible properties and methods (ascending ordinal by name; parameters in declaration order) with projected Mist types; lists hidden members with reasons from `Classify`; for inherited members, indicates the declaring base type by FQN. Empty type → header + "no members". Unresolved type → message + non-zero exit. Omitted name or run outside a project folder → usage guidance + non-zero exit.

The inspection report builder reuses the provider's projection (for visible members and their Mist types) and `VisibilityFilter.Classify` (for hidden members and reasons), enumerating the CLR type's members (including inherited) once.

### C8. Documentation and sample

- **Book** (`book/src/advanced/dotnet-interop.md`, `book/src/SUMMARY.md`, `book/snippets/...`): document quiet-by-default builds, `--verbose`, `mist restore`, `[framework_references]` (with `Microsoft.AspNetCore.App` example), cross-assembly inheritance, individual-overload hiding, ref structs remaining unmapped, and `mist inspect` (invocation + sample output). All diagnostic IDs (`MIST_PKG_001 (held back, no number)/002/003`, `MIST0034/002`) documented with trigger + meaning. New code examples are compiled snippets per the Book Snippet Policy; ensure SUMMARY.md links the chapter (it is currently unlinked).
- **Steering** (`distribute/steering/mist-interop.md`, and as needed `mist-language.md`/`mist-advanced-features.md`): apply Requirement 14, including correcting the now-false "ref structs are projected as stack-only structs" statement. Preserve the `MIST-MANAGED-STEERING` marker and front matter.
- **Sample** (`samples/CSharpInteropV2/Program.mist` + `mist.toml`): the Requirement 12 proof code verbatim; `mist.toml` declares `Newtonsoft.Json = "13.0.3"` and `[framework_references] "Microsoft.AspNetCore.App" = true`. `samples/AllFeatures` calls `CSharpInteropV2.Program.RunDemo()` and declares the same package + framework reference; baseline updated.

## Data Models

| Type | Change |
|------|--------|
| `ProjectConfiguration` | + `ImmutableArray<FrameworkReference> FrameworkReferences`, + `string TargetFramework = "net10.0"` |
| `FrameworkReference` | new record `(string Name)` |
| `CompilerOptions` | + `Packages`, + `FrameworkReferences`, + `TargetFramework`, + `bool VerboseResolution` |
| `PackageResolutionResult` / `ResolvedPackageInfo` | new records (structured resolver output) |
| `FrameworkResolutionResult` / `ResolvedFrameworkInfo` | new records |
| `MemberHiddenReason` / `MemberClassification` | new (inspection support) |
| `CliArguments` | + `bool Verbose`, + `CliCommand.Inspect/Restore`, + `string? InspectTypeName` |
| `ICompilationProgressReporter` | stage id `int → string` |
| `IBackend.GetAmbientTypeResolver` | `() → (string targetFramework, ImmutableArray<string> additionalAssemblyPaths)` |

## Error Handling

| ID | Severity | Condition | Owner |
|----|----------|-----------|-------|
| `MIST0088` | Error | `framework_references` not a table | ProjectFileParser |
| `MIST0091` | Error | entry value not boolean | ProjectFileParser |
| `MIST0089`, `MIST0090` | Error | empty/whitespace or >256-char name | ProjectFileParser |
| `MIST_PKG_001 (held back, no number)` | Error | no assemblies compatible with Target_Framework | PackageResolver |
| `MIST0036` | Error | package/version not found on any feed | PackageResolver |
| `MIST0037` | Error | connection/network failure or restore timeout | PackageResolver |
| `MIST0034` | Error | enabled framework not installed for Target_Framework | FrameworkReferenceResolver |
| `MIST0035` | Error | unknown shared-framework name | FrameworkReferenceResolver |

All resolution errors always surface regardless of `--verbose` or `--quiet`: delivered to the resolution listener during Stage 1.5, before lexing, and kept in the result for its error count and for callers with no listener. `mist restore` runs no later stage and prints its diagnostics after its summary. Resolution failures do not abort the build before Lexing; the build continues with whatever resolved successfully (Requirements 2.6, 5.8). Unresolved cross-assembly relationships are dropped silently from emission but recorded for diagnostics (Requirement 6.6).

## Testing Strategy

In-process tests (no process spawning), using the shared-driver / direct-provider patterns already established in `Test/Mist.Tests`:

- **ProjectFileParser**: `[framework_references]` table parsing — valid true/false, absent table, non-table value, non-boolean value, empty/oversized names. (`Driver/`)
- **FrameworkReferenceResolver**: pack-path discovery, version selection (major-match, highest), `MIST0034` (not installed) and `MIST0035` (unknown name). Resolution success asserted against the locally-installed `Microsoft.AspNetCore.App` pack; if absent in CI, the not-installed path is asserted instead. (`Driver/`)
- **PackageResolver**: structured-result shape and `MIST_PKG_001 (held back, no number)/002/003` diagnostics. Network-dependent restore is covered by existing TFM-selection unit tests plus targeted tests using already-cached packages; no new network-heavy property tests.
- **VisibilityFilter**: ref structs are NOT excluded (`Span<>`/`ReadOnlySpan<>` not excluded); per-overload behavior with genuinely-unsupported overloads; ref-struct overloads stay visible (Span sibling and `byte[]` sibling both kept) using `Test/Mist.Tests.InteropFixtures` (a fixture exposing mixed Span/non-Span overloads); `Classify` reports ref-struct overloads as visible. (`Backend/`)
- **TypeProjector / AssemblyTypeProvider**: cross-assembly base type and interface resolution and assignability, asserted against genuinely cross-assembly SDK types (e.g., `NetworkStream : Stream`, `IPEndPoint : EndPoint`) loaded through a provider built from runtime + SDK paths; inherited-member access; unresolved-relationship fallback. (`Backend/`)
- **Inspect/Restore command logic**: invoke the report builder and the restore-resolution logic **in-process** (call the command handler functions directly, not via spawning the CLI). Assert printed output, exit codes, and diagnostics.
- **Pipeline integration**: a driver-level test that compiles a small program referencing a package type, asserting the resolved assembly reaches the provider and the type resolves — using a pre-cached package to avoid network flakiness.

**Process-spawning surface:** `CSharpInteropV2` is **excluded** from `samples/AllFeatures` (it requires network NuGet restore and an installed shared framework). Instead, a dedicated integration test compiles and runs the `CSharpInteropV2` sample end-to-end — performing a real `Newtonsoft.Json` restore and resolving `Microsoft.AspNetCore.App` — and asserts exit code 0 and byte-for-byte stdout against the proof output. This single test is an **approved exception** to the Test Process Spawning Policy (one spawn, full end-to-end exercise of the one sample) and configures UTF-8 stdout/stderr per the Test UTF-8 Encoding Policy. All other tests are in-process and spawn no processes.

## Design Decisions and Tradeoffs

1. **Resolution in the driver, not the backend.** Moving resolution into an observable stage is the only way to satisfy the verbose/quiet/restore reporting requirements and to surface `MIST_PKG_* (historical scheme)`/`MIST_FWREF_* (historical scheme)` consistently. Cost: a signature change to `IBackend.GetAmbientTypeResolver`. Accepted — the backend still owns SDK discovery and provider construction.
2. **String stage identifiers.** Chosen over renumbering integer stages (which would churn baselines and every call site's meaning) or a decimal type. Localized to the reporter contract + two implementations + driver call sites.
3. **Cache-mediated base-type resolution.** Reuses the provider's existing reference-equality cache rather than introducing a separate inheritance graph, which both fixes cross-assembly resolution and preserves the recursion-safety the cache already provides.
4. **Per-overload filtering is mostly emergent.** Since filtering is already per-`MemberInfo`, the main risk is that adding ref-struct exclusion could surface a latent group-level cascade. An early investigation task pins the actual root cause of the user's "all overloads hidden" report before changing filtering, so the fix is targeted.

   **Investigation outcome (Task 1).** The root cause was pinned by reading the projection/scope/overload-resolution paths and confirmed empirically with a throwaway in-process probe built directly on `AssemblyTypeProvider` against the real SDK assemblies. Findings:

   - **No group-level cascade exists at the projection layer.** `TypeProjector.ProjectMethods` evaluates `VisibilityFilter.IsMemberExcluded` per `MethodInfo` and skips only the offending method via `continue`; surviving overloads are still projected. `Scope.AddMember` appends each symbol to a backing `List<ISymbol>`, so same-named overloads coexist, and `Scope.LookupAll` returns all of them. Excluding one overload therefore removes exactly that overload — siblings are unaffected. (This also gives Requirement 8.6 for free: when *every* overload is excluded, none is added and the name projects no symbol.)
   - **No group-level filtering at `GetMembers`.** `AssemblyTypeProvider.GetMembers` returns the cached member array verbatim; it performs no name-group collapsing.
   - **The user's symptom is not reproducible against current code.** With ref structs *not yet excluded*, the probe showed the full groups project today: `System.Text.Encoding.GetBytes` → 7 overloads (including the `ReadOnlySpan<char>`/`Span<byte>` one), `System.Net.Sockets.Socket.Send` → 11 (including three `ReadOnlySpan<byte>` ones), `System.Console.ReadLine` → 1. The `Span<T>` overloads currently project against **disconnected `Span` shells** (created by `ProjectSimpleTypeReference`, which does not set `IsRefStruct`), not against the cached fully-projected `Span` type. The probe also confirmed `ResolveType("System","Span")` / `"ReadOnlySpan"` currently return `StructType` with `IsRefStruct == true`.
   - **Conclusion / impact on Tasks 5 and 7.** The "all hidden" concern was *forward-looking* — the risk that introducing ref-struct exclusion might cascade. Under Option A that exclusion is **not** introduced: ref structs are projected as stack-only structs and the `Span` overloads project alongside their siblings, exactly as the probe observed today. Because filtering and scope insertion are strictly per-`MemberInfo`, genuinely-unsupported overloads (pointer params, `ref readonly` returns) are still hidden individually while `GetBytes(string)`, `Send(byte[])`, and `ReadLine()` stay callable. No group-level guard is needed.

5. **Ref structs projected as stack-only structs, not faked away.** Ref-struct types (`Span<T>`, `ReadOnlySpan<T>`, any `IsByRefLike` type) are projected as Mist stack-only structs (`StructType.IsRefStruct = true`), preserving the prior, working behavior and keeping the `RefStructs` sample compiling and running. Mist does not yet perform escape/lifetime analysis for projected CLR ref structs, so the projection is unsound in the general case; the transpiled-C# compiler is the safety backstop for any misuse (storing a ref struct on the heap, boxing, capturing, generic-arg use). Full first-party ref-struct safety (stackalloc plus escape analysis) is deliberate future work, tracked separately. This is more useful than refusing to project a widely-used type: the alternative (excluding ref structs and hiding the members that use them) was tried and reverted because it removed large, commonly-needed swaths of the CLR surface for a safety guarantee Mist's downstream compiler already provides.

   **Investigation outcome (Task 1) — scope of the `IsRefStruct` handling.** A codebase-wide search confirmed `StructType.IsRefStruct` is **load-bearing** for both Mist's own source-declared `ref struct` feature and the projection of CLR ref structs. Specifically:
   - `SemanticModelBuilder` sets `structType.IsRefStruct = HasRefModifier(structDecl.Modifiers)` for Mist-source `ref struct` declarations (covered by `RefStructParsingPropertyTests`).
   - `TypeProjector.ProjectStruct` sets `structType.IsRefStruct = true` for `IsByRefLike` CLR types (detected via `IsByRefLikeAttribute` metadata, since `Type.IsByRefLike` is unreliable under `MetadataLoadContext`). This projection is **retained** under Option A.
   - `CSharpEmitter` (~line 3931) uses `IsRefStruct` only to emit the `ref ` prefix on a Mist-source **struct declaration**; projected CLR types are never re-emitted as declarations, so this path is unaffected by the projection assignment.
   - `TypeCheckerAnalyzer` and `SemanticModelBuilder` enforce ref-struct discipline (no field/property in non-ref-struct, no capture, no boxing to interface, no generic-arg/union/optional use) keyed off `IsRefStruct`; these checks remain for Mist-declared ref structs.

   Therefore the implementation **retains** the projection-time assignment in `TypeProjector.ProjectStruct` and `AssemblyTypeProvider.ResolveType` returns the projected `Span<T>`/`ReadOnlySpan<T>` type (non-null). `VisibilityFilter.IsTypeExcluded` excludes only non-public types, so members whose signatures reference ref structs stay visible.
6. **Known-framework allow-list.** A small allow-list distinguishes `MIST0035` (unknown name) from `MIST0034` (known but not installed), giving actionable errors without attempting to enumerate every conceivable shared framework.

## Correctness Properties

These properties define observable invariants the implementation must uphold. They map back to the requirements and are the basis for the test suite.

### Property 1: Package assemblies reach the type provider
For any project declaring a `[packages]` entry that resolves to at least one assembly, a public type from that package is resolvable by the semantic model by its CLR namespace and name with no configuration beyond the entry. Falsifiable: remove the driver→backend wiring and the type fails to resolve.

**Validates: Requirements 2.1, 2.2, 2.4**

### Property 2: Resolution diagnostics are deterministic and category-accurate
Each failing package or framework produces exactly one error diagnostic with the correct stable ID for its failure category (`MIST_PKG_001 (held back, no number)` unsupported TFM, `MIST0036` not found, `MIST0037` network/timeout; `MIST0034` not installed, `MIST0035` unknown name), independently per dependency.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.6, 5.4, 5.5, 5.7**

### Property 3: Errors surface regardless of verbosity; success detail is gated
For any resolution outcome: error diagnostics appear whether or not `--verbose` is supplied. The stage header and per-package/per-framework detail lines appear if and only if `--verbose` is supplied (build/run) or the command is `mist restore`.

**Validates: Requirements 1.3, 1.4, 1.9, 13.2, 13.3**

### Property 4: Cross-assembly assignability is reflexive over the chain
For a projected type `D` whose ancestor chain `D → … → A` spans assemblies, a value of `D` is assignable to every ancestor type and implemented interface in the chain, and casts in both directions are accepted. Falsifiable: with disconnected base shells, assignment to `A` is rejected.

**Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5**

### Property 5: Inherited members are callable at the most-derived visible signature
For a projected derived type, every visible public member declared on any ancestor is invocable on the derived type; an overridden/shadowed member exposes exactly one (most-derived visible) signature; a member hidden at the most-derived level falls back to the nearest visible ancestor signature; a fully hidden inherited member is not invocable.

**Validates: Requirements 7.1, 7.3, 7.4, 7.7, 7.8**

### Property 6: Overload filtering is local to the offending overload
For an overload group containing both supported overloads and overloads that use a genuinely-unsupported type (a pointer parameter or a `ref readonly` return), exactly the unsupported overloads are excluded and the supported overloads remain resolution candidates. If and only if every overload is excluded, the method name projects no symbol.

**Validates: Requirements 8.1, 8.2, 8.6, 8.7**

### Property 7: Ref structs are projected as usable stack-only structs
Every CLR type carrying `IsByRefLikeAttribute` (including `Span<T>`/`ReadOnlySpan<T>`) is projected as a Mist stack-only struct (`StructType.IsRefStruct == true`) and is resolvable by namespace and name (non-null), not excluded. Members whose signatures reference a ref-struct type in any position stay visible. Falsifiable: re-introduce ref-struct exclusion and `ResolveType("System","Span",1)` returns null, the `RefStructs` sample fails to compile, and span-using overloads disappear.

**Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5**

### Property 8: Inspect output is complete, ordered, and reasoned
For a resolvable type, `mist inspect` lists every visible property and method (ascending ordinal by name, parameters in declaration order) with projected Mist types, every hidden member with a reason, and the declaring base FQN for inherited members; unresolved/omitted-name/outside-project cases exit non-zero.

**Validates: Requirements 10.2, 10.3, 10.4, 10.5, 10.7, 10.8**

### Property 9: Sample proof code produces exact output
`samples/CSharpInteropV2` compiles with zero error diagnostics, exits 0, and emits the Requirement 12 expected output byte-for-byte, exercising package resolution, framework references, cross-assembly inheritance, inherited members, and per-overload filtering in one run.

**Validates: Requirements 12.3, 12.7, 12.8**

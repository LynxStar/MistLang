# Multi-Project Emission (Architectural Change)

## Problem

The C# backend currently emits all compilation units (user code + all referenced projects) as source files into a single `out/` directory compiled into one assembly. This causes:

1. **CS0017 entry point conflicts** — Referenced projects with `Main` methods conflict with the consumer's entry point (or the test SDK's generated entry point). Currently worked around with in-process Roslyn's `mainTypeName` parameter, but `dotnet test`/`dotnet build` on the generated csproj doesn't have that disambiguation.

2. **Broken transitive isolation at assembly level** — The semantic model correctly isolates transitive dependencies (A→B→C: A can't see C's symbols during name resolution), but the emitter dumps all units into one assembly anyway. If Mist gains an `internal` modifier, there's no assembly boundary to enforce it.

3. **Future issues** — Anything that relies on assembly-level isolation (InternalsVisibleTo, assembly-level attributes, per-project NuGet references, per-project output types) will break or require workarounds.

## Current Architecture

```
Driver loads:  [stdlib] + [user .mist] + [ref project A .mist] + [ref project B .mist] + ...
                                    ↓
SemanticModel:  One flat model (transitive symbols in separate scope for name resolution only)
                                    ↓
Backend emits:  out/Program.cs, out/references/A/Foo.cs, out/references/B/Bar.cs, ...
                                    ↓
Roslyn:         One compilation → one DLL
```

## Target Architecture

```
Driver loads:   Same as today (semantic model still needs full picture)
                                    ↓
SemanticModel:  Same as today (full type checking across all projects)
                                    ↓
Backend emits:  out/B/B.csproj + B's .cs files
                out/A/A.csproj + A's .cs files + <ProjectReference> to B
                out/Root/Root.csproj + Root's .cs files + <ProjectReference> to A
                                    ↓
Roslyn:         Compile in dependency order (leaves first), each project → own DLL
                Each project references dependent DLLs as metadata references
```

## Key Design Decisions

### 1. Roslyn compilation strategy
- **Option A**: Compile each project separately via in-process Roslyn, passing dependent DLLs as `MetadataReference`. Keeps current speed but more complex orchestration.
- **Option B**: Emit csprojs with `<ProjectReference>` and let MSBuild handle build order. Simpler but slower (MSBuild startup overhead per project). However, this is what `dotnet test` needs anyway.
- **Option C**: Hybrid — in-process Roslyn for `mist run`/`mist build` (speed), emit proper csprojs for `mist test` (compatibility with dotnet test).

### 2. Dependency graph flow
The `ProjectReferenceResolver` already builds the full dependency graph. This graph (or a summary of it) needs to reach the backend. Currently the backend only receives `ISemanticModel` + flat parameters. Options:
- Add a `ProjectGraph` to the data passed to `IBackend.Emit()`
- Derive the graph from `CompilationUnit.OriginProjectPath` relationships in the model

### 3. Compilation order
Must compile in topological order (leaves first). The driver already resolves this order during loading — reuse that.

### 4. NuGet packages and framework references
Each project's `mist.toml` declares its own packages. Currently these all get merged into the root csproj. With multi-project emission, each project's csproj gets only its own package references.

### 5. AllFeatures sample (40+ references)
Each referenced sample becomes its own csproj/DLL. Should be fine since they're small, but compilation time will increase due to per-project Roslyn overhead. Consider parallel compilation of independent leaves.

### 6. Emitter-owned marker types need one shared home
The v2 emitter writes `Mist.Emission.CSharp.__StructInit` and `__DefaultInit` into every compilation (`CSharpEmitterV2.EmitStructInitMarker`). That is correct while everything compiles into one assembly. Split per project, each DLL would declare its own copy, and a derived class's default could no longer chain into a base in another project. Since 2026-10-01 that default chains `: base(marker)` into the base's protected sentinel, which takes a `__DefaultInit`. Emit the markers once, in an assembly every project references (Standard's, or a small runtime assembly), before splitting.

## Affected Code

- `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs` — partition emission by project
- `Source/Mist/Backends/CSharpTranspiler/CSharpCompilationDriver.cs` — multi-project compilation
- `Source/Mist/Backends/CSharpTranspiler/CSharpTranspilerBackend.cs` — orchestrate build order
- `Source/Mist/Backends/Contracts/IBackend.cs` — may need project graph in signature
- `Source/Mist/Driver/CompilerDriver.cs` — pass project graph to backend
- `Source/Mist/CLI/Program.cs` — `mist test` no longer needs special csproj workarounds

## Immediate Workaround

Until this is implemented, library projects (test projects) that reference projects with `Main` methods need `<GenerateProgramFile>false</GenerateProgramFile>` in the generated csproj to prevent the test SDK from injecting a conflicting entry point.

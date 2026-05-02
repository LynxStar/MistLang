---
inclusion: always
---

# Test Process Spawning Policy

## Rule

Unit and integration tests MUST NOT spawn an external process (e.g. `dotnet`, `Process.Start`) more than a small fixed number of times per test run. Spawning a process 100+ times in a test suite is forbidden.

## Why

Each `dotnet` process spawn costs ~1–2 seconds of startup overhead. 100 iterations × 1.5s = 2.5 minutes for a single test class. This was observed in `OperatorRoundTripPropertyTests` and `BootstrapNumericOperatorPropertyTests`, which originally took ~8 minutes combined.

## Approved Patterns

**Batching**: Generate all N scenarios upfront, build a single program that prints N results (one per line), compile once, run once, compare line-by-line. Report the failing scenario index and its inputs on failure.

```csharp
// Good: 1 compile, 1 process spawn, N output lines compared
var scenarios = Gen.Sample(100);
var source = BuildBatchedProgram(scenarios);
var (exitCode, stdout, _) = CompileAndRun(source);
var lines = stdout.TrimEnd().Split('\n');
for (var i = 0; i < scenarios.Count; i++)
    Assert.True(lines[i] == Expected(scenarios[i]),
        $"Scenario {i} failed: {scenarios[i]}");
```

**In-process compilation**: Use `CompilerDriver.Compile()` directly instead of `dotnet run --project`. This eliminates the MSBuild/restore overhead entirely. Only spawn a process to run the compiled DLL output.

```csharp
// Good: compile in-process, only spawn dotnet to run the output DLL
var result = SharedDriver.Compile(options);
var process = Process.Start("dotnet", result.PrimaryOutputArtifactPath);
```

**Fixed deterministic cases**: Replace random property-based tests with `[Theory]` + `[InlineData]` covering the meaningful boundary values explicitly (e.g. 0, 1, 2, 3, 4, 5 overloads).

## Forbidden Patterns

```csharp
// Bad: spawns a new process per FsCheck iteration
[Property(MaxTest = 100)]
public Property MyTest(Scenario s)
{
    var process = new Process { StartInfo = new ProcessStartInfo { FileName = "dotnet", ... } };
    process.Start();
    ...
}
```

```csharp
// Bad: dotnet run --project rebuilds the CLI on every call
Arguments = $"run --project \"{cliProjectPath}\" -- run \"{tempDir}\"";
```

## Shared Driver

When multiple test iterations need compilation, use a single shared `CompilerDriver` instance rather than constructing a new one per iteration. The driver is stateless and safe to reuse.

```csharp
private static readonly CompilerDriver SharedDriver = new(
    new MistLexer(), new MistParser(), new SemanticModelBuilder(),
    ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
    ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
```

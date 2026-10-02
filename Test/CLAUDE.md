# Tests

Before adding or moving a test under `Test/`, read the testing
philosophy, `.claude/skills/testing-philosophy/SKILL.md` (`/testing-philosophy`), and decide its
tier from the invariant it encodes — strict adherence is
required for all test code.

# Test tier classification

Every new or migrated test must carry a discoverable tier. Use xUnit's native
`[Trait("Tier", "N")]`, with the exact key `Tier` and a value from `"1"` to `"6"`: Unit, Functional,
Component, Integration, System and Full system respectively. Existing tests may stay unmarked
until classified; missing metadata means unclassified, never Tier 1 by default.

Each test has exactly one effective tier. Put the trait on a concrete test class when all its
tests share a tier. In a mixed class, leave the class untagged and tag each test method. xUnit
combines class and method traits, so a method trait does not override a class trait. Do not put
tier defaults on assemblies or shared base fixtures. The convention covers `[Fact]`, `[Theory]`
and FsCheck `[Property]`; all rows of a theory share its tier, so split cases that need different
tiers into separate methods. Other feature/category traits and scheduling collections remain
independent.

```csharp
[Trait("Tier", "1")]
public sealed class LiteralDecisionTests
{
    [Fact]
    public void OutOfRangeLiteralIsRefused()
    {
        // Directly construct the decision's inputs and assert its result.
    }
}
```

```csharp
public sealed class BoundaryTests
{
    [Fact]
    [Trait("Tier", "4")]
    public void BindingFailureDoesNotCascadeIntoAnalysis()
    {
        // Exercise only the binding-to-analysis seam.
    }
}
```

Classify the invariant first, then check the implementation and every helper/fixture it invokes
against that tier's permitted infrastructure. A Tier 1 label on a test that calls the compiler
is an audit finding. Refactor it down or justify the higher-tier invariant and label it honestly;
changing the tag alone does not make misplaced coverage acceptable. Names, folders and tier
comments may explain intent, but runnable metadata is the classifier.

Filter classified tests through the ledger, for example:

```bash
tools/test-ledger/run.sh Test/Mist.Tests --filter 'Tier=1'
tools/test-ledger/run.sh Test/Mist.Tests --filter 'Tier=4|Tier=5|Tier=6'
```

A filtered run covers only classified tests matching that filter; use the unfiltered suite for
full regression coverage while classification is incomplete. Audits compare the tier metadata
with actual dependencies and with the pyramid's case budgets. Count theory rows and repeated
pipeline executions as coverage/cost, even when one method hides hundreds of them. Higher-tier
additions beyond the root `CLAUDE.md` limits need explicit human approval for a documented
exception before adding them, with the invariant, case count, expected cost and lower-tier
alternatives stated. Ordinary tests within those limits need no extra approval.

# Compiling a Program Is Tier 4 or Above

`CompilerDriver.Compile` runs every stage, so a test that calls it is at Tier 4 or above however
small its program is. A test whose assertion is about one stage — a diagnostic, a binding, an
emitted string — belongs at Tier 1–3 and does not compile a program. The patterns below make a
legitimate Tier 4–6 test cheaper. They never make a compile acceptable at a lower tier, and
"in-process" is not a licence to compile hundreds of programs.

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

When multiple test iterations need compilation, use a single shared `CompilerDriver` instance rather than constructing a new one per iteration. Reuse it **sequentially, not concurrently**: no test establishes that one driver is safe for concurrent `Compile` calls, and a driver reports every compile's stage events to the one progress reporter it was constructed with. A `private static` driver owned by one test class is sequential, because xunit runs a class's tests one at a time. A driver shared by several classes runs concurrently, so give each class its own.

The lexed and parsed standard library is cached once per process for every driver (`Source/Mist/Driver/StdlibParseCache.cs`), whatever lexer and parser each driver was given.

```csharp
private static readonly CompilerDriver SharedDriver = new(new MistLexer(), new MistParser());
```

# Test Output Encoding Policy

## Rule: ASCII-Only Test Output

All expected output strings in tests and all string literals in sample/test Mist programs MUST use only ASCII-safe characters (U+0000–U+007F). Do NOT use characters outside this range, including:

- Em dashes (`—` U+2014) — use `--` or `-` instead
- Smart quotes (`"` `"` `'` `'`) — use straight quotes `"` `'` instead
- Ellipsis (`…` U+2026) — use `...` instead
- Non-breaking spaces (U+00A0)
- Accented characters, symbols, emoji, or any codepoint above U+007F

## Why

The Mist compiler does NOT inject `Console.OutputEncoding = UTF8`. On Windows, when stdout is redirected to a pipe (as in test process spawning), .NET defaults `Console.OutputEncoding` to the system's active code page (e.g., CP437 or CP1252). Characters outside that code page get "best fit" mapped to ASCII approximations (e.g., `—` becomes `-`) or replaced with `?`, causing silent test failures.

Rather than fighting encoding configuration on both sides of every process boundary, we avoid the problem entirely by restricting test content to the ASCII subset that is identical across all code pages.

## Reader Side (Still Required)

Tests that spawn a compiled Mist program and read its stdout SHOULD still set `StandardOutputEncoding = System.Text.Encoding.UTF8` on `ProcessStartInfo` as a defense-in-depth measure. This protects against accidental non-ASCII output that might slip through.

```csharp
var psi = new ProcessStartInfo
{
    FileName = "dotnet",
    Arguments = dllPath,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    StandardOutputEncoding = System.Text.Encoding.UTF8,
    StandardErrorEncoding = System.Text.Encoding.UTF8,
    UseShellExecute = false,
    CreateNoWindow = true,
};
```

## Forbidden Patterns

```mist
// BAD: non-ASCII em dash in string literal
Console.WriteLine("Hello — World")
```

```mist
// BAD: smart quotes
Console.WriteLine("\u201CHello\u201D")
```

```csharp
// BAD in tests: non-ASCII in expected output
Assert.Equal("Hello — World", output);
```

## Approved Patterns

```mist
// GOOD: ASCII-only
Console.WriteLine("Hello - World")
Console.WriteLine("Hello -- World")
```

```csharp
// GOOD in tests: ASCII-only expected output
Assert.Equal("Hello - World", output);
```

## Applies To

- All test files in `Test/Mist.Tests/` that spawn processes and compare stdout
- All integration tests that compile Mist code and run the resulting DLL
- All `.mist` sample/snippet files used by tests
- All expected-output baselines

## Does NOT Apply To

- The Mist language itself — user programs may use any Unicode they want
- Documentation and book content
- Error messages shown to users via the CLI (the CLI can manage its own encoding)

# Test Failure Triage Policy

Two rules for the same situation: a test just failed and you need to know whether it is yours.

## Rule 0: Run the suite through the ledger

A full run goes through `tools/test-ledger/run.sh`, started in the background. It builds, runs with
a trx logger, and keeps everything in `TestResults/ledger/<stamp>/`; `TestResults/ledger/latest`
points at the newest run from the moment it starts:

```bash
tools/test-ledger/run.sh
```

Its stdout is only the ledger's path and, at the end, `summary.txt`, so run it as it is. Do not
redirect it to a file of your own, and do not pipe it or a bare `dotnet test` through `grep`, `head`
or `tail`: that is how five entries in `backlog/flaky-tests.yaml` lost the name of the test that
failed.

When it ends, read `summary.txt`. Its first line says how the run ended, and a `no trx:` line names
an assembly that started and never finished. Then read `failures.txt`: every failure's full message
and stack, including any the console printed before a stopped run could write its trx. Stopping a
run (TaskStop, Ctrl-C) still writes both. Read the ledger before rerunning, because a green rerun
destroys the evidence.

Pass a project and `dotnet test` arguments after it, e.g.
`tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~X"`, and set
`LEDGER_NO_BUILD=1` when the binaries are current.

Running `dotnet test` while another worktree's suite or build is running changes timing. The ledger
records other `dotnet` processes in `env.txt`, so check it before reading a slow test as a defect. A
test that asserts on elapsed time belongs in `[Collection(WallClockCollection.Name)]`, which runs
after the parallel phase; in parallel, most of what a stopwatch reads is other tests' garbage
collection.

## Rule 1: Rebuild before blaming the working tree

When verifying whether a test failure is pre-existing by stashing changes, you MUST rebuild before running tests. Using `dotnet test --no-build` after `git stash` is **always wrong** because the compiled binaries still reflect the stashed (new) code, not the working tree.

### Why

`dotnet test --no-build` reuses whatever binaries are already in `bin/`. After `git stash`, the source files revert but the compiled output remains unchanged. The test run exercises the old binaries and proves nothing about the old source.

### Correct Pattern

```bash
# 1. Stash your changes
git stash

# 2. Rebuild from the now-reverted source (REQUIRED)
dotnet build

# 3. Run the tests against the freshly-built old code
dotnet test --no-build

# 4. Restore your changes
git stash pop
```

The key: `dotnet build` (or `dotnet test` without `--no-build`) MUST appear between `git stash` and the test execution.

### Forbidden Pattern

```bash
# BAD: stash changes then skip the build — tests still run NEW code
git stash
dotnet test --no-build   # ← proves nothing, binaries are stale
git stash pop
```

### Preferred Alternative

If you only need to check whether a test existed and passed before your changes, prefer:

```bash
# Check if the test file existed in the previous commit
git show HEAD:<path-to-test-file>

# Or run tests from a clean baseline commit
git stash
dotnet test --filter "FullyQualifiedName~TestName"
git stash pop
```

Note: `dotnet test` without `--no-build` will rebuild automatically, which is correct after a stash.

## Rule 2: Record every flaky test you find

When a test fails and then passes without any code change between the runs, you MUST append an entry to `backlog/flaky-tests.yaml` before moving on. This is required even when you are not fixing it, and even when the flake is obviously unrelated to what you are working on.

Fixing it is optional. Recording it is not.

### Why

A flake found mid-change is a distraction from that change, so leaving it unfixed is usually the right call. But leaving it *unrecorded* is not: the next person to hit it re-diagnoses it from scratch, and every unexplained red run erodes the signal the suite exists to carry. The register turns a recurring mystery into a known, closed question.

### What counts

- A test that fails on one run and passes on a rerun with no intervening edit.
- A test that fails in the full suite but passes under `--filter` in isolation.
- A property-based test that fails only on some random draws.
- A test whose failure depends on run order, parallelism, wall-clock time, or machine state.

A test that fails consistently is not flaky. It is either a real defect or a stale baseline, and it gets fixed rather than recorded.

### What to write

Append to the `flakes:` list in `backlog/flaky-tests.yaml`, following the field documentation at the top of that file. Two of those fields carry most of the value:

- **cause** — `unknown` is an acceptable answer. A guess dressed up as a cause is not, because it stops the next person from looking.
- **evidence** — how you concluded it was flaky and not yours. Enough that the diagnosis can be re-checked rather than re-derived. Name the files your change touched, note that the test file was unmodified, and record that the rerun was green.

### Establishing it is not yours

Before recording, confirm the test file is unmodified in your working tree and that the failure does not reproduce:

```bash
git status --short <path-to-test-file>
dotnet test --filter "FullyQualifiedName~TestName"
```

If the test file IS modified by your change, it is not a flake you found -- it is a flake you introduced, and it gets fixed.

## Applies To

- Any full-suite or filtered test run during a change
- Any investigation of whether a failure is pre-existing
- Verification steps before presenting results or committing

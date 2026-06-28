# TODO: Mist-Native Test Runner

## Current State

`mist test` delegates to the backend's test runner. For the C# transpiler backend, this means:

1. Compile the project via the standard Mist pipeline (lex, parse, semantic analysis, type checking, C# emission)
2. Invoke `dotnet test` on the emitted .csproj
3. Stream stdout/stderr back to the user and return dotnet's exit code

This works today and supports xUnit, NUnit, MSTest, or any test framework that `dotnet test` discovers via reflection on `[Fact]`, `[Test]`, `[TestMethod]`, etc.

## Future Goal

A Mist-native test runner is desired but out of scope for v1.

## Rationale for Deferring

- **No annotation system was available** until attributes were added. Without `[Fact]`-style decorators, there was no way to mark methods as tests in Mist source.
- **A native runner requires more than discovery.** Even with attributes in place, a useful test runner needs assertion APIs, result formatting, and tooling integration — none of which exist yet.
- **The `dotnet test` delegation is correct and complete.** It supports parameterized tests, parallel execution, filtering, and TRX output today. There is no user-facing gap that blocks adoption.

## What a Native Test Runner Would Need

### Test Discovery
- Scan compiled assemblies (or Mist source directly) for methods decorated with test-attributed markers
- Support filtering by name, trait, category
- Handle parameterized tests (equivalent to `[Theory]` + `[InlineData]`)

### Assertion Library
- `Assert.Equal(expected, actual)` with structural comparison
- `Assert.Throws<T>(action)` for exception testing
- `Assert.True` / `Assert.False` with optional messages
- Collection assertions (`Assert.Contains`, `Assert.Empty`, etc.)
- Decide whether assertions are a stdlib module or a separate package

### Result Reporting Format
- Console summary (pass/fail counts, duration, failure details)
- TRX (Visual Studio Test Results) for CI integration
- JUnit XML for cross-platform CI systems
- Machine-readable JSON for tooling consumption

### IDE Integration
- VS Code Test Explorer adapter (discover tests, show pass/fail inline)
- CodeLens-style "Run Test" / "Debug Test" links above test methods
- Real-time result streaming during test execution
- Source mapping from failures back to Mist line numbers (not emitted C# lines)

## Open Questions

1. Should the native runner operate on Mist source (interpret/compile on-the-fly) or on compiled output (inspect the emitted assembly)?
2. Should Mist assertions be expressions that return a result type, or statements that throw on failure (like xUnit)?
3. How does the native runner coexist with `dotnet test` during the transition? Can both be invoked from `mist test` via a flag?

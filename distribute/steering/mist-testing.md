<!-- MIST-MANAGED-STEERING: installed by `mist steering`. Do not edit — this file is overwritten on every reinstall. Do not add this marker to your own files; any file containing it is treated as Mist-managed and may be deleted. -->

# Mist Testing

How to set up a test project, write test methods with xUnit attributes, and run them with `mist test`. Include this when adding tests, configuring test projects, or diagnosing test runner issues.

## Setting Up a Test Project

A test project is a normal Mist project with two additions in `mist.toml`:

1. Set `output_type = "library"` so no Main entry point is required.
2. Add xUnit packages to the `[packages]` table.

```toml
name = "MyTests"
output_type = "library"

[packages]
xunit = "2.9.3"
xunit.runner.visualstudio = "3.0.2"
Microsoft.NET.Test.Sdk = "17.12.0"
```

The three packages serve distinct roles:

- **xunit** — the test framework itself (`Assert`, `[Fact]`, `[Theory]`, etc.)
- **xunit.runner.visualstudio** — the test adapter that lets `dotnet test` discover and run xUnit tests
- **Microsoft.NET.Test.Sdk** — the MSTest platform infrastructure that `dotnet test` requires

All three are needed for `dotnet test` to find and execute tests.

## Writing Test Methods

Import the `Xunit` namespace and decorate methods with test attributes:

```mist
using Xunit;

class MathTests
{
    [Fact]
    public void AdditionWorks()
    {
        var result = 2 + 2;
        Assert.Equal(4, result);
    }

    [Fact]
    public void SubtractionWorks()
    {
        var result = 10 - 3;
        Assert.Equal(7, result);
    }
}
```

### Parameterized Tests

Use `[Theory]` with one or more `[InlineData(...)]` attributes to run the same test with different inputs:

```mist
using Xunit;

class ParameterizedTests
{
    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(0, 0, 0)]
    [InlineData(-1, 1, 0)]
    public void Add(int a, int b, int expected)
    {
        var result = a + b;
        Assert.Equal(expected, result);
    }
}
```

### Common Assertions

xUnit assertions available from `Assert`:

- `Assert.Equal(expected, actual)` — value equality
- `Assert.True(condition)` / `Assert.False(condition)` — boolean checks
- `Assert.Contains(substring, str)` — string containment
- `Assert.Throws<T>(() => ...)` — exception verification
- `Assert.NotEqual(expected, actual)` — value inequality

## Running Tests with `mist test`

```bash
mist test               # compile and run all tests
mist test --verbose     # show package resolution detail during compilation
mist test --output ./out  # use a custom output directory
```

### How It Works

1. **Compiles the project** using the standard Mist compilation pipeline (lex, parse, semantic analysis, type checking, C# emission). The project's `output_type` is respected — the .csproj is emitted as Library or Exe per the normal rules.
2. **Invokes `dotnet test`** on the emitted .csproj in the output directory.
3. **Streams output** from `dotnet test` to your terminal in real time (both stdout and stderr).
4. **Returns the exit code** from `dotnet test` as the CLI exit code (0 = all tests pass, non-zero = failures).

If compilation fails, diagnostics are printed and `dotnet test` is not invoked (exit code 1).

### Works on Any Project

`mist test` works regardless of the project's `output_type`. If the project has no test-attributed methods, the test runner reports zero tests found and exits with code 0. This means you can run `mist test` on an executable project that happens to include test methods.

### Missing Package Warning

If the compiler detects methods decorated with test-like attributes (`[Fact]`, `[Theory]`, `[Test]`, `[TestMethod]`) but no test framework package is declared in `mist.toml`, it emits warning `MIST_TEST_0001` suggesting that test framework packages are likely missing.

## Example: Complete Test Project

**mist.toml:**
```toml
name = "MyApp.Tests"
output_type = "library"

[packages]
xunit = "2.9.3"
xunit.runner.visualstudio = "3.0.2"
Microsoft.NET.Test.Sdk = "17.12.0"
```

**Tests.mist:**
```mist
namespace MyApp.Tests;

using Xunit;

class CalculatorTests
{
    [Fact]
    public void AddReturnsSum()
    {
        var result = 2 + 3;
        Assert.Equal(5, result);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(100, 200, 300)]
    public void AddParameterized(int a, int b, int expected)
    {
        Assert.Equal(expected, a + b);
    }
}
```

**Run:**
```bash
mist test
```

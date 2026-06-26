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

# Test UTF-8 Encoding Policy

## Rule

All tests that spawn a compiled Mist program as a child process and read its stdout MUST configure both sides for UTF-8 encoding:

1. **Reader side (test process):** Set `StandardOutputEncoding = System.Text.Encoding.UTF8` on `ProcessStartInfo`.
2. **Writer side (compiled program):** The Mist compiler emits `System.Console.OutputEncoding = System.Text.Encoding.UTF8;` as the first statement of every entry point (`Main` method or top-level statements). This is already handled by the compiler — no test-level action needed.

## Why

On Windows, when stdout is redirected to a pipe (as in test process spawning), .NET defaults `Console.OutputEncoding` to the system's active code page (e.g., 437 or 1252). Characters outside that code page — such as em dashes (U+2014), non-ASCII Unicode, or any character above 0x7F — get "best fit" mapped to ASCII approximations (e.g., `—` becomes `-`) or replaced with `?`.

This means a program that prints Unicode via `Console.WriteLine` will silently produce wrong output when run as a child process without UTF-8 encoding configured, even though the same program works correctly when run interactively or through the Mist CLI (which sets `Console.OutputEncoding = UTF8` at startup).

## Approved Pattern

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

## Forbidden Pattern

```csharp
// BAD: Missing encoding — will corrupt non-ASCII output on Windows
var psi = new ProcessStartInfo
{
    FileName = "dotnet",
    Arguments = dllPath,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true,
};
```

## Compiler Guarantee

The Mist C# emitter (`CSharpEmitter.cs`) injects `System.Console.OutputEncoding = System.Text.Encoding.UTF8;` at the start of:
- Every `static void Main()` method (non-referenced projects only)
- Every top-level statement entry point (non-referenced projects only)

This ensures compiled Mist programs always output UTF-8 regardless of the host platform's default console encoding, matching the CLI's behavior.

## Applies To

- All test files in `Test/Mist.Tests/` that spawn processes and read stdout
- All integration tests that compile Mist code and run the resulting DLL

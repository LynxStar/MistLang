// Task 8.3: Emit pattern matching on union types
// Validates: Requirements 10.6, 11.1, 11.2, 11.3

using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Integration tests verifying that the C# emitter correctly emits pattern matching
/// on union types using the Is{T}/As{T} property pattern instead of C#'s native
/// `is T t` pattern.
///
/// Uses the batched pattern: 1 compile, 1 process spawn, N output lines compared.
///
/// **Validates: Requirements 10.6, 11.1, 11.2, 11.3**
/// </summary>
public sealed class UnionPatternMatchingEmissionTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string outputDir;

    public UnionPatternMatchingEmissionTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "mist_union_pattern_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Mist program source ─────────────────────────────────────────────

    /// <summary>
    /// Builds a Mist program that exercises pattern matching on union types:
    /// - `is T t` type pattern on union → checks Is{T}, extracts via As{T} (Req 10.6, 11.1, 11.2)
    /// - `is var v` var pattern on union → binds the helper type instance directly (Req 11.3)
    /// </summary>
    private static string BuildMistProgram()
    {
        return """
            using Standard;

            namespace TestProgram;

            public class Program {
                public static void CheckInt(int | string value) {
                    if (value is int n) {
                        Console.WriteLine("int_match");
                    } else {
                        Console.WriteLine("not_int");
                    }
                }

                public static void CheckString(int | string value) {
                    if (value is string s) {
                        Console.WriteLine(s);
                    } else {
                        Console.WriteLine("not_string");
                    }
                }

                public static void CheckVar(int | string value) {
                    if (value is var v) {
                        Console.WriteLine("var_bound");
                    }
                }

                public static void Main(string[] args) {
                    int | string x = 42;
                    int | string y = "hello";

                    CheckInt(x);
                    CheckInt(y);
                    CheckString(x);
                    CheckString(y);
                    CheckVar(x);
                    CheckVar(y);
                }
            }
            """;
    }

    private static readonly string[] ExpectedLines =
    [
        "int_match",    // CheckInt(42) → is int n matches
        "not_int",      // CheckInt("hello") → is int n does not match
        "not_string",   // CheckString(42) → is string s does not match
        "hello",        // CheckString("hello") → is string s matches, prints s
        "var_bound",    // CheckVar(42) → is var v always matches
        "var_bound",    // CheckVar("hello") → is var v always matches
    ];

    // ─── Compile and run helper ──────────────────────────────────────────

    private (int ExitCode, string Stdout, string Stderr) CompileAndRun(string mistSource)
    {
        var iterDir = Path.Combine(outputDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iterDir);

        try
        {
            var sourcePath = Path.Combine(iterDir, "Program.mist");
            File.WriteAllText(sourcePath, mistSource);

            var binDir = Path.Combine(iterDir, "bin");
            var options = new CompilerOptions(
                ProjectFolder: iterDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: iterDir,
                BinDirectory: binDir,
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            var result = SharedDriver.Compile(options);

            if(!result.Succeeded || result.PrimaryOutputArtifactPath is null)
            {
                var diagnosticText = string.Join("\n",
                    result.Diagnostics.Select(d => $"  {d.Severity} {d.Id.Value}: {d.Message}"));
                return (-1, "", $"Compilation failed:\n{diagnosticText}");
            }

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = result.PrimaryOutputArtifactPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if(process is null)
            {
                return (-1, "", "Failed to start dotnet process");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if(!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return (-1, "", "Process timed out after 30 seconds");
            }

            return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        finally
        {
            try { Directory.Delete(iterDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Emitted C# content verification ─────────────────────────────────

    private string CompileAndGetEmittedCSharp(string mistSource)
    {
        var iterDir = Path.Combine(outputDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iterDir);

        try
        {
            var sourcePath = Path.Combine(iterDir, "Program.mist");
            File.WriteAllText(sourcePath, mistSource);

            var binDir = Path.Combine(iterDir, "bin");
            var options = new CompilerOptions(
                ProjectFolder: iterDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: iterDir,
                BinDirectory: binDir,
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            SharedDriver.Compile(options);

            // Read the emitted .cs file
            var csFile = Directory.GetFiles(iterDir, "Program.cs").FirstOrDefault();
            return csFile is not null ? File.ReadAllText(csFile) : "";
        }
        finally
        {
            try { Directory.Delete(iterDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that pattern matching on union types compiles and runs correctly.
    /// - `is T t` checks the discriminator tag and extracts the typed value
    /// - `is var v` binds the helper type instance directly
    ///
    /// Batched: 1 compile, 1 process spawn, 6 output lines compared.
    ///
    /// **Validates: Requirements 10.6, 11.1, 11.2, 11.3**
    /// </summary>
    [Fact]
    public void UnionPatternMatching_CompilesAndRunsCorrectly()
    {
        var mistSource = BuildMistProgram();
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        Assert.Equal(ExpectedLines.Length, lines.Length);
        for(var i = 0; i < ExpectedLines.Length; i++)
        {
            Assert.True(lines[i] == ExpectedLines[i],
                $"Line {i} mismatch: expected '{ExpectedLines[i]}', got '{lines[i]}'");
        }
    }

    /// <summary>
    /// Verifies that the emitted C# source contains the Is{T}/As{T} pattern
    /// for type patterns on union types, rather than C#'s native `is T t`.
    ///
    /// **Validates: Requirements 10.6, 11.1, 11.2**
    /// </summary>
    [Fact]
    public void UnionPatternMatching_EmittedCSharp_UsesIsAsProperties()
    {
        var mistSource = BuildMistProgram();
        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        // Type pattern `is int n` should emit `value.IsInt32` condition
        Assert.Contains(".IsInt32", emittedCSharp);
        // Type pattern `is string s` should emit `value.IsString` condition
        Assert.Contains(".IsString", emittedCSharp);

        // Variable extraction should use As{T} properties
        Assert.Contains(".AsInt32", emittedCSharp);
        Assert.Contains(".AsString", emittedCSharp);

        // Should NOT contain C#'s native `is int` or `is string` pattern on union values
        // (The helper struct doesn't support C#'s native pattern matching)
        Assert.DoesNotContain("is int ", emittedCSharp);
        Assert.DoesNotContain("is string ", emittedCSharp);
    }
}
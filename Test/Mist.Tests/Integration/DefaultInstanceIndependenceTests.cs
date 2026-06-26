// Task 12.10 of the default-keyword spec: batched end-to-end test that
// default instances with reference-type fields are independent.
// Design § *Correctness Properties → Property 16*:
//
//   For any defaultable type (class or struct) that contains at least one
//   reference-type field, two separate evaluations of default(TypeName)
//   SHALL produce instances where the reference-type fields point to
//   distinct objects, and each instance's field values SHALL equal the
//   computed defaults.
//
// **Validates: Requirements 12.4, 12.9**

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

namespace Mist.Tests.Integration;

/// <summary>
/// Batched end-to-end test for Property 16: default instance independence
/// for types with reference fields.
///
/// Builds a single Mist program that creates multiple default instances of
/// classes and structs (including structs with reference-type fields),
/// compiles once via shared <see cref="CompilerDriver"/>, runs the compiled
/// output once, and verifies output values and that reference-type fields
/// in independently-created default instances are distinct.
///
/// Uses batching pattern: 1 compile, 1 process spawn, N output lines compared.
///
/// **Validates: Requirements 12.4, 12.9**
/// </summary>
public sealed class DefaultInstanceIndependenceTests : IDisposable
{
    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string outputDir;

    public DefaultInstanceIndependenceTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "mist_default_independence_" + Guid.NewGuid().ToString("N"));
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
    /// Builds a single Mist program that:
    /// 1. Defines an Inner class with an int field (value)
    /// 2. Defines an Outer class with an Inner field (reference-type field) and an int field
    /// 3. Defines a Wrapper struct with an Inner field (struct with reference-type field) and an int field
    /// 4. Creates two default instances of Outer and two of Wrapper
    /// 5. Verifies default field values are correct
    /// 6. Mutates one instance's inner field and checks the other is unaffected
    ///    (proves reference independence — if they shared the same Inner, mutation would propagate)
    /// </summary>
    private static string BuildMistProgram()
    {
        return """
            using Standard;

            namespace TestProgram;

            public class Inner {
                public int value;
            }

            public class Outer {
                public Inner inner;
                public int count;
            }

            public struct Wrapper {
                public Inner inner;
                public int tag;
            }

            public class Program {
                public static void Main(string[] args) {
                    Outer a = default(Outer);
                    Outer b = default(Outer);

                    Wrapper wa = default(Wrapper);
                    Wrapper wb = default(Wrapper);

                    // Line 0: a.inner.value starts at default (0)
                    Console.WriteLine(a.inner.value);

                    // Line 1: b.inner.value starts at default (0)
                    Console.WriteLine(b.inner.value);

                    // Line 2: a.count starts at default (0)
                    Console.WriteLine(a.count);

                    // Line 3: b.count starts at default (0)
                    Console.WriteLine(b.count);

                    // Mutate a.inner.value to prove independence
                    a.inner.value = 42;

                    // Line 4: a.inner.value is now 42
                    Console.WriteLine(a.inner.value);

                    // Line 5: b.inner.value is still 0 (independent reference)
                    Console.WriteLine(b.inner.value);

                    // Line 6: wa.inner.value starts at default (0)
                    Console.WriteLine(wa.inner.value);

                    // Line 7: wb.inner.value starts at default (0)
                    Console.WriteLine(wb.inner.value);

                    // Line 8: wa.tag starts at default (0)
                    Console.WriteLine(wa.tag);

                    // Line 9: wb.tag starts at default (0)
                    Console.WriteLine(wb.tag);

                    // Mutate wa.inner.value to prove struct wrapper independence
                    wa.inner.value = 99;

                    // Line 10: wa.inner.value is now 99
                    Console.WriteLine(wa.inner.value);

                    // Line 11: wb.inner.value is still 0 (independent reference)
                    Console.WriteLine(wb.inner.value);
                }
            }
            """;
    }

    // ─── Expected output ─────────────────────────────────────────────────

    private static readonly string[] ExpectedLines =
    [
        "0",   // Line 0:  a.inner.value == 0 (default)
        "0",   // Line 1:  b.inner.value == 0 (default)
        "0",   // Line 2:  a.count == 0 (default)
        "0",   // Line 3:  b.count == 0 (default)
        "42",  // Line 4:  a.inner.value == 42 (mutated)
        "0",   // Line 5:  b.inner.value == 0 (independent — proves distinct references)
        "0",   // Line 6:  wa.inner.value == 0 (default)
        "0",   // Line 7:  wb.inner.value == 0 (default)
        "0",   // Line 8:  wa.tag == 0 (default)
        "0",   // Line 9:  wb.tag == 0 (default)
        "99",  // Line 10: wa.inner.value == 99 (mutated)
        "0",   // Line 11: wb.inner.value == 0 (independent — proves distinct references)
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

    // ─── Test ────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 16: Default instance independence for types with reference fields.
    ///
    /// Two separate evaluations of default(TypeName) produce instances where
    /// reference-type fields point to distinct objects, and each instance's
    /// field values equal the computed defaults. Independence is verified by
    /// mutating one instance's reference-type field and confirming the other
    /// instance is unaffected.
    ///
    /// Batched: 1 compile, 1 process spawn, 12 output lines compared.
    ///
    /// **Validates: Requirements 12.4, 12.9**
    /// </summary>
    [Fact]
    public void DefaultInstances_WithReferenceFields_AreIndependent()
    {
        var mistSource = BuildMistProgram();
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.None);

        Assert.True(lines.Length == ExpectedLines.Length,
            $"Expected {ExpectedLines.Length} output lines but got {lines.Length}.\n" +
            $"Stdout:\n{stdout}");

        for(var i = 0; i < ExpectedLines.Length; i++)
        {
            var actual = lines[i].TrimEnd('\r');
            Assert.True(ExpectedLines[i] == actual,
                $"Line {i} mismatch.\n" +
                $"Expected: '{ExpectedLines[i]}'\n" +
                $"Actual:   '{actual}'");
        }
    }
}
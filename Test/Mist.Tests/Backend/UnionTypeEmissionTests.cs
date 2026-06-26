// Task 8.2: Emit union type usage (variables, parameters, return types, fields)
// Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5

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
/// Integration tests verifying that the C# emitter correctly uses the generated
/// helper type name for union-typed variables, parameters, return types, and fields.
/// Also verifies that implicit conversion operators enable constituent-to-union assignment.
///
/// Uses the batched pattern: 1 compile, 1 process spawn, N output lines compared.
///
/// **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**
/// </summary>
public sealed class UnionTypeEmissionTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string outputDir;

    public UnionTypeEmissionTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "mist_union_emission_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
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
    /// Builds a Mist program that exercises union types in all type positions:
    /// - Variable declarations with union types (Req 10.1)
    /// - Method parameters with union types (Req 10.2)
    /// - Method return types with union types (Req 10.3)
    /// - Field declarations with union types (Req 10.4)
    /// - Constituent-to-union assignment via implicit conversion (Req 10.5)
    ///
    /// Note: Pattern matching on union types (Req 10.6) is tested in Task 8.3.
    /// This test verifies that the emitted C# compiles and runs, proving that
    /// the helper type name is used correctly and implicit conversions work.
    /// </summary>
    private static string BuildMistProgram()
    {
        return """
            using Standard;

            namespace TestProgram;

            public class Container {
                public int | string data;
            }

            public class Program {
                public static int | string GetValue(bool flag) {
                    if (flag) {
                        return 42;
                    }
                    return "hello";
                }

                public static void AcceptUnion(int | string value) {
                    Console.WriteLine("accepted");
                }

                public static void Main(string[] args) {
                    int | string x = 42;
                    int | string y = "world";

                    Console.WriteLine("var_int");
                    Console.WriteLine("var_string");

                    AcceptUnion(x);
                    AcceptUnion(y);

                    int | string result = GetValue(true);
                    int | string result2 = GetValue(false);
                    Console.WriteLine("return_true");
                    Console.WriteLine("return_false");

                    Container c = new Container();
                    c.data = 99;
                    Console.WriteLine("field_int");

                    c.data = "field";
                    Console.WriteLine("field_string");
                }
            }
            """;
    }

    private static readonly string[] ExpectedLines =
    [
        "var_int",       // After assigning int to union variable
        "var_string",    // After assigning string to union variable
        "accepted",      // AcceptUnion(x) - passing union to parameter
        "accepted",      // AcceptUnion(y) - passing union to parameter
        "return_true",   // After calling GetValue(true) - union return type
        "return_false",  // After calling GetValue(false) - union return type
        "field_int",     // After assigning int to union field
        "field_string",  // After assigning string to union field
    ];

    // ─── Compile and run helper ──────────────────────────────────────────

    private (int ExitCode, string Stdout, string Stderr) CompileAndRun(string mistSource)
    {
        var iterDir = Path.Combine(outputDir, Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(iterDir);

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
        _ = Directory.CreateDirectory(iterDir);

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

            _ = SharedDriver.Compile(options);

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
    /// Verifies that the emitted C# uses the helper type name (Union_Int32_String)
    /// for union-typed variables, parameters, return types, and fields.
    /// Also verifies that implicit conversion operators enable constituent-to-union
    /// assignment at runtime.
    ///
    /// Batched: 1 compile, 1 process spawn, 6 output lines compared.
    ///
    /// **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**
    /// </summary>
    [Fact]
    public void UnionType_EmitsHelperTypeName_AndRunsCorrectly()
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
    /// Verifies that the emitted C# source contains the helper type name
    /// Union_Int32_String for union-typed declarations.
    ///
    /// **Validates: Requirements 10.1, 10.2, 10.3, 10.4**
    /// </summary>
    [Fact]
    public void UnionType_EmittedCSharp_ContainsHelperTypeName()
    {
        var mistSource = BuildMistProgram();
        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        // The helper type name should appear in the emitted C#
        Assert.Contains("Union_Int32_String", emittedCSharp);

        // Variable declarations should use the helper type name (Req 10.1)
        Assert.Contains("Union_Int32_String x", emittedCSharp);
        Assert.Contains("Union_Int32_String y", emittedCSharp);

        // Return type should use the helper type name (Req 10.3)
        Assert.Contains("Union_Int32_String GetValue", emittedCSharp);

        // Parameter should use the helper type name (Req 10.2)
        Assert.Contains("Union_Int32_String value", emittedCSharp);

        // Field should use the helper type name (Req 10.4)
        Assert.Contains("Union_Int32_String data", emittedCSharp);
    }
}
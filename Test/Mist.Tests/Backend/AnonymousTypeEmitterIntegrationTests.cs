// Task 8.3: Verify emission for property access and pattern matching on anonymous types.
//
// This test verifies that the generic emission paths correctly handle:
// 1. Property access on anonymous type values → `anonValue.Name`
// 2. Pattern matching with property patterns on anonymous types → C# property patterns
// 3. Assignment to `unknown` → standard `object` lowering
//
// **Validates: Requirements 7.4, 8.1, 8.2, 9.2**

using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Integration test verifying that anonymous type property access, pattern matching,
/// and unknown assignment emit correct C# and produce expected runtime output.
///
/// Uses the batched approach: 1 in-process compile + 1 process run.
///
/// **Validates: Requirements 7.4, 8.1, 8.2, 9.2**
/// </summary>
public sealed class AnonymousTypeEmitterIntegrationTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public AnonymousTypeEmitterIntegrationTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "mist_anon_emit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Compiles a Mist program that exercises:
    /// 1. Property access on anonymous types (anonValue.Name, anonValue.Value)
    /// 2. Assignment to unknown (unknown box = new { ... })
    /// 3. Pattern matching with property patterns on anonymous types
    ///
    /// Verifies:
    /// - Compilation succeeds without diagnostics
    /// - Emitted C# contains standard member access (not special intrinsic forms)
    /// - Emitted C# uses `object` for `unknown`-typed variable
    /// - Running the compiled DLL produces expected stdout
    ///
    /// **Validates: Requirements 7.4, 8.1, 8.2, 9.2**
    /// </summary>
    [Fact]
    public void AnonymousType_PropertyAccessAndPatternMatching_EmitsCorrectCSharp()
    {
        const string mistSource = """
            using Standard;

            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    // Property access on anonymous type
                    var x = new { Name = "test", Value = 42 };
                    Console.WriteLine(x.Name);
                    Console.WriteLine($"{x.Value}");

                    // Assignment to unknown (emits as object)
                    unknown box = new { X = 10, Y = 20 };
                    Console.WriteLine("boxed");

                    // Pattern matching with property patterns on unknown
                    if (box is { X: var xVal, Y: var yVal })
                    {
                        Console.WriteLine($"X={xVal} Y={yVal}");
                    }
                }
            }
            """;

        var sourcePath = Path.Combine(_outputDir, "Program.mist");
        File.WriteAllText(sourcePath, mistSource);

        var binDir = Path.Combine(_outputDir, "bin");
        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: binDir,
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        // Act: compile in-process
        var result = SharedDriver.Compile(options);

        // Assert: compilation succeeded without diagnostics
        Assert.True(result.Succeeded,
            $"Compilation failed. Diagnostics:\n{string.Join("\n", result.Diagnostics.Select(d => $"  {d.Severity} {d.Id.Value}: {d.Message}"))}");
        // Assert: emitted C# source contains expected patterns
        var csFiles = Directory.GetFiles(_outputDir, "*.cs");
        Assert.NotEmpty(csFiles);

        var emittedSource = string.Join("\n", csFiles.Select(File.ReadAllText));

        // Property access emits as standard member access
        Assert.Contains("x.Name", emittedSource);
        Assert.Contains("x.Value", emittedSource);

        // unknown lowered to object
        Assert.Contains("object box", emittedSource);

        // Anonymous type expression emitted as C# anonymous type
        Assert.Contains("new {", emittedSource);

        // Act: run the compiled DLL (1 process spawn per test-process-spawning policy)
        Assert.NotNull(result.PrimaryOutputArtifactPath);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = result.PrimaryOutputArtifactPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(30_000), "Process timed out after 30 seconds");
        Assert.True(process.ExitCode == 0,
            $"Program failed (exit code {process.ExitCode}).\nStderr:\n{stderr}");

        // Assert: correct stdout output
        var lines = stdout.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        Assert.Equal(4, lines.Length);
        Assert.Equal("test", lines[0]);
        Assert.Equal("42", lines[1]);
        Assert.Equal("boxed", lines[2]);
        Assert.Equal("X=10 Y=20", lines[3]);
    }
}
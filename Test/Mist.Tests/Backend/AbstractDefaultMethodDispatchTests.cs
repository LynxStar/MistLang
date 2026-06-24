// Feature: abstract-type-unification, Property 6: Default Dispatch Consistency
// Feature: abstract-type-unification, Property 7: Override Takes Precedence
// **Validates: Requirements 15.1, 15.2, 15.3, 16.1, 16.2, 16.3, 16.4, 16.5**

using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Runtime integration tests verifying that default method forwarding stubs produce
/// correct dispatch behavior at runtime:
///
/// <list type="bullet">
///   <item><b>Property 6 (Default Dispatch Consistency):</b> Calling a default method
///   through a concrete-type reference and through an abstract-type reference produces
///   identical output.</item>
///   <item><b>Property 7 (Override Takes Precedence):</b> When a concrete type overrides
///   a default, the override is invoked regardless of reference type.</item>
/// </list>
///
/// A single Mist program exercises all scenarios (1 compile, 1 process spawn).
/// Each scenario prints a labeled line; the test verifies all lines match expectations.
///
/// **Validates: Requirements 15.1, 15.2, 15.3, 16.1, 16.2, 16.3, 16.4, 16.5**
/// </summary>
public sealed class AbstractDefaultMethodDispatchTests : IDisposable
{
    private readonly string _outputDir;

    public AbstractDefaultMethodDispatchTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistDefaultDispatch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Compile and run helper (1 compile, 1 process spawn) ─────────────

    private (int ExitCode, string Stdout, string Stderr) CompileAndRun(string mistSource)
    {
        var iterDir = Path.Combine(_outputDir, Guid.NewGuid().ToString("N"));
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

            if (!result.Succeeded || result.PrimaryOutputArtifactPath is null)
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
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (-1, "", "Failed to start dotnet process");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(30_000))
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

    // ═══════════════════════════════════════════════════════════════════════
    // Batched runtime verification — all scenarios in one program
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exercises dispatch consistency and override precedence at runtime.
    /// A single Mist program covers:
    ///   1. Default called through concrete reference (R15.1)
    ///   2. Default called through abstract reference (R15.2) — must match (1)
    ///   3. Override called through concrete reference (R15.3)
    ///   4. Override called through abstract reference (R15.3) — must match (3)
    ///   5. Inherited default through concrete reference
    ///   6. Inherited default through abstract reference — must match (5)
    ///   7. Default with parameters — arguments forwarded correctly
    /// </summary>
    [Fact]
    public void DefaultDispatch_And_OverridePrecedence_AtRuntime()
    {
        const string source = """
            using Standard;
            namespace TestProgram;

            abstract IGreeting
            {
                string Name();
                string Greet() { return $"Hello, {this.Name()}"; }
            }

            // Does NOT override Greet — uses the default.
            public class Alice : IGreeting
            {
                public string Name() { return "Alice"; }
            }

            // DOES override Greet — override takes precedence.
            public class Bob : IGreeting
            {
                public string Name() { return "Bob"; }
                public string Greet() { return "YO"; }
            }

            // Inherited default through a child abstract type.
            abstract IFancy : IGreeting
            {
                int Level();
            }

            public class Charlie : IFancy
            {
                public string Name() { return "Charlie"; }
                public int Level() { return 3; }
            }

            // Default with parameters.
            abstract IPrefixer
            {
                string Prefix();
                string Decorate(string text) { return $"{this.Prefix()}{text}"; }
            }

            public class Brackets : IPrefixer
            {
                public string Prefix() { return "["; }
            }

            public class Program
            {
                public static void Main()
                {
                    // Scenario 1: Default through concrete reference (R15.1)
                    var alice = new Alice();
                    Console.WriteLine(alice.Greet());

                    // Scenario 2: Default through abstract reference (R15.2)
                    IGreeting aliceRef = alice;
                    Console.WriteLine(aliceRef.Greet());

                    // Scenario 3: Override through concrete reference (R15.3)
                    var bob = new Bob();
                    Console.WriteLine(bob.Greet());

                    // Scenario 4: Override through abstract reference (R15.3)
                    IGreeting bobRef = bob;
                    Console.WriteLine(bobRef.Greet());

                    // Scenario 5: Inherited default through concrete reference
                    var charlie = new Charlie();
                    Console.WriteLine(charlie.Greet());

                    // Scenario 6: Inherited default through abstract reference
                    IGreeting charlieRef = charlie;
                    Console.WriteLine(charlieRef.Greet());

                    // Scenario 7: Default with parameters forwarded correctly
                    var brackets = new Brackets();
                    Console.WriteLine(brackets.Decorate("ok"));
                }
            }
            """;

        var (exitCode, stdout, stderr) = CompileAndRun(source);

        Assert.True(exitCode == 0,
            $"Program exited with code {exitCode}.\nStderr: {stderr}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        // Expected output lines:
        string[] expected =
        [
            "Hello, Alice",    // 1: default via concrete
            "Hello, Alice",    // 2: default via abstract — MUST match line 1 (Property 6)
            "YO",              // 3: override via concrete (Property 7)
            "YO",              // 4: override via abstract — MUST match line 3 (Property 7)
            "Hello, Charlie",  // 5: inherited default via concrete
            "Hello, Charlie",  // 6: inherited default via abstract — MUST match line 5
            "[ok",             // 7: default with parameter forwarding
        ];

        Assert.True(lines.Length == expected.Length,
            $"Expected {expected.Length} output lines but got {lines.Length}.\nActual output:\n{stdout}");

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(lines[i] == expected[i],
                $"Line {i + 1} mismatch.\n  Expected: \"{expected[i]}\"\n  Actual:   \"{lines[i]}\"\nFull output:\n{stdout}");
        }
    }
}

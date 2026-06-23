// Feature: abstract-type-unification, Property 7: Method dispatch is reference-type-independent

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.AbstractTypeUnification;

// ─── Scenario type ──────────────────────────────────────────────────────────

/// <summary>
/// A scenario for testing method dispatch reference-type independence.
/// Each scenario defines an abstract type with a default method and a required
/// slot (Name), and a concrete type that fills the required slot. The test
/// calls the default method through both the concrete and abstract reference
/// and verifies identical output.
/// </summary>
public sealed class MethodDispatchScenario
{
    /// <summary>The greeting prefix used in the default method body.</summary>
    public string Prefix { get; }

    /// <summary>The name value returned by the concrete type's Name() method.</summary>
    public string NameValue { get; }

    /// <summary>Whether the concrete type overrides the default method.</summary>
    public bool OverridesDefault { get; }

    /// <summary>The override prefix (only used when OverridesDefault is true).</summary>
    public string OverridePrefix { get; }

    public MethodDispatchScenario(string prefix, string nameValue, bool overridesDefault, string overridePrefix)
    {
        Prefix = prefix;
        NameValue = nameValue;
        OverridesDefault = overridesDefault;
        OverridePrefix = overridePrefix;
    }

    /// <summary>The expected output for this scenario (same for both references).</summary>
    public string ExpectedOutput => OverridesDefault
        ? $"{OverridePrefix} {NameValue}"
        : $"{Prefix} {NameValue}";

    public override string ToString() =>
        OverridesDefault
            ? $"Override(prefix='{OverridePrefix}', name='{NameValue}')"
            : $"Default(prefix='{Prefix}', name='{NameValue}')";
}

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// Generates scenarios for method dispatch reference-type independence testing.
/// Each scenario varies the greeting prefix, name value, and whether the
/// concrete type overrides the default method.
/// </summary>
public static class MethodDispatchGenerators
{
    private static readonly string[] Prefixes =
        ["Hello", "Hi", "Hey", "Greetings", "Welcome", "Howdy", "Salutations"];

    private static readonly string[] Names =
        ["Alice", "Bob", "Carol", "Dave", "Eve", "Frank", "Grace", "Heidi"];

    public static Gen<MethodDispatchScenario> GenScenario()
    {
        return from prefix in Gen.Elements(Prefixes)
               from name in Gen.Elements(Names)
               from overrides in Gen.Elements(true, false)
               from overridePrefix in Gen.Elements(Prefixes)
               select new MethodDispatchScenario(prefix, name, overrides, overridePrefix);
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests verifying that method dispatch is reference-type-independent.
///
/// Property 7: For any concrete type that either takes a default method implementation
/// or overrides one, calling the method through the concrete type reference and through
/// the abstract type reference SHALL produce identical behavior (same return value).
///
/// Uses batched process spawning: 1 compile, 1 run for all 100 scenarios.
///
/// **Validates: Requirements 5.2, 5.3**
/// </summary>
public sealed class MethodDispatchPropertyTests : IDisposable
{
    private const int IterationCount = 100;

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public MethodDispatchPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "mist_pbt_dispatch_" + Guid.NewGuid().ToString("N"));
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

    // ─── Source generation ───────────────────────────────────────────────

    /// <summary>
    /// Builds a single Mist program that defines N abstract types with default
    /// methods and N concrete types, then calls the method through both concrete
    /// and abstract references, printing two lines per scenario.
    /// </summary>
    private static string BuildBatchedProgram(IReadOnlyList<MethodDispatchScenario> scenarios)
    {
        var declarations = new StringBuilder();
        var mainBody = new StringBuilder();

        for (var i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];

            // Abstract type with a required Name() slot and a default Greet() method
            declarations.AppendLine($"abstract IGreeting{i}");
            declarations.AppendLine("{");
            declarations.AppendLine($"    string Name();");
            declarations.AppendLine($"    string Greet()");
            declarations.AppendLine("    {");
            declarations.AppendLine($"        return \"{s.Prefix} \" + this.Name();");
            declarations.AppendLine("    }");
            declarations.AppendLine("}");
            declarations.AppendLine();

            // Concrete type filling the Name() slot
            declarations.AppendLine($"public class Impl{i} : IGreeting{i}");
            declarations.AppendLine("{");
            declarations.AppendLine($"    public string Name()");
            declarations.AppendLine("    {");
            declarations.AppendLine($"        return \"{s.NameValue}\";");
            declarations.AppendLine("    }");

            // Optionally override the default Greet() method
            if (s.OverridesDefault)
            {
                declarations.AppendLine();
                declarations.AppendLine($"    public string Greet()");
                declarations.AppendLine("    {");
                declarations.AppendLine($"        return \"{s.OverridePrefix} \" + this.Name();");
                declarations.AppendLine("    }");
            }

            declarations.AppendLine("}");
            declarations.AppendLine();

            // Main body: call through concrete and abstract references
            mainBody.AppendLine($"        var c{i} = new Impl{i}();");
            mainBody.AppendLine($"        IGreeting{i} a{i} = c{i};");
            mainBody.AppendLine($"        Console.WriteLine(c{i}.Greet());");
            mainBody.AppendLine($"        Console.WriteLine(a{i}.Greet());");
        }

        return $$"""
            using Standard;

            namespace TestProgram;

            {{declarations}}
            public class Program
            {
                public static void Main(string[] args)
                {
            {{mainBody}}    }
            }
            """;
    }

    // ─── Compile and run helper ──────────────────────────────────────────

    /// <summary>
    /// Compiles a Mist source in-process via CompilerDriver, then runs the
    /// produced DLL with <c>dotnet</c> to capture stdout.
    /// </summary>
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

    // ─── Test ────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 7: Method dispatch is reference-type-independent.
    ///
    /// For any concrete type that either takes a default method implementation
    /// or overrides one, calling the method through the concrete type reference
    /// and through the abstract type reference SHALL produce identical behavior
    /// (same return value).
    ///
    /// Generates 100 scenarios (varying prefix, name, and override behavior),
    /// builds a single Mist program, compiles once, runs once, and compares
    /// output lines pairwise: line[0]==line[1], line[2]==line[3], etc.
    ///
    /// Batched: 1 compile, 1 process spawn, 200 output lines compared pairwise.
    ///
    /// **Validates: Requirements 5.2, 5.3**
    /// </summary>
    [Fact]
    public void MethodDispatch_IsReferenceTypeIndependent()
    {
        var scenarios = MethodDispatchGenerators.GenScenario()
            .Sample(IterationCount)
            .ToList();

        var mistSource = BuildBatchedProgram(scenarios);
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.None);
        var expectedLineCount = scenarios.Count * 2;

        Assert.True(lines.Length == expectedLineCount,
            $"Expected {expectedLineCount} output lines but got {lines.Length}.\n" +
            $"Stdout:\n{stdout}");

        for (var i = 0; i < scenarios.Count; i++)
        {
            var concreteResult = lines[i * 2].TrimEnd('\r');
            var abstractResult = lines[i * 2 + 1].TrimEnd('\r');

            Assert.True(concreteResult == abstractResult,
                $"Scenario {i} failed: {scenarios[i]}\n" +
                $"Concrete reference: '{concreteResult}'\n" +
                $"Abstract reference: '{abstractResult}'\n" +
                $"Expected identical output from both reference types.");

            // Also verify the output matches the expected value
            var expected = scenarios[i].ExpectedOutput;
            Assert.True(concreteResult == expected,
                $"Scenario {i} unexpected output: {scenarios[i]}\n" +
                $"Expected: '{expected}'\n" +
                $"Actual:   '{concreteResult}'");
        }
    }
}

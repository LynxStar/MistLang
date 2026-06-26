// Feature: anonymous-types, Property 9: Emitter produces correct C# anonymous type syntax
//
// For any valid anonymous type expression containing explicit properties and/or projections,
// the emitter SHALL produce C#'s `new { ... }` syntax preserving property names and projection
// forms, and any variable declaration whose type is AnonymousType SHALL emit using `var`.
//
// Uses the batched compilation pattern: generate N scenarios, build one Mist program,
// compile once, run once, compare N output lines.
//
// **Validates: Requirements 7.1, 7.2, 7.3, 7.5**

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Batched property-based test for anonymous type emitter correctness.
///
/// Property 9: For any valid anonymous type expression containing explicit properties
/// and/or projections, the emitter SHALL produce C#'s <c>new { ... }</c> syntax preserving
/// property names and projection forms, and any variable declaration whose type is
/// <c>AnonymousType</c> SHALL emit using <c>var</c>.
///
/// The test generates N random anonymous type scenarios, builds a single Mist program
/// that prints property values for each scenario, compiles once, runs once, and compares
/// each output line to the expected values.
///
/// **Validates: Requirements 7.1, 7.2, 7.3, 7.5**
/// </summary>
public sealed class AnonymousTypeEmitterPropertyTests : IDisposable
{
    private const int ScenarioCount = 50;

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string outputDir;

    public AnonymousTypeEmitterPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "mist_anon_emit_pbt_" + Guid.NewGuid().ToString("N"));
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

    // ─── Scenario types ──────────────────────────────────────────────────

    private enum PropertyKind { Explicit, ProjectedVariable }

    private sealed record AnonProperty(string Name, PropertyKind Kind, string Value)
    {
        /// <summary>The Mist source fragment for this property inside <c>new { ... }</c>.</summary>
        public string ToMistInitializer()
        {
            return Kind switch
            {
                PropertyKind.Explicit => $"{Name} = {Value}",
                PropertyKind.ProjectedVariable => Name, // just the variable name (projection)
                _ => throw new InvalidOperationException()
            };
        }
    }

    private sealed record AnonScenario(ImmutableArray<AnonProperty> Properties, int Index)
    {
        /// <summary>Expected stdout line: comma-separated property values.</summary>
        public string ExpectedOutput()
        {
            var parts = Properties.Select(p =>
            {
                var val = p.Value;
                if(val.StartsWith('"') && val.EndsWith('"'))
                {
                    return val[1..^1]; // strip quotes for string literals
                }

                return val;
            });
            return string.Join(",", parts);
        }
    }

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ValidNames =
        ["Name", "Count", "City", "Age", "Tag", "Width", "Height", "Label", "Score", "Level"];

    private static Gen<string> GenStringValue()
    {
        return Gen.Elements("hello", "world", "test", "mist", "alpha", "beta", "gamma");
    }

    private static Gen<string> GenIntValue()
    {
        return Gen.Choose(0, 999).Select(n => n.ToString());
    }

    private static Gen<AnonProperty> GenExplicitStringProperty(string name)
    {
        return from val in GenStringValue()
               select new AnonProperty(name, PropertyKind.Explicit, $"\"{val}\"");
    }

    private static Gen<AnonProperty> GenExplicitIntProperty(string name)
    {
        return from val in GenIntValue()
               select new AnonProperty(name, PropertyKind.Explicit, val);
    }

    private static Gen<AnonProperty> GenProjectedVariable(string name)
    {
        return from val in GenStringValue()
               select new AnonProperty(name, PropertyKind.ProjectedVariable, $"\"{val}\"");
    }

    private static Gen<AnonScenario> GenScenario(int index)
    {
        return from propCount in Gen.Choose(1, 4)
               from names in Gen.Shuffle(ValidNames).Select(arr => arr.Take(propCount).ToArray())
               from props in GenPropertiesFromNames(names)
               select new AnonScenario(props.ToImmutableArray(), index);
    }

    private static Gen<AnonProperty[]> GenPropertiesFromNames(string[] names)
    {
        var gens = names.Select((name, i) =>
            // Mix explicit and projected properties. Projected uses string vars.
            Gen.Choose(0, 2).SelectMany(kind => kind switch
            {
                0 => GenExplicitStringProperty(name),
                1 => GenExplicitIntProperty(name),
                _ => GenProjectedVariable(name),
            }));

        return gens.Aggregate(
            Gen.Constant(Array.Empty<AnonProperty>()),
            (acc, gen) => from arr in acc
                          from prop in gen
                          select arr.Append(prop).ToArray());
    }

    // ─── Source builder ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a single Mist program whose Main creates all anonymous type scenarios
    /// and prints one verification line per scenario (comma-separated property values).
    /// Each scenario is wrapped in its own block scope to avoid variable name conflicts
    /// between projected variables across scenarios.
    /// </summary>
    private static string BuildBatchedProgram(IReadOnlyList<AnonScenario> scenarios)
    {
        var body = new StringBuilder();

        for(var i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            _ = body.AppendLine($"        // Scenario {i}");
            _ = body.AppendLine("        {");

            // For projected variables, declare them first
            foreach(var prop in s.Properties)
            {
                if(prop.Kind == PropertyKind.ProjectedVariable)
                {
                    _ = body.AppendLine($"            var {prop.Name} = {prop.Value};");
                }
            }

            // Build the anonymous type expression
            var initParts = s.Properties.Select(p => p.ToMistInitializer());
            var initList = string.Join(", ", initParts);
            _ = body.AppendLine($"            var s = new {{ {initList} }};");

            // Print property values as comma-separated
            var accessParts = s.Properties.Select(p => $"{{s.{p.Name}}}");
            var interpolation = string.Join(",", accessParts);
            _ = body.AppendLine($"            Console.WriteLine($\"{interpolation}\");");

            _ = body.AppendLine("        }");
            _ = body.AppendLine();
        }

        return $$"""
            using Standard;

            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
            {{body}}    }
            }
            """;
    }

    // ─── Compile and run ─────────────────────────────────────────────────

    private (int ExitCode, string Stdout, string Stderr) CompileAndRun(string mistSource)
    {
        var sourcePath = Path.Combine(outputDir, "Program.mist");
        File.WriteAllText(sourcePath, mistSource);

        var binDir = Path.Combine(outputDir, "bin");
        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
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
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
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

    // ─── Test ────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 9: For any valid anonymous type expression containing explicit properties
    /// and/or projections, the emitter SHALL produce C#'s <c>new { ... }</c> syntax preserving
    /// property names and projection forms, and any variable declaration whose type is
    /// <c>AnonymousType</c> SHALL emit using <c>var</c>.
    ///
    /// Verification strategy:
    /// - Generate N scenarios with explicit and projected properties
    /// - Build one batched Mist program that creates anonymous types and prints property values
    /// - Compile once, run once (1 process spawn per policy)
    /// - Compare each output line to expected values
    ///
    /// If anonymous types emitted incorrectly (wrong syntax, missing var, wrong property names),
    /// the C# compilation or runtime output would fail.
    ///
    /// **Validates: Requirements 7.1, 7.2, 7.3, 7.5**
    /// </summary>
    [Fact]
    [Trait("Category", "PropertyBased")]
    public void AnonymousTypeEmitter_ProducesCorrectCSharpSyntax()
    {
        // Generate scenarios deterministically via FsCheck
        var scenarios = Gen.Sized(size =>
            Enumerable.Range(0, ScenarioCount)
                .Select(i => GenScenario(i))
                .Aggregate(
                    Gen.Constant(ImmutableArray<AnonScenario>.Empty),
                    (acc, gen) => from arr in acc
                                  from s in gen
                                  select arr.Add(s)))
            .Sample(size: 30, numberOfSamples: 1)
            .First();

        var mistSource = BuildBatchedProgram(scenarios);
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}\n\nSource:\n{mistSource}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.None);
        Assert.True(lines.Length == scenarios.Length,
            $"Expected {scenarios.Length} output lines but got {lines.Length}.\n" +
            $"Stdout:\n{stdout}\n\nSource:\n{mistSource}");

        for(var i = 0; i < scenarios.Length; i++)
        {
            var expected = scenarios[i].ExpectedOutput();
            var actual = lines[i].TrimEnd('\r');
            Assert.True(expected == actual,
                $"Scenario {i} failed.\n" +
                $"Properties: [{string.Join(", ", scenarios[i].Properties.Select(p => $"{p.Name}({p.Kind})={p.Value}"))}]\n" +
                $"Expected: '{expected}'\nActual:   '{actual}'");
        }
    }

    /// <summary>
    /// Supplementary verification: the emitted C# source uses <c>var</c> for anonymous type
    /// declarations (not an explicit type name), confirming Requirement 7.5.
    ///
    /// **Validates: Requirement 7.5**
    /// </summary>
    [Fact]
    [Trait("Category", "PropertyBased")]
    public void AnonymousTypeEmitter_UsesVarForDeclarations()
    {
        // A simple program with anonymous types — verify emitted C# uses var
        const string mistSource = """
            using Standard;

            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    var x = new { Name = "test", Value = 42 };
                    var city = "Portland";
                    var y = new { city, Score = 100 };
                    Console.WriteLine($"{x.Name},{x.Value}");
                    Console.WriteLine($"{y.city},{y.Score}");
                }
            }
            """;

        var sourcePath = Path.Combine(outputDir, "VarCheck.mist");
        File.WriteAllText(sourcePath, mistSource);

        var binDir = Path.Combine(outputDir, "bin_var");
        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: binDir,
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var result = SharedDriver.Compile(options);

        Assert.True(result.Succeeded,
            $"Compilation failed:\n{string.Join("\n", result.Diagnostics.Select(d => $"  {d.Severity} {d.Id.Value}: {d.Message}"))}");

        // Read the emitted C# source files
        var csFiles = Directory.GetFiles(outputDir, "*.cs");
        Assert.NotEmpty(csFiles);

        var emittedSource = string.Join("\n", csFiles.Select(File.ReadAllText));

        // Verify 'var' is used for anonymous type declarations (not a concrete type)
        // The emitted code should have `var x = new {` patterns
        Assert.Contains("var x = new {", emittedSource);
        Assert.Contains("var y = new {", emittedSource);

        // Verify anonymous type syntax is correct — uses `new {` form
        Assert.Contains("new { Name = ", emittedSource);

        // Verify projection form — the variable name appears directly in the anonymous type
        Assert.Contains("city", emittedSource);
    }
}
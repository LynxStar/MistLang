// Feature: abstract-type-unification, Property 15: Unfilled CLR virtual slots emit nothing

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.AbstractTypeUnification;

/// <summary>
/// Example-based tests verifying that unfilled CLR virtual slots emit nothing.
///
/// Property 15: For any Mist type that does NOT fill a slot originating from a CLR
/// virtual method, the emitter SHALL NOT emit any method for that slot — the base
/// class implementation is inherited normally via C# semantics.
///
/// Since we cannot easily generate random CLR assemblies, these tests use deterministic
/// examples with well-known .NET types (System.Object.ToString is virtual).
///
/// Uses in-process compilation via CompilerDriver with EmitToDisk: false.
/// Spawns 0 external processes.
///
/// **Validates: Requirements 8.5**
/// </summary>
public sealed class UnfilledClrVirtualSlotsPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public UnfilledClrVirtualSlotsPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistUnfilledClrVirtPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver ───────────────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Extracts the brace-delimited body of a type declaration from emitted C#.
    /// </summary>
    private static string? ExtractClassSection(string emittedCSharp, string className)
    {
        var searchPattern = $"class {className}";
        var typeIndex = emittedCSharp.IndexOf(searchPattern, StringComparison.Ordinal);
        if (typeIndex < 0)
            return null;

        var braceStart = emittedCSharp.IndexOf('{', typeIndex);
        if (braceStart < 0)
            return null;

        var depth = 0;
        for (var i = braceStart; i < emittedCSharp.Length; i++)
        {
            if (emittedCSharp[i] == '{') depth++;
            else if (emittedCSharp[i] == '}') depth--;

            if (depth == 0)
            {
                return emittedCSharp[braceStart..(i + 1)];
            }
        }

        return null;
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// A simple class that does NOT override System.Object.ToString() should NOT
    /// have a ToString method emitted in the generated C#. The base implementation
    /// from System.Object is inherited normally.
    /// </summary>
    [Fact]
    public void SimpleClass_NotOverridingToString_DoesNotEmitToString()
    {
        const string source = """
            namespace TestProject;

            public class SimpleClass
            {
                private int value;

                public SimpleClass(int v)
                {
                    this.value = v;
                }

                public int GetValue()
                {
                    return this.value;
                }
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                }
            }
            """;

        var result = CompileSource(source);

        // Compilation should succeed
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(errors);

        // Get emitted C#
        Assert.NotNull(result.EmittedFiles);
        Assert.NotEmpty(result.EmittedFiles);

        var allEmitted = string.Join("\n\n", result.EmittedFiles.Values);

        // Extract SimpleClass section
        var classSection = ExtractClassSection(allEmitted, "SimpleClass");
        Assert.NotNull(classSection);

        // Verify ToString does NOT appear in the class body
        Assert.DoesNotContain("ToString", classSection);
    }

    /// <summary>
    /// A class with multiple methods but NOT overriding any Object virtual methods
    /// (ToString, Equals, GetHashCode) should not emit any of those methods.
    /// </summary>
    [Fact]
    public void ClassWithMethods_NotOverridingObjectVirtuals_DoesNotEmitObjectVirtuals()
    {
        const string source = """
            namespace TestProject;

            public class Calculator
            {
                private int result;

                public Calculator()
                {
                    this.result = 0;
                }

                public int Add(int a, int b)
                {
                    return a + b;
                }

                public int GetResult()
                {
                    return this.result;
                }
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(errors);

        Assert.NotNull(result.EmittedFiles);
        Assert.NotEmpty(result.EmittedFiles);

        var allEmitted = string.Join("\n\n", result.EmittedFiles.Values);

        var classSection = ExtractClassSection(allEmitted, "Calculator");
        Assert.NotNull(classSection);

        // None of Object's virtual methods should appear
        Assert.DoesNotContain("ToString", classSection);
        Assert.DoesNotContain("GetHashCode", classSection);
        Assert.DoesNotContain("Equals", classSection);
    }
}

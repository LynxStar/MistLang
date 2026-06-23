// Feature: abstract-type-unification, Property 14: CLR slot filling emits override

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
/// Deterministic example-based tests verifying that CLR slot filling emits the
/// `override` keyword in the generated C#.
///
/// Property 14: For any Mist type that fills a slot originating from a CLR
/// `abstract` or `virtual` method on a base class, the emitter SHALL generate
/// the C# `override` keyword on the emitted method.
///
/// Since we cannot easily generate random CLR assemblies, this test uses
/// well-known .NET types (System.Object) whose virtual methods are available
/// in every compilation. Object.ToString() is virtual, so filling it in Mist
/// should produce `override` in the emitted C#.
///
/// Uses in-process compilation via a shared CompilerDriver instance with
/// EmitToDisk: false to capture emitted C# content in memory.
/// Spawns 0 external processes.
///
/// **Validates: Requirements 8.4**
/// </summary>
public sealed class ClrSlotFillingOverridePropertyTests : IDisposable
{
    private readonly string _outputDir;

    public ClrSlotFillingOverridePropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistClrOverridePBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across tests) ─────────────────────────────

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
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Returns all emitted C# content concatenated, or null if no files were emitted.
    /// </summary>
    private static string? GetAllEmittedCSharp(CompilerResult result)
    {
        if (result.EmittedFiles is null || result.EmittedFiles.Count == 0)
            return null;

        return string.Join("\n\n", result.EmittedFiles.Values);
    }

    /// <summary>
    /// Extracts the brace-delimited body of a type declaration from emitted C#.
    /// </summary>
    private static string? ExtractTypeSection(string emittedCSharp, string typeKeyword, string typeName)
    {
        var searchPattern = $"{typeKeyword} {typeName}";
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
    /// Filling Object.ToString() (a CLR virtual method) should emit `override`
    /// in the generated C#.
    /// </summary>
    [Fact]
    public void FillingObjectToString_EmitsOverride()
    {
        var source = """
            namespace TestProject;

            public class MyClass
            {
                public string ToString()
                {
                    return "custom";
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

        var emittedCSharp = GetAllEmittedCSharp(result);
        Assert.NotNull(emittedCSharp);

        // The emitted C# for MyClass should contain `override` on ToString
        var classSection = ExtractTypeSection(emittedCSharp, "class", "MyClass");
        Assert.NotNull(classSection);

        // Verify the override keyword appears before ToString in the class body
        Assert.Contains("override", classSection);

        // More specifically, check for the pattern "override ... ToString"
        var overrideIndex = classSection.IndexOf("override", StringComparison.Ordinal);
        var toStringIndex = classSection.IndexOf("ToString", overrideIndex, StringComparison.Ordinal);
        Assert.True(toStringIndex > overrideIndex,
            $"Expected 'override' keyword before 'ToString' method in emitted C#.\nClass section:\n{classSection}");
    }

    /// <summary>
    /// Filling Object.GetHashCode() (a CLR virtual method) should emit `override`
    /// in the generated C#.
    /// </summary>
    [Fact]
    public void FillingObjectGetHashCode_EmitsOverride()
    {
        var source = """
            namespace TestProject;

            public class MyClass
            {
                public int GetHashCode()
                {
                    return 42;
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

        var emittedCSharp = GetAllEmittedCSharp(result);
        Assert.NotNull(emittedCSharp);

        var classSection = ExtractTypeSection(emittedCSharp, "class", "MyClass");
        Assert.NotNull(classSection);

        // Verify the override keyword appears before GetHashCode in the class body
        Assert.Contains("override", classSection);

        var overrideIndex = classSection.IndexOf("override", StringComparison.Ordinal);
        var methodIndex = classSection.IndexOf("GetHashCode", overrideIndex, StringComparison.Ordinal);
        Assert.True(methodIndex > overrideIndex,
            $"Expected 'override' keyword before 'GetHashCode' method in emitted C#.\nClass section:\n{classSection}");
    }

    /// <summary>
    /// Filling Object.Equals(object) (a CLR virtual method) should emit `override`
    /// in the generated C#.
    /// </summary>
    [Fact]
    public void FillingObjectEquals_EmitsOverride()
    {
        var source = """
            namespace TestProject;

            public class MyClass
            {
                public bool Equals(object obj)
                {
                    return true;
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

        var emittedCSharp = GetAllEmittedCSharp(result);
        Assert.NotNull(emittedCSharp);

        var classSection = ExtractTypeSection(emittedCSharp, "class", "MyClass");
        Assert.NotNull(classSection);

        // Verify the override keyword appears before Equals in the class body
        Assert.Contains("override", classSection);

        var overrideIndex = classSection.IndexOf("override", StringComparison.Ordinal);
        var methodIndex = classSection.IndexOf("Equals", overrideIndex, StringComparison.Ordinal);
        Assert.True(methodIndex > overrideIndex,
            $"Expected 'override' keyword before 'Equals' method in emitted C#.\nClass section:\n{classSection}");
    }

    /// <summary>
    /// A method that does NOT correspond to a CLR virtual/abstract slot should NOT
    /// emit `override`. This verifies that override is only used for CLR slot filling.
    /// </summary>
    [Fact]
    public void NonVirtualMethod_DoesNotEmitOverride()
    {
        var source = """
            namespace TestProject;

            public class MyClass
            {
                public string CustomMethod()
                {
                    return "hello";
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

        var emittedCSharp = GetAllEmittedCSharp(result);
        Assert.NotNull(emittedCSharp);

        var classSection = ExtractTypeSection(emittedCSharp, "class", "MyClass");
        Assert.NotNull(classSection);

        // CustomMethod is not a CLR virtual method, so no override should appear
        Assert.Contains("CustomMethod", classSection);

        // There should be no override at all in this class since ToString/GetHashCode/Equals
        // are not being overridden
        Assert.DoesNotContain("override", classSection);
    }
}

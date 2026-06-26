// Feature: drop-semantics, Property 10: Reverse declaration order
//
// **Validates: Requirements 4.5, 13.1**

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
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

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// A generated scenario: N linear locals declared in a scope, where the expected
/// emitted Drop() calls must appear in reverse declaration order.
/// </summary>
public sealed class ReverseDropOrderScenario
{
    /// <summary>The generated Mist source code.</summary>
    public string Source { get; }

    /// <summary>Local variable names in declaration order.</summary>
    public IReadOnlyList<string> LocalNamesInOrder { get; }

    /// <summary>Human-readable description for shrinking/reporting.</summary>
    public string Description { get; }

    public ReverseDropOrderScenario(string source, IReadOnlyList<string> localNamesInOrder, string description)
    {
        Source = source;
        LocalNamesInOrder = localNamesInOrder;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

public static class ReverseDropOrderGenerators
{
    /// <summary>
    /// Generates a scenario with N linear locals (N from 2 to 10) all live at scope exit,
    /// asserting that Drop() calls appear in reverse declaration order.
    /// </summary>
    public static Gen<ReverseDropOrderScenario> GenReverseDropOrderScenario()
    {
        return from count in Gen.Choose(2, 10)
               from seed in Gen.Choose(0, 100)
               select BuildScenario(count, seed);
    }

    private static ReverseDropOrderScenario BuildScenario(int count, int seed)
    {
        // Generate distinct local names using a prefix and index to guarantee uniqueness.
        // We use names like v0, v1, v2, ... vN-1 to make order verification unambiguous.
        var localNames = Enumerable.Range(0, count).Select(i => $"v{i}").ToList();

        var source = BuildSource(localNames);
        var description = $"N={count} linear locals, seed={seed}";

        return new ReverseDropOrderScenario(source, localNames, description);
    }

    private static string BuildSource(List<string> localNames)
    {
        var sb = new StringBuilder();

        // Linear type with void Drop() — each instance is distinguishable
        sb.AppendLine("linear class Resource");
        sb.AppendLine("{");
        sb.AppendLine("    int id;");
        sb.AppendLine();
        sb.AppendLine("    public Resource(int val)");
        sb.AppendLine("    {");
        sb.AppendLine("        this.id = val;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public void Drop()");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public int GetId()");
        sb.AppendLine("    {");
        sb.AppendLine("        return id;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        // Program with Main that declares N linear locals, all live at scope exit
        sb.AppendLine("class Program");
        sb.AppendLine("{");
        sb.AppendLine("    public static void Main()");
        sb.AppendLine("    {");

        for(var i = 0; i < localNames.Count; i++)
        {
            sb.AppendLine($"        Resource {localNames[i]} = new Resource({i});");
        }

        // Access each to ensure they are "used" (avoid potential unused-variable issues)
        for(var i = 0; i < localNames.Count; i++)
        {
            sb.AppendLine($"        int id{i} = {localNames[i]}.GetId();");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for reverse declaration order in drop emission.
///
/// Feature: drop-semantics, Property 10: Reverse declaration order
///
/// **Validates: Requirements 4.5, 13.1**
///
/// For any scope containing N variables with drop obligations (N ≥ 2), the compiler
/// SHALL insert Drop() calls in reverse declaration order — the last declared variable
/// is dropped first.
///
/// All compilation runs fully in-process via a shared <see cref="CompilerDriver"/> with
/// <c>EmitToDisk: false</c>; the emitted C# text is inspected directly.
/// Spawns 0 external processes.
/// </summary>
public sealed class ReverseDeclarationDropOrderPropertyTests : IDisposable
{
    private readonly string outputDir;

    public ReverseDeclarationDropOrderPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistReverseDropOrderPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across iterations) ────────────────────────

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

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<ReverseDropOrderScenario> ArbitraryReverseDropOrderScenario() =>
        ReverseDropOrderGenerators.GenReverseDropOrderScenario().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Returns the emitted user C# file (the one containing the test namespace), or null.
    /// </summary>
    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the ordered list of Drop() calls from the emitted C# by finding
    /// all occurrences of <c>{varName}.Drop()</c> and returning them in the order
    /// they appear in the source text.
    /// </summary>
    private static List<string> ExtractDropOrder(string emittedCSharp, IReadOnlyList<string> localNames)
    {
        // Build a regex that matches any of our local names followed by .Drop()
        // Using word-boundary to avoid matching substrings of other identifiers.
        var pattern = $@"(?<name>{string.Join("|", localNames.Select(Regex.Escape))})\.Drop\(\)";
        var matches = Regex.Matches(emittedCSharp, pattern);

        return matches.Select(m => m.Groups["name"].Value).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: Reverse Declaration Order for Multiple Drops
    //
    // **Validates: Requirements 4.5, 13.1**
    //
    // For each generated scenario with N linear locals (2 ≤ N ≤ 10), the emitted
    // C# must contain Drop() calls for all N locals, and those calls must appear
    // in reverse declaration order (last declared is dropped first).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReverseDeclarationDropOrderPropertyTests) })]
    public Property MultipleLinearLocals_DroppedInReverseDeclarationOrder(ReverseDropOrderScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found.\nSource:\n{scenario.Source}");
        }

        var dropOrder = ExtractDropOrder(emittedCSharp, scenario.LocalNamesInOrder);

        // All N locals must have a Drop() call
        if(dropOrder.Count != scenario.LocalNamesInOrder.Count)
        {
            return false.ToProperty()
                .Label($"Expected {scenario.LocalNamesInOrder.Count} Drop() calls but found {dropOrder.Count}.\n" +
                       $"Drop calls found: [{string.Join(", ", dropOrder)}]\n" +
                       $"Expected locals: [{string.Join(", ", scenario.LocalNamesInOrder)}]\n" +
                       $"Emitted C#:\n{emittedCSharp}");
        }

        // The drop order must be the reverse of declaration order
        var expectedOrder = scenario.LocalNamesInOrder.Reverse().ToList();
        var orderMatches = dropOrder.SequenceEqual(expectedOrder);

        return orderMatches.ToProperty()
            .Label($"Drop order mismatch.\n" +
                   $"Expected (reverse declaration): [{string.Join(", ", expectedOrder)}]\n" +
                   $"Actual: [{string.Join(", ", dropOrder)}]\n" +
                   $"Emitted C#:\n{emittedCSharp}");
    }

    // ─── Example-based tests (deterministic) ─────────────────────────────

    /// <summary>
    /// Two linear locals — last declared is dropped first.
    /// **Validates: Requirements 4.5, 13.1**
    /// </summary>
    [Fact]
    public void TwoLinearLocals_DroppedInReverseOrder()
    {
        var source = """
            linear class Resource
            {
                int id;

                public Resource(int val)
                {
                    this.id = val;
                }

                public void Drop()
                {
                }

                public int GetId()
                {
                    return id;
                }
            }
            class Program
            {
                public static void Main()
                {
                    Resource a = new Resource(1);
                    Resource b = new Resource(2);
                    int x = a.GetId();
                    int y = b.GetId();
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded,
            "Compilation failed: " + string.Join("; ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}")));

        var emitted = GetEmittedCSharp(result);
        Assert.NotNull(emitted);

        var dropOrder = ExtractDropOrder(emitted, new[] { "a", "b" });
        Assert.Equal(2, dropOrder.Count);
        // b was declared last → dropped first
        Assert.Equal("b", dropOrder[0]);
        Assert.Equal("a", dropOrder[1]);
    }

    /// <summary>
    /// Three linear locals — reverse declaration order: c, b, a.
    /// **Validates: Requirements 4.5, 13.1**
    /// </summary>
    [Fact]
    public void ThreeLinearLocals_DroppedInReverseOrder()
    {
        var source = """
            linear class Resource
            {
                int id;

                public Resource(int val)
                {
                    this.id = val;
                }

                public void Drop()
                {
                }

                public int GetId()
                {
                    return id;
                }
            }
            class Program
            {
                public static void Main()
                {
                    Resource a = new Resource(1);
                    Resource b = new Resource(2);
                    Resource c = new Resource(3);
                    int x = a.GetId();
                    int y = b.GetId();
                    int z = c.GetId();
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded,
            "Compilation failed: " + string.Join("; ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}")));

        var emitted = GetEmittedCSharp(result);
        Assert.NotNull(emitted);

        var dropOrder = ExtractDropOrder(emitted, new[] { "a", "b", "c" });
        Assert.Equal(3, dropOrder.Count);
        // c, b, a (reverse declaration order)
        Assert.Equal("c", dropOrder[0]);
        Assert.Equal("b", dropOrder[1]);
        Assert.Equal("a", dropOrder[2]);
    }
}
// Feature: stack-only-types, Property 14: Stackalloc expression emission
// **Validates: Requirements 6.1**

using System.Collections.Immutable;
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
/// Describes a generated stackalloc emission scenario for property testing.
/// Carries the Mist source, the expected CLR element type name, and the size
/// expression as it should appear in emitted C#.
/// </summary>
public sealed record StackallocEmissionScenario(
    string Source,
    string MistElementType,
    string ExpectedClrType,
    string SizeExpression,
    string Description)
{
    public override string ToString()
    {
        return Description;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for C# emission of stackalloc expressions.
///
/// Property 14: Stackalloc expression emission
///
/// For any Mist <c>stackalloc T[n]</c> expression where T is a stampable
/// primitive type, the emitted C# SHALL contain <c>stackalloc C[E]</c> where
/// C is the CLR name of T (resolved via Backing_Type_Map) and E is the emitted
/// size expression appearing exactly once.
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance
/// with EmitToDisk: false to capture emitted C# content in memory.
///
/// **Validates: Requirements 6.1**
/// </summary>
public sealed class StackallocExpressionEmissionPropertyTests : IDisposable
{
    private readonly string outputDir;

    public StackallocExpressionEmissionPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistStackallocEmitPBT_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
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
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Element type pool: (Mist keyword, expected CLR emission name) ───

    private static readonly (string MistType, string ClrType)[] ElementTypes =
    [
        ("int", "int"),
        ("uint", "uint"),
        ("long", "long"),
        ("ulong", "ulong"),
        ("short", "short"),
        ("ushort", "ushort"),
        ("byte", "byte"),
        ("sbyte", "sbyte"),
        ("float", "float"),
        ("double", "double"),
        ("decimal", "decimal"),
        ("bool", "bool"),
        ("char", "char"),
    ];

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a stackalloc emission scenario with a random primitive element
    /// type and an integer literal size (known length).
    /// </summary>
    private static Gen<StackallocEmissionScenario> GenLiteralSizeScenario()
    {
        return from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               from size in Gen.Choose(1, 1024)
               let mistType = ElementTypes[typeIdx].MistType
               let clrType = ElementTypes[typeIdx].ClrType
               let sizeStr = size.ToString()
               let source = WrapInProgram($"var s = stackalloc {mistType}[{sizeStr}];")
               select new StackallocEmissionScenario(
                   source, mistType, clrType, sizeStr,
                   $"stackalloc {mistType}[{sizeStr}] → stackalloc {clrType}[{sizeStr}]");
    }

    /// <summary>
    /// Generates a stackalloc emission scenario with a random primitive element
    /// type and a runtime (non-literal) size expression referencing a local variable.
    /// </summary>
    private static Gen<StackallocEmissionScenario> GenRuntimeSizeScenario()
    {
        return from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               from sizeVal in Gen.Choose(1, 100)
               let mistType = ElementTypes[typeIdx].MistType
               let clrType = ElementTypes[typeIdx].ClrType
               // Use a local variable as the runtime size — the emitter will emit
               // the variable name as the size expression.
               let source = WrapInProgram(
                   $"var n = {sizeVal};\n        var s = stackalloc {mistType}[n];")
               select new StackallocEmissionScenario(
                   source, mistType, clrType, "n",
                   $"stackalloc {mistType}[n] (n={sizeVal}) → stackalloc {clrType}[n]");
    }

    /// <summary>
    /// Combined generator: literal and runtime sizes with equal probability.
    /// </summary>
    public static Arbitrary<StackallocEmissionScenario> ArbitraryStackallocEmissionScenario()
    {
        return Gen.OneOf(
            GenLiteralSizeScenario(),
            GenRuntimeSizeScenario()).ToArbitrary();
    }

    // ─── Program wrapping ───────────────────────────────────────────────

    private static string WrapInProgram(string body)
    {
        return $"class Program {{\n    static void Main() {{\n        {body}\n    }}\n}}";
    }

    // ─── Compilation helper ─────────────────────────────────────────────

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
    /// Extracts the emitted C# content from the compilation result.
    /// Returns the file containing the user namespace (TestProgram).
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

    // ═══════════════════════════════════════════════════════════════════════
    // Property 14: Stackalloc expression emission
    //
    // For any Mist `stackalloc T[n]` expression with stampable element type T,
    // the emitted C# SHALL contain `stackalloc C[E]` where C is the CLR name
    // of T and E is the emitted size expression appearing exactly once.
    //
    // **Validates: Requirements 6.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(StackallocExpressionEmissionPropertyTests) })]
    public Property StackallocExpression_EmitsCorrectClrTypeAndSize(
        StackallocEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ",
                result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found for namespace TestProgram.\nSource:\n{scenario.Source}");
        }

        // The expected stackalloc fragment: `stackalloc C[E]`
        var expectedFragment = $"stackalloc {scenario.ExpectedClrType}[{scenario.SizeExpression}]";

        // Count occurrences — the size expression within the stackalloc should appear exactly once
        var occurrences = CountOccurrences(emittedCSharp, expectedFragment);

        if(occurrences != 1)
        {
            return false.ToProperty()
                .Label($"Expected exactly 1 occurrence of '{expectedFragment}' but found {occurrences}.\n" +
                       $"Emitted C#:\n{emittedCSharp}");
        }

        return true.ToProperty()
            .Label($"OK: '{expectedFragment}' appears exactly once");
    }

    // ─── Utility ────────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var idx = 0;
        while((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
// Regression tests for the dropped-operand binary-chain emission defect.
//
// A left-associative binary chain such as `a + b + c` is represented by the
// generated grammar node as a head operand plus an array of trailing operands
// (`Multiplicatives`, `Unarys`, `Relationals`, `Additives`, `Equalitys`,
// `LogicalAnds`). The C# emitter previously emitted only the head operand and the
// single (last) trailing operand, silently dropping every middle operand, so
// `a + b + c` emitted as `a + c`. This is a pre-existing, general emitter defect
// independent of variadics; it surfaced through Requirement 18.7 (the Variadics
// sample's `Sum3`/`forward` producing wrong values).
//
// These tests compile Mist source in-process (no external process is spawned;
// EmitToDisk is false) and assert the emitted C# preserves every operand in the
// correct left-associative order.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Backend;

/// <summary>
/// Example-based regression tests verifying that the C# emitter emits every
/// operand of a left-associative binary chain (three or more operands) instead
/// of dropping the middle operands.
/// </summary>
public sealed class BinaryChainEmissionTests : IDisposable
{
    private readonly string outputDir;

    public BinaryChainEmissionTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistBinaryChainEmit_" + Guid.NewGuid().ToString("N"));
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

    // ─── Helpers ─────────────────────────────────────────────────────────

    private string CompileToCSharp(string source)
    {
        // Append a trivial entry point so the type checker does not halt the
        // pipeline (and skip backend emission) with MIST_TC_0017.
        var withEntryPoint = source
            + "\n\npublic class __EntryPoint { public static void Main() { } }\n";

        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, withEntryPoint);

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

        var result = SharedDriver.Compile(options);

        var errors = result.Diagnostics
            .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error
                && d.Id.Value != "MIST_TC_0017")
            .ToList();
        Assert.True(errors.Count == 0,
            $"Compilation failed:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}");

        Assert.NotNull(result.EmittedFiles);
        var userFile = result.EmittedFiles!.Values
            .FirstOrDefault(content => content.Contains("namespace TestProgram"));
        Assert.False(string.IsNullOrEmpty(userFile),
            "No emitted user C# file was found.");
        return userFile!;
    }

    /// <summary>Collapses runs of whitespace so emitted-shape assertions are robust to indentation.</summary>
    private static string Normalize(string s)
    {
        return System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Additive chains (Requirement 18.7 — Sum3 / forward)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AdditiveChain_ThreeOperands_EmitsEveryOperand()
    {
        const string source = """
            namespace TestProgram;

            public class Chains
            {
                public int Sum3(int a, int b, int c)
                {
                    return a + b + c;
                }
            }
            """;

        var norm = Normalize(CompileToCSharp(source));

        Assert.Contains("return a + b + c;", norm);
    }

    [Fact]
    public void AdditiveChain_FourOperands_EmitsEveryOperandInOrder()
    {
        const string source = """
            namespace TestProgram;

            public class Chains
            {
                public int Sum4(int a, int b, int c, int d)
                {
                    return a + b + c + d;
                }
            }
            """;

        var norm = Normalize(CompileToCSharp(source));

        Assert.Contains("return a + b + c + d;", norm);
    }

    [Fact]
    public void SubtractiveChain_ThreeOperands_EmitsEveryOperandInLeftAssociativeOrder()
    {
        const string source = """
            namespace TestProgram;

            public class Chains
            {
                public int Diff3(int a, int b, int c)
                {
                    return a - b - c;
                }
            }
            """;

        var norm = Normalize(CompileToCSharp(source));

        Assert.Contains("return a - b - c;", norm);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Multiplicative chains
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultiplicativeChain_ThreeOperands_EmitsEveryOperand()
    {
        const string source = """
            namespace TestProgram;

            public class Chains
            {
                public int Product3(int a, int b, int c)
                {
                    return a * b * c;
                }
            }
            """;

        var norm = Normalize(CompileToCSharp(source));

        Assert.Contains("return a * b * c;", norm);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Logical chains
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LogicalAndChain_ThreeOperands_EmitsEveryOperand()
    {
        const string source = """
            namespace TestProgram;

            public class Chains
            {
                public bool All3(bool x, bool y, bool z)
                {
                    return x && y && z;
                }
            }
            """;

        var norm = Normalize(CompileToCSharp(source));

        Assert.Contains("return x && y && z;", norm);
    }

    [Fact]
    public void LogicalOrChain_ThreeOperands_EmitsEveryOperand()
    {
        const string source = """
            namespace TestProgram;

            public class Chains
            {
                public bool Any3(bool x, bool y, bool z)
                {
                    return x || y || z;
                }
            }
            """;

        var norm = Normalize(CompileToCSharp(source));

        Assert.Contains("return x || y || z;", norm);
    }
}
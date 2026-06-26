using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that the type checker correctly enforces readonly local
/// variable reassignment prohibition and ref argument restriction.
///
/// Requirements: 3.1–3.6, 4.1–4.5, 6.1–6.3
/// </summary>
public sealed class ReadonlyLocalTypeCheckerTests : IDisposable
{
    private readonly string outputDir;

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer(), new ReadonlyAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    public ReadonlyLocalTypeCheckerTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistReadonlyLocalTC_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var fullSource = "namespace TestProject;\n" + source;
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, fullSource);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    private static bool HasDiagnostic(CompilerResult result, string diagnosticId)
    {
        return result.Diagnostics.Any(d => d.Id.Value == diagnosticId);
    }

    // ─── Test 1: readonly var x = 7; x = 10; → MIST_READONLY_LOCAL_0001 ─

    [Fact]
    public void ReadonlyVarReassignment_ProducesDiagnostic()
    {
        var source = """
            class Program {
                public static void Main() {
                    readonly var x = 7;
                    x = 10;
                }
            }
            """;

        var result = CompileSource(source);

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Id.Value == "MIST_READONLY_LOCAL_0001");
        Assert.NotNull(diagnostic);
        Assert.Contains("Cannot reassign readonly local variable 'x'", diagnostic!.Message);
    }

    // ─── Test 2: var x = 7; x = 10; → no readonly diagnostic ────────────

    [Fact]
    public void NonReadonlyVarReassignment_DoesNotProduceReadonlyDiagnostic()
    {
        var source = """
            class Program {
                public static void Main() {
                    var x = 7;
                    x = 10;
                }
            }
            """;

        var result = CompileSource(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.Value == "MIST_READONLY_LOCAL_0001");
    }

    // ─── Test 3: passing readonly local as ref argument → diagnostic ─────

    [Fact]
    public void ReadonlyLocalPassedAsRefArgument_ProducesDiagnostic()
    {
        var source = """
            class Program {
                public static void Main() {
                    readonly var x = 7;
                    Mutate(ref x);
                }

                public static void Mutate(ref int value) {
                    value = 42;
                }
            }
            """;

        var result = CompileSource(source);

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Id.Value == "MIST_READONLY_LOCAL_0001");
        Assert.NotNull(diagnostic);
        Assert.Contains("Cannot pass readonly local variable", diagnostic!.Message);
        Assert.Contains("by mutable reference", diagnostic.Message);
    }

    // ─── Test 4: readonly var inside readonly { } block → still gets diagnostic ─

    [Fact]
    public void ReadonlyLocalInsideReadonlyBlock_StillProducesDiagnostic()
    {
        var source = """
            class Program {
                public static void Main() {
                    readonly {
                        readonly var x = 7;
                        x = 10;
                    }
                }
            }
            """;

        var result = CompileSource(source);

        // Debug: output all diagnostics and success status
        var allDiags = string.Join("\n", result.Diagnostics.Select(d => $"[{d.Severity}] {d.Id.Value}: {d.Message}"));
        Assert.True(HasDiagnostic(result, "MIST_READONLY_LOCAL_0001"),
            $"Expected MIST_READONLY_LOCAL_0001 for reassignment of readonly local inside readonly block. Succeeded={result.Succeeded}, Diagnostics:\n{allDiags}");
    }

    // ─── Test 5: readonly tuple deconstruction → diagnostic on reassignment ─

    [Fact]
    public void ReadonlyTupleDeconstructionReassignment_ProducesDiagnostic()
    {
        var source = """
            class Program {
                public static void Main() {
                    readonly var (a, b) = (1, 2);
                    a = 3;
                }
            }
            """;

        var result = CompileSource(source);

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Id.Value == "MIST_READONLY_LOCAL_0001");
        Assert.NotNull(diagnostic);
        Assert.Contains("Cannot reassign readonly local variable 'a'", diagnostic!.Message);
    }
}
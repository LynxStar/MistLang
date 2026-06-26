// **Validates: Requirements 8.1, 8.2**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that arithmetic and comparison operations on refinement
/// types produce the base type (arithmetic erasure) and use base type semantics.
///
/// Uses CompilerDriver with stdlib to ensure operator resolution is available.
///
/// **Validates: Requirements 8.1, 8.2**
/// </summary>
public class RefinementArithmeticErasureTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 8.1: Arithmetic on refinement type produces base type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Addition_OnRefinementType_ProducesBaseType()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    AdultAge age = 20;
                    int result = age + 1;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // No "operator cannot be applied" errors
        Assert.Empty(WithId(diagnostics, "MIST_TC_0003"));
        // No refinement enforcement errors (result is int, not AdultAge)
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0098"));
    }

    [Fact]
    public void Subtraction_OnRefinementType_ProducesBaseType()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    AdultAge age = 25;
                    int result = age - 5;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_TC_0003"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0098"));
    }

    [Fact]
    public void Multiplication_OnRefinementType_ProducesBaseType()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    AdultAge age = 20;
                    int result = age * 2;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_TC_0003"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0098"));
    }

    [Fact]
    public void Division_OnRefinementType_ProducesBaseType()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    AdultAge age = 20;
                    int result = age / 2;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_TC_0003"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0098"));
    }

    [Fact]
    public void Modulo_OnRefinementType_ProducesBaseType()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    AdultAge age = 20;
                    int result = age % 3;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_TC_0003"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0098"));
    }

    [Fact]
    public void ArithmeticResult_CannotBeAssignedToRefinementType()
    {
        // The result of arithmetic is the base type (int), not the refinement type.
        // Assigning it back to a refinement type variable should require validation.
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    AdultAge age = 20;
                    AdultAge next = age + 1;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Should produce a "non-constant assigned to refinement type" diagnostic
        Assert.NotEmpty(WithId(diagnostics, "MIST_TC_0098"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 8.2: Comparison on refinement type uses base type semantics
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Comparison_OnRefinementType_UsesBaseTypeSemantics()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    AdultAge age = 20;
                    bool result = age > 21;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // No "operator cannot be applied" errors
        Assert.Empty(WithId(diagnostics, "MIST_TC_0004"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0003"));
    }

    [Fact]
    public void Equality_OnRefinementType_UsesBaseTypeSemantics()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    AdultAge age = 20;
                    bool result = age == 20;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // No "operator cannot be applied" errors
        Assert.Empty(WithId(diagnostics, "MIST_TC_0005"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0003"));
    }

    [Fact]
    public void TwoRefinementTypes_InArithmetic_ProducesBaseType()
    {
        const string source = """
            type AdultAge = int >= 18;
            type Percentage = int >= 0;
            class Program {
                public static void Main(string[] args) {
                    AdultAge age = 20;
                    Percentage pct = 50;
                    int result = age + pct;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_TC_0003"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0098"));
    }

    [Fact]
    public void TwoRefinementTypes_InComparison_UsesBaseTypeSemantics()
    {
        const string source = """
            type AdultAge = int >= 18;
            type Percentage = int >= 0;
            class Program {
                public static void Main(string[] args) {
                    AdultAge age = 20;
                    Percentage pct = 50;
                    bool result = age < pct;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_TC_0004"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0003"));
    }
}
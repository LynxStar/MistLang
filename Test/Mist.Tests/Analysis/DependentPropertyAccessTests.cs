// Feature: dependent-types, Task 6.3: Dependent property access resolution
// **Validates: Requirements 7.2, 7.3, 7.4**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying dependent property access resolution:
/// - TypeName&lt;Value&gt;.ParamName resolves to the generated dependent property
/// - The dependent property is read-only (assignment produces MIST_TC_0020)
/// </summary>
public class DependentPropertyAccessTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private static ImmutableArray<Diagnostic> CompileAndGetAllDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return parseDiag.ToImmutableArray();
        }

        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag.ToImmutableArray());
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.2: Dependent property access resolves correctly
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DependentPropertyAccess_ResolvesCorrectly_NoDiagnostics()
    {
        const string source = """
            class FixedBuffer<Size:int> {
            }
            class Program {
                public static void Main() {
                    int x = FixedBuffer<5>.Size;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        // Should not produce any errors at all (except possibly entry point)
        var allErrors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value != "MIST_TC_0017")
            .ToList();

        var allDiags = diagnostics.Select(d => $"{d.Id.Value}: {d.Message}").ToList();
        Assert.True(allErrors.Count == 0, $"Expected no errors but got: [{string.Join("; ", allDiags)}]");
    }

    [Fact]
    public void DependentPropertyAccess_MultipleParams_ResolvesCorrectly()
    {
        const string source = """
            class Matrix<Rows:int, Cols:int> {
            }
            class Program {
                public static void Main() {
                    int r = Matrix<3, 4>.Rows;
                    int c = Matrix<3, 4>.Cols;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var memberErrors = diagnostics
            .Where(d => d.Id.Value is "MIST_TC_0065" or "MIST_UNDEFINED_MEMBER"
                or "MIST_TC_0019" or "MIST_TC_0015" or "MIST_TC_0016")
            .ToList();

        Assert.Empty(memberErrors);
    }

    [Fact]
    public void DependentPropertyAccess_BoolParam_ResolvesCorrectly()
    {
        const string source = """
            class Flag<Enabled:bool> {
            }
            class Program {
                public static void Main() {
                    bool f = Flag<true>.Enabled;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var memberErrors = diagnostics
            .Where(d => d.Id.Value is "MIST_TC_0065" or "MIST_UNDEFINED_MEMBER"
                or "MIST_TC_0019" or "MIST_TC_0015" or "MIST_TC_0016")
            .ToList();

        Assert.Empty(memberErrors);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.3: Dependent property is on the type, not instances
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DependentPropertyAccess_ViaTypeName_IsValid()
    {
        const string source = """
            class FixedBuffer<Size:int> {
            }
            class Program {
                public static void Main() {
                    int s = FixedBuffer<10>.Size;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var memberErrors = diagnostics
            .Where(d => d.Id.Value is "MIST_TC_0065" or "MIST_UNDEFINED_MEMBER"
                or "MIST_TC_0019" or "MIST_TC_0015" or "MIST_TC_0016")
            .ToList();

        Assert.Empty(memberErrors);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.4: Dependent property is read-only
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DependentPropertyAssignment_ProducesDiagnostic()
    {
        // Test that the generated dependent property is read-only.
        // Access via an instance variable and attempt assignment.
        // The property has HasSet=false, so MIST_TC_0020 should be emitted.
        const string source = """
            class FixedBuffer<Size:int> {
                public int Value { get; set; }
            }
            class Program {
                public static void Main() {
                    var buf = new FixedBuffer<5>();
                    buf.Size = 10;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        // Should produce MIST_TC_0020: no accessible 'set' or 'init' accessor
        var assignDiag = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0020")
            .ToList();

        var allDiags = diagnostics.Select(d => $"{d.Id.Value}: {d.Message}").ToList();
        Assert.True(assignDiag.Count > 0, $"Expected MIST_TC_0020 but got: [{string.Join("; ", allDiags)}]");
        Assert.Contains("Size", assignDiag[0].Message);
    }

    [Fact]
    public void DependentPropertyAssignment_MultipleParams_ProducesDiagnostic()
    {
        const string source = """
            class Matrix<Rows:int, Cols:int> {
                public int Value { get; set; }
            }
            class Program {
                public static void Main() {
                    var m = new Matrix<3, 4>();
                    m.Rows = 5;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var assignDiag = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0020")
            .ToList();

        var allDiags = diagnostics.Select(d => $"{d.Id.Value}: {d.Message}").ToList();
        Assert.True(assignDiag.Count > 0, $"Expected MIST_TC_0020 but got: [{string.Join("; ", allDiags)}]");
        Assert.Contains("Rows", assignDiag[0].Message);
    }
}
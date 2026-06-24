// Feature: dependent-types, Task 6.7: Abstract dependent parameter validation
// **Validates: Requirements 10.3, 10.4, 10.5**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying abstract dependent parameter validation:
/// - Concrete dependent value when implementing an abstract (Requirement 10.3)
/// - Forwarding from class dependent parameter to abstract dependent parameter (Requirement 10.4)
/// - Dependent property accessible on abstract type (Requirement 10.5)
/// </summary>
public class DependentAbstractParameterValidationTests
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
        if (parseResult.Root is null)
            return parseDiag.ToImmutableArray();

        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(parseResult.Root),
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
    // Requirement 10.3: Concrete dependent value when implementing abstract
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AbstractWithDependentParam_ConcreteValue_NoDiagnostics()
    {
        const string source = """
            abstract IFixedCollection<Size:int> {
            }
            class SmallBuffer : IFixedCollection<16> {
            }
            class Program {
                public static void Main() {
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value != "MIST_TC_0017")
            .ToList();

        var allDiags = diagnostics.Select(d => $"{d.Id.Value}: {d.Message}").ToList();
        Assert.True(errors.Count == 0, $"Expected no errors but got: [{string.Join("; ", allDiags)}]");
    }

    [Fact]
    public void AbstractWithDependentParam_MultipleConcrete_NoDiagnostics()
    {
        const string source = """
            abstract IMatrix<Rows:int, Cols:int> {
            }
            class SmallMatrix : IMatrix<3, 4> {
            }
            class Program {
                public static void Main() {
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value != "MIST_TC_0017")
            .ToList();

        var allDiags = diagnostics.Select(d => $"{d.Id.Value}: {d.Message}").ToList();
        Assert.True(errors.Count == 0, $"Expected no errors but got: [{string.Join("; ", allDiags)}]");
    }

    [Fact]
    public void AbstractWithDependentParam_BoolValue_NoDiagnostics()
    {
        const string source = """
            abstract IToggleable<Enabled:bool> {
            }
            class AlwaysOn : IToggleable<true> {
            }
            class Program {
                public static void Main() {
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value != "MIST_TC_0017")
            .ToList();

        var allDiags = diagnostics.Select(d => $"{d.Id.Value}: {d.Message}").ToList();
        Assert.True(errors.Count == 0, $"Expected no errors but got: [{string.Join("; ", allDiags)}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.4: Forwarding from class to abstract dependent param
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AbstractWithDependentParam_ForwardedFromClass_NoDiagnostics()
    {
        const string source = """
            abstract IFixedCollection<Size:int> {
            }
            class Buffer<N:int> : IFixedCollection<N> {
            }
            class Program {
                public static void Main() {
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value != "MIST_TC_0017")
            .ToList();

        var allDiags = diagnostics.Select(d => $"{d.Id.Value}: {d.Message}").ToList();
        Assert.True(errors.Count == 0, $"Expected no errors but got: [{string.Join("; ", allDiags)}]");
    }

    [Fact]
    public void AbstractWithDependentParam_ForwardedMultiple_NoDiagnostics()
    {
        const string source = """
            abstract IMatrix<Rows:int, Cols:int> {
            }
            class FlexMatrix<R:int, C:int> : IMatrix<R, C> {
            }
            class Program {
                public static void Main() {
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value != "MIST_TC_0017")
            .ToList();

        var allDiags = diagnostics.Select(d => $"{d.Id.Value}: {d.Message}").ToList();
        Assert.True(errors.Count == 0, $"Expected no errors but got: [{string.Join("; ", allDiags)}]");
    }

    [Fact]
    public void AbstractWithDependentParam_NarrowingForwarding_EmitsDiagnostic()
    {
        // Forwarding long -> int is a narrowing violation (MIST_DT_0004).
        const string source = """
            abstract IFixedCollection<Size:int> {
            }
            class Buffer<N:long> : IFixedCollection<N> {
            }
            class Program {
                public static void Main() {
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var narrowingDiag = diagnostics
            .Where(d => d.Id.Value == "MIST_DT_0004")
            .ToList();

        Assert.True(narrowingDiag.Count > 0,
            $"Expected MIST_DT_0004 but got: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.5: Dependent property accessible on abstract type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AbstractWithDependentParam_PropertyAccess_NoDiagnostics()
    {
        const string source = """
            abstract IFixedCollection<Size:int> {
            }
            class Program {
                public static void Main() {
                    int s = IFixedCollection<16>.Size;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var memberErrors = diagnostics
            .Where(d => d.Id.Value is "MIST_TC_0065" or "MIST_UNDEFINED_MEMBER"
                or "MIST_TC_0019" or "MIST_TC_0015" or "MIST_TC_0016")
            .ToList();

        var allDiags = diagnostics.Select(d => $"{d.Id.Value}: {d.Message}").ToList();
        Assert.True(memberErrors.Count == 0, $"Expected no member errors but got: [{string.Join("; ", allDiags)}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Mixed: abstract with both regular and dependent type parameters
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AbstractWithMixedParams_ConcreteValue_NoDiagnostics()
    {
        const string source = """
            abstract ITypedCollection<T, Size:int> {
            }
            class IntBuffer : ITypedCollection<int, 32> {
            }
            class Program {
                public static void Main() {
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value != "MIST_TC_0017")
            .ToList();

        var allDiags = diagnostics.Select(d => $"{d.Id.Value}: {d.Message}").ToList();
        Assert.True(errors.Count == 0, $"Expected no errors but got: [{string.Join("; ", allDiags)}]");
    }

    [Fact]
    public void AbstractWithMixedParams_ForwardedDependent_NoDiagnostics()
    {
        const string source = """
            abstract ITypedCollection<T, Size:int> {
            }
            class GenericBuffer<T, N:int> : ITypedCollection<T, N> {
            }
            class Program {
                public static void Main() {
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value != "MIST_TC_0017")
            .ToList();

        var allDiags = diagnostics.Select(d => $"{d.Id.Value}: {d.Message}").ToList();
        Assert.True(errors.Count == 0, $"Expected no errors but got: [{string.Join("; ", allDiags)}]");
    }
}

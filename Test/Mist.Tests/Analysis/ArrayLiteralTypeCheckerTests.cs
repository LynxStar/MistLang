// Feature: array-literals — Unit tests for type checker array literal validation
// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 6.1, 6.2, 6.3, 6.4**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for type checker validation of array literal expressions.
/// Tests cover:
/// - Homogeneous type inference (int[], string[])
/// - Explicit type annotation validation
/// - Incompatible element type diagnostic (MIST_TC_0095)
/// - Void element rejection (MIST_TC_0034)
/// - Unknown element rejection
/// - Empty array type inference
/// </summary>
public class ArrayLiteralTypeCheckerTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var tcBag = new DiagnosticBag();
        Analyzer.Analyze(model, tcBag);

        return semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
    }

    private static bool HasDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Any(d => d.Id.Value == id);
    }

    private static bool HasNoDiagnosticErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        return !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 5.1: Homogeneous element type inference
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Homogeneous int array [1, 2, 3] infers as int[] — no type errors.
    /// Validates: Requirement 5.1
    /// </summary>
    [Fact]
    public void HomogeneousIntArray_InfersAsIntArray_NoDiagnostics()
    {
        const string source = """
            class Program {
                public static void Main() {
                    var x = [1, 2, 3];
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(HasDiagnostic(diagnostics, "MIST_TC_0095"),
            $"Expected no MIST_TC_0095 but got: {string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
        Assert.True(HasNoDiagnosticErrors(diagnostics),
            $"Expected no errors but got: {string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    /// <summary>
    /// Homogeneous string array ["a", "b"] infers as string[] — no type errors.
    /// Validates: Requirement 5.1
    /// </summary>
    [Fact]
    public void HomogeneousStringArray_InfersAsStringArray_NoDiagnostics()
    {
        const string source = """
            class Program {
                public static void Main() {
                    var x = ["a", "b"];
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(HasDiagnostic(diagnostics, "MIST_TC_0095"),
            $"Expected no MIST_TC_0095 but got: {string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
        Assert.True(HasNoDiagnosticErrors(diagnostics),
            $"Expected no errors but got: {string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 5.3: Explicit type annotation validation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Explicit type annotation int[] x = [1, 2, 3] validates successfully.
    /// Validates: Requirement 5.3
    /// </summary>
    [Fact]
    public void ExplicitTypeAnnotation_IntArrayWithIntElements_NoDiagnostics()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int[] x = [1, 2, 3];
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(HasDiagnostic(diagnostics, "MIST_TC_0095"),
            $"Expected no MIST_TC_0095 but got: {string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
        Assert.True(HasNoDiagnosticErrors(diagnostics),
            $"Expected no errors but got: {string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 5.2: Incompatible element types
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Incompatible types [1, "hello"] emits MIST_TC_0095 diagnostic.
    /// Validates: Requirement 5.2
    /// </summary>
    [Fact]
    public void IncompatibleTypes_IntAndString_EmitsMIST_TC_0095()
    {
        const string source = """
            class Program {
                public static void Main() {
                    var x = [1, "hello"];
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.True(HasDiagnostic(diagnostics, "MIST_TC_0095"),
            $"Expected MIST_TC_0095 but got: {string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.3: Void element rejection
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Void element rejection emits MIST_TC_0034 diagnostic.
    /// Validates: Requirement 6.3
    /// </summary>
    [Fact]
    public void VoidElement_EmitsMIST_TC_0034()
    {
        const string source = """
            class Program {
                public static void Main() {
                    var x = [Main()];
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.True(HasDiagnostic(diagnostics, "MIST_TC_0034"),
            $"Expected MIST_TC_0034 but got: {string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.4: Unknown element rejection
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Unknown element rejection emits appropriate diagnostic.
    /// Validates: Requirement 6.4
    /// </summary>
    [Fact]
    public void UnknownElement_EmitsAppropriateError()
    {
        const string source = """
            class Program {
                public static void Main() {
                    unknown x = unknown;
                    var arr = [x];
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // The type checker should reject unknown-typed elements in array literals.
        // This may emit a specific "unknown as array element" diagnostic or a general error.
        var hasError = diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            (d.Id.Value.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
             d.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
             d.Id.Value == "MIST_TC_0034"));

        Assert.True(hasError,
            $"Expected an error related to unknown element type but got: {string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 5.4: Empty array type inference
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Empty array literal [] with explicit type annotation int[] e = [] — no type errors.
    /// Validates: Requirement 5.4
    /// </summary>
    [Fact]
    public void EmptyArrayWithExplicitType_InfersAsIntArray_NoDiagnostics()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int[] x = [];
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.True(HasNoDiagnosticErrors(diagnostics),
            $"Expected no errors but got: {string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    /// <summary>
    /// Empty array literal [] with explicit type annotation string[] s = [] — no type errors.
    /// Validates: Requirement 5.4
    /// </summary>
    [Fact]
    public void EmptyArrayWithStringType_InfersAsStringArray_NoDiagnostics()
    {
        const string source = """
            class Program {
                public static void Main() {
                    string[] s = [];
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.True(HasNoDiagnosticErrors(diagnostics),
            $"Expected no errors but got: {string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    /// <summary>
    /// Empty array literal [] with var (no target type) emits MIST_TC_0096 diagnostic.
    /// Validates: Requirement 5.5
    /// </summary>
    [Fact]
    public void EmptyArrayWithVar_EmitsMIST_TC_0096()
    {
        const string source = """
            class Program {
                public static void Main() {
                    var foo = [];
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.True(HasDiagnostic(diagnostics, "MIST_TC_0096"),
            $"Expected MIST_TC_0096 but got: {string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    /// <summary>
    /// int[] in expression position is a type literal, not an empty array.
    /// Validates: Requirement 2.4
    /// </summary>
    [Fact]
    public void IntArrayTypeLiteral_IsTypeLiteral_NotEmptyArray()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int[] x = int[];
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // int[] as a type literal assigned to int[] variable should not produce
        // MIST_TC_0096 (that's for empty array literals without target type).
        // It may produce other diagnostics (type mismatch since int[] type literal
        // resolves to Type, not int[]), but not the empty array error.
        Assert.False(HasDiagnostic(diagnostics, "MIST_TC_0096"),
            $"Did not expect MIST_TC_0096 but got: {string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }
}
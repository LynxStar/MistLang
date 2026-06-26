// Feature: binary-literals — Unit tests for binary literal expression contexts
// **Validates: Requirements 2.3, 2.4, 3.1**

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests verifying that binary literals compile without diagnostics
/// in various expression contexts: variable initializer, method argument,
/// and arithmetic expression.
///
/// Uses in-process lexing, parsing, and semantic model building — no process spawning.
///
/// **Validates: Requirements 2.3, 2.4, 3.1**
/// </summary>
public class BinaryLiteralExpressionTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private readonly SemanticModelBuilder _builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        if(bag.HasErrors)
        {
            return null;
        }

        var result = Parser.Parse(tokens, bag);
        return result.Root as CompilationUnitNode;
    }

    private ISemanticModel BuildSemanticModel(
        Storm.Runtime.SyntaxNode ast,
        string source,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var bag = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            bag);
        diagnostics = bag.ToImmutableArray();
        return model;
    }

    private static ImmutableArray<Diagnostic> GetErrorDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 1: Binary literal as variable initializer — Req 2.3, 3.1
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that <c>int mask = 0b1010;</c> inside a method body parses
    /// and builds a semantic model without any error diagnostics.
    ///
    /// **Validates: Requirements 2.3, 3.1**
    /// </summary>
    [Fact]
    public void BinaryLiteral_AsVariableInitializer_CompilesWithoutDiagnostics()
    {
        var source = "class C { public static void Main() { int mask = 0b1010; } }";

        var root = ParseSource(source);
        Assert.NotNull(root);

        var model = BuildSemanticModel(root!, source, out var diagnostics);
        var errors = GetErrorDiagnostics(diagnostics);

        Assert.Empty(errors);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: Binary literal as method argument — Req 2.4
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that passing <c>0b1010</c> as a method argument compiles
    /// without any error diagnostics. The class defines a method accepting
    /// an <c>int</c> parameter, then calls it with a binary literal.
    ///
    /// **Validates: Requirements 2.4**
    /// </summary>
    [Fact]
    public void BinaryLiteral_AsMethodArgument_CompilesWithoutDiagnostics()
    {
        var source = """
            class C {
                public static void foo(int x) { }
                public static void Main() { foo(0b1010); }
            }
            """;

        var root = ParseSource(source);
        Assert.NotNull(root);

        var model = BuildSemanticModel(root!, source, out var diagnostics);
        var errors = GetErrorDiagnostics(diagnostics);

        Assert.Empty(errors);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 3: Binary literal in arithmetic expression — Req 3.1
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that <c>0b1010 + 0b0101</c> in an arithmetic expression
    /// compiles without any error diagnostics.
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Fact]
    public void BinaryLiteral_InArithmeticExpression_CompilesWithoutDiagnostics()
    {
        var source = "class C { public static void Main() { var result = 0b1010 + 0b0101; } }";

        var root = ParseSource(source);
        Assert.NotNull(root);

        var model = BuildSemanticModel(root!, source, out var diagnostics);
        var errors = GetErrorDiagnostics(diagnostics);

        Assert.Empty(errors);
    }
}
// Feature: array-literals — Unit tests for C# emitter array literal output
// **Validates: Requirements 7.1, 7.2, 7.3, 8.1**

using System.Collections.Immutable;
using System.Text;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for C# emitter output of array literal expressions:
/// - Non-empty array literal emits new[] { ... } (Requirement 7.1)
/// - Nested array literals emit nested new[] { ... } (Requirement 7.3)
/// - Explicit type annotation emits new T[] { ... } (Requirement 7.2)
/// - Empty array literal emits Array.Empty&lt;T&gt;() (Requirement 8.1)
///
/// Uses in-memory compilation via <see cref="CSharpEmitter.EmitToMemory"/>
/// (no disk I/O, no process spawning).
///
/// **Validates: Requirements 7.1, 7.2, 7.3, 8.1**
/// </summary>
public sealed class ArrayLiteralEmitterTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── In-memory compilation helper ────────────────────────────────────

    private static string CompileAndGetEmittedCSharp(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        // Lex
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, $"Lex errors: {FormatDiagnostics(lexBag)}");

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, $"Parse errors: {FormatDiagnostics(parseBag)}");
        Assert.NotNull(parseResult.Root);

        // Semantic model
        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors, $"Semantic errors: {FormatDiagnostics(semBag)}");

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        var tcErrors = tcBag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(tcErrors);

        // Emit in-memory
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, emitBag);
        Assert.False(emitBag.HasErrors, $"Emit errors: {FormatDiagnostics(emitBag)}");

        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            _ = sb.AppendLine(content);
        }

        return sb.ToString();
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a non-empty array literal [1, 2, 3] emits as
    /// new[] { 1, 2, 3 } in the C# output.
    ///
    /// **Validates: Requirement 7.1**
    /// </summary>
    [Fact]
    public void NonEmptyArrayLiteral_EmitsNewArraySyntax()
    {
        var mistSource = """
            class Program {
                static void Main() {
                    var x = [1, 2, 3];
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        Assert.Contains("new[] { 1, 2, 3 }", emittedCSharp);
    }

    /// <summary>
    /// Verifies that nested array literals [[1, 2], [3, 4]] emit as
    /// new[] { new[] { 1, 2 }, new[] { 3, 4 } } in the C# output.
    ///
    /// **Validates: Requirement 7.3**
    /// </summary>
    [Fact]
    public void NestedArrayLiteral_EmitsNestedNewArraySyntax()
    {
        var mistSource = """
            class Program {
                static void Main() {
                    var x = [[1, 2], [3, 4]];
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        Assert.Contains("new[] { new[] { 1, 2 }, new[] { 3, 4 } }", emittedCSharp);
    }

    /// <summary>
    /// Verifies that an array literal with explicit type annotation
    /// int[] x = [1, 2, 3] emits as int[] x = new int[] { 1, 2, 3 }.
    ///
    /// **Validates: Requirement 7.2**
    /// </summary>
    [Fact]
    public void ExplicitTypeAnnotation_EmitsTypedNewArraySyntax()
    {
        var mistSource = """
            class Program {
                static void Main() {
                    int[] x = [1, 2, 3];
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        Assert.Contains("int[] x = new int[] { 1, 2, 3 }", emittedCSharp);
    }

    /// <summary>
    /// Verifies that an empty array literal [] with explicit type annotation
    /// int[] x = [] emits as Array.Empty&lt;int&gt;() in the C# output.
    ///
    /// **Validates: Requirement 8.1**
    /// </summary>
    [Fact]
    public void EmptyArrayLiteral_EmitsArrayEmpty()
    {
        var mistSource = """
            class Program {
                static void Main() {
                    int[] x = [];
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        Assert.Contains("Array.Empty<int>()", emittedCSharp);
    }

    /// <summary>
    /// Verifies that int[] in expression position (type literal) emits as
    /// typeof(int[]) in the C# output, NOT as Array.Empty&lt;int&gt;().
    ///
    /// **Validates: Requirement 2.4**
    /// </summary>
    [Fact]
    public void TypeLiteralIntArray_EmitsTypeOf_NotArrayEmpty()
    {
        var mistSource = """
            class Program {
                static void Main() {
                    var t = int[];
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        Assert.Contains("typeof(int[])", emittedCSharp);
        Assert.DoesNotContain("Array.Empty", emittedCSharp);
    }
}
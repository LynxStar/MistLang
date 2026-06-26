// Feature: unknown-type (Phase D, Task 25.4)
// **Validates: Requirements 13.1, 13.2, 13.3**
//
// Unit tests for Task 25: the Type Checker rejects member access and
// operator application on a bare-`unknown`-typed operand, and leaves
// `is`-expression narrowing (Req 13.3, the designated escape) alone.
//
// Diagnostics under test:
//   * `MIST_TC_0065` — bare-unknown member access (Req 13.1).
//     Reallocated from the design-document-proposed `MIST_TC_0053`,
//     which was already in use by the pattern-matching feature's
//     `PatternUndefinedProperty` diagnostic.
//   * `MIST_TC_0066` — bare-unknown operator (Req 13.2). Reallocated
//     from the design-document-proposed `MIST_TC_0054`, already in
//     use by `PatternListRequiresArray`.
// See the `BareUnknownMemberAccess` / `BareUnknownOperator`
// allocation comments in `TypeCheckerAnalyzer.cs`.
//
// These tests run fully in-process via the shared
// `SemanticModelBuilder` + `TypeCheckerAnalyzer` — no `dotnet` process
// is spawned, per the workspace test-process-spawning policy.

using System.Collections.Immutable;
using System.Linq;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for the bare-unknown rejection rules introduced by
/// Task 25. Each test compiles a small Mist program that exercises a
/// member access or operator application on an <c>unknown</c>-typed
/// operand, runs the type checker, and asserts the presence or
/// absence of the appropriate diagnostic code.
/// </summary>
public class UnknownBareRejectionTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

    /// <summary>
    /// Diagnostic code emitted when a member access has a receiver of
    /// exactly <c>UnknownType</c> (Req 13.1). Reallocated from the
    /// design-document-proposed <c>MIST_TC_0053</c>, already owned by
    /// <c>PatternUndefinedProperty</c>.
    /// </summary>
    private const string BareUnknownMemberAccessDiagnosticId = "MIST_TC_0065";

    /// <summary>
    /// Diagnostic code emitted when an operator is applied to an
    /// operand of <c>UnknownType</c> (Req 13.2). Reallocated from the
    /// design-document-proposed <c>MIST_TC_0054</c>, already owned by
    /// <c>PatternListRequiresArray</c>.
    /// </summary>
    private const string BareUnknownOperatorDiagnosticId = "MIST_TC_0066";

    /// <summary>
    /// Lexes, parses, builds the semantic model, then runs the type
    /// checker against <paramref name="source"/>. Returns the combined
    /// diagnostic set from the semantic model builder and the type
    /// checker.
    /// </summary>
    private ImmutableArray<Diagnostic> CompileAndCollectDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, "Lex should not produce errors");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, "Parse should not produce errors");
        Assert.NotNull(parseResult.Root);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        var semBag = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var tcBag = new DiagnosticBag();
        analyzer.Analyze(model, tcBag);

        return semBag.ToImmutableArray().Concat(tcBag.ToImmutableArray()).ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 25.4 bullet 1 — `fn f(x: unknown) { x.M() }` → MIST_TC_0065
    //
    // Req 13.1: a member access on a receiver whose static type is
    // exactly `UnknownType` emits the bare-unknown diagnostic naming
    // the receiver's static type, regardless of the member name —
    // `UnknownType` has no members (Req 3.4).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodCall_OnBareUnknown_IsRejected()
    {
        const string source =
            "class Foo {\n" +
            "    public void M(unknown x) {\n" +
            "        x.M();\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, BareUnknownMemberAccessDiagnosticId);

        _ = Assert.Single(hits);
        var d = hits[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        // Message names the member and the receiver's static type
        // ('unknown'), and suggests using an 'is' pattern (Req 16.4).
        Assert.Contains("M", d.Message);
        Assert.Contains("unknown", d.Message);
        Assert.Contains("is", d.Message);
    }

    /// <summary>
    /// The member-access form (<c>x.Name</c> — no parentheses) is
    /// rejected identically, because it routes through the same
    /// <c>CheckPostfixNode</c> logic as a method call.
    /// </summary>
    [Fact]
    public void MemberAccess_OnBareUnknown_IsRejected()
    {
        const string source =
            "class Foo {\n" +
            "    public int M(unknown x) {\n" +
            "        return x.Field;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, BareUnknownMemberAccessDiagnosticId);

        _ = Assert.Single(hits);
        Assert.Contains("Field", hits[0].Message);
        Assert.Contains("unknown", hits[0].Message);
    }

    /// <summary>
    /// An indexer access on a bare-<c>unknown</c> receiver is also a
    /// member access for the purposes of Req 13.1 and routes through
    /// the same <c>CheckPostfixNode</c> branch — the diagnostic fires
    /// with the canonical indexer lexeme <c>this[]</c>.
    /// </summary>
    [Fact]
    public void IndexerAccess_OnBareUnknown_IsRejected()
    {
        const string source =
            "class Foo {\n" +
            "    public int M(unknown x) {\n" +
            "        return x[0];\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, BareUnknownMemberAccessDiagnosticId);

        _ = Assert.Single(hits);
        Assert.Contains("unknown", hits[0].Message);
    }

    /// <summary>
    /// Counter-example: the bare-unknown diagnostic does <em>not</em>
    /// fire on a <c>Closed_Generic_With_Unknown</c> receiver. That
    /// case is owned by Task 21's member-filtering diagnostic
    /// (<c>MIST_TC_0062</c>) and the two rules must not overlap.
    /// </summary>
    [Fact]
    public void MemberAccess_OnClosedGenericWithUnknown_DoesNotFireBareUnknown()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M(List<unknown> xs) {\n" +
            "        xs.Add(0);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);

        // Task 25's bare-unknown diagnostic must NOT fire on a
        // List<unknown> receiver.
        Assert.Empty(WithId(diagnostics, BareUnknownMemberAccessDiagnosticId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 25.4 bullet 2 — `fn f(x: unknown, y: unknown) { x op y }` →
    // MIST_TC_0066 for every operator listed in Req 13.2.
    //
    // Req 13.2: a binary or unary operator applied to any `unknown`
    // operand emits the diagnostic. `UnknownType` has no declared
    // members (Req 3.4), so no user-defined operator can rescue the
    // expression.
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    // Arithmetic operators.
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("*")]
    [InlineData("/")]
    [InlineData("%")]
    // Equality operators.
    [InlineData("==")]
    [InlineData("!=")]
    // Relational operators.
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("<=")]
    [InlineData(">=")]
    // Short-circuit logical operators.
    [InlineData("&&")]
    [InlineData("||")]
    public void BinaryOperator_OnBareUnknownOperands_IsRejected(string op)
    {
        // Both operands are `unknown`. The operator must be rejected
        // with MIST_TC_0066 regardless of which specific operator is
        // applied (Req 13.2).
        var source =
            "class Foo {\n" +
            "    public void M(unknown x, unknown y) {\n" +
            $"        var r = x {op} y;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, BareUnknownOperatorDiagnosticId);

        Assert.NotEmpty(hits);
        var d = hits[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        // Message names the operator, the operand type(s), and
        // suggests using an `is` pattern (Req 16.4).
        Assert.Contains(op, d.Message);
        Assert.Contains("unknown", d.Message);
        Assert.Contains("is", d.Message);
    }

    /// <summary>
    /// The rule fires when only one operand is <c>unknown</c> — the
    /// other may be any concrete type. This exercises the asymmetric
    /// case of Req 13.2 ("when any operand's static type is
    /// UnknownType").
    /// </summary>
    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("==")]
    [InlineData("<")]
    public void BinaryOperator_WithOneUnknownOperand_IsRejected(string op)
    {
        var source =
            "class Foo {\n" +
            "    public void M(unknown x, int y) {\n" +
            $"        var r = x {op} y;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.NotEmpty(WithId(diagnostics, BareUnknownOperatorDiagnosticId));
    }

    /// <summary>
    /// Symmetric counterpart: <c>int</c> on the left, <c>unknown</c>
    /// on the right. The rule is symmetric in Req 13.2 ("any operand").
    /// </summary>
    [Fact]
    public void BinaryOperator_WithUnknownOnRight_IsRejected()
    {
        const string source =
            "class Foo {\n" +
            "    public void M(int x, unknown y) {\n" +
            "        var r = x + y;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.NotEmpty(WithId(diagnostics, BareUnknownOperatorDiagnosticId));
    }

    [Theory]
    // Unary logical negation.
    [InlineData("!")]
    // Unary arithmetic negation.
    [InlineData("-")]
    public void UnaryOperator_OnBareUnknown_IsRejected(string op)
    {
        var source =
            "class Foo {\n" +
            "    public void M(unknown x) {\n" +
            $"        var r = {op}x;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, BareUnknownOperatorDiagnosticId);

        Assert.NotEmpty(hits);
        var d = hits[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains(op, d.Message);
        Assert.Contains("unknown", d.Message);
        Assert.Contains("is", d.Message);
    }

    /// <summary>
    /// User-defined operators on <c>unknown</c> also fail, because
    /// <c>UnknownType</c> exposes no member scope — no operator
    /// symbol can ever be found (Req 3.4, Req 13.2). This is
    /// observationally indistinguishable from the rejection of a
    /// built-in operator but is worth an explicit test.
    /// </summary>
    [Fact]
    public void UserDefinedOperator_OnBareUnknown_IsRejected()
    {
        // Foo declares `operator+` that takes two Foo operands. Even
        // though such an operator exists in the program, it cannot
        // apply to `unknown` because no member is visible through the
        // `unknown` scope.
        const string source =
            "class Foo {\n" +
            "    public Foo operator+(Foo a, Foo b) { return a; }\n" +
            "    public void M(unknown x, unknown y) {\n" +
            "        var r = x + y;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.NotEmpty(WithId(diagnostics, BareUnknownOperatorDiagnosticId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 25.4 bullet 3 — `fn f(x: unknown) { x is int i; i + 1 }` → accepted
    //
    // Req 13.3: `is`-expression handling on bare `unknown` is the
    // designated escape mechanism. After narrowing, the bound
    // identifier has the concrete type and participates in normal
    // operator resolution (Req 11.1, 11.2).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsPatternNarrowing_OnBareUnknown_ThenOperator_IsAccepted()
    {
        // The scrutinee is `unknown` and the pattern narrows to
        // `int`, which is the designated escape mechanism. Inside
        // the `if` body, `i` has static type `int`, so the `+`
        // operator resolves normally and no MIST_TC_0066 is emitted.
        const string source =
            "class Foo {\n" +
            "    public void M(unknown x) {\n" +
            "        if (x is int i) {\n" +
            "            var r = i + 1;\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);

        // Neither bare-unknown diagnostic fires on the narrowed
        // binding — `i` is `int`, not `unknown`.
        Assert.Empty(WithId(diagnostics, BareUnknownMemberAccessDiagnosticId));
        Assert.Empty(WithId(diagnostics, BareUnknownOperatorDiagnosticId));

        // The original `is` expression is itself accepted — Task 24
        // does not reject narrowing from `unknown` to `int`.
        Assert.Empty(WithId(diagnostics, "MIST_TC_0063"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0064"));
    }

    /// <summary>
    /// After <c>is int i</c> narrowing, calling a method on the
    /// narrowed binding is also accepted — member access routes
    /// through the same postfix logic, and the narrowed binding has
    /// a concrete static type.
    /// </summary>
    [Fact]
    public void IsPatternNarrowing_OnBareUnknown_ThenMemberAccess_IsAccepted()
    {
        const string source =
            "class Bar {\n" +
            "    public int V;\n" +
            "    public int GetV() { return this.V; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M(unknown x) {\n" +
            "        if (x is Bar b) {\n" +
            "            var r = b.GetV();\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, BareUnknownMemberAccessDiagnosticId));
    }

    /// <summary>
    /// The <c>is</c>-expression itself on a bare-<c>unknown</c>
    /// receiver is not the member-access rule's concern (Req 13.3)
    /// and must not emit <c>MIST_TC_0065</c>. Narrowing is the
    /// designated escape.
    /// </summary>
    [Fact]
    public void IsExpression_OnBareUnknown_DoesNotFireMemberAccessRejection()
    {
        const string source =
            "class Foo {\n" +
            "    public bool M(unknown x) {\n" +
            "        return x is int i;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, BareUnknownMemberAccessDiagnosticId));
        Assert.Empty(WithId(diagnostics, BareUnknownOperatorDiagnosticId));
    }
}
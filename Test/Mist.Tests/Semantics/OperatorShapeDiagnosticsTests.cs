using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for Task 12 of the Mist Standard Library spec:
/// <c>Pass1e_ValidateOperatorShape</c> emits three new diagnostics on
/// adversarial operator declarations and stays silent on the
/// correct-usage counter-examples.
///
/// Validates:
/// * Requirement 4.7 — <c>MIST_SB_0033</c> is allocated for operators
///   declared outside a class or struct body. The Mist grammar restricts
///   <c>OperatorDeclaration</c> to <c>MemberDeclaration</c>, so this
///   diagnostic is currently unreachable from parsed source. The
///   diagnostic ID is still registered so a future grammar relaxation
///   inherits the validation automatically.
/// * Requirements 4.8 &amp; 4.9 — <c>MIST_SB_0034</c> fires when a binary
///   operator does not declare exactly 2 parameters, or when a unary
///   operator does not declare exactly 1.
/// * Requirement 4.2 — <c>MIST_SB_0035</c> fires when an operator is
///   missing either <c>public</c> or <c>static</c>.
/// * Requirements 14.6 &amp; 14.7 — the diagnostic IDs and their message
///   forms match the design document (§ <c>Error Handling and Diagnostics</c>).
/// </summary>
public class OperatorShapeDiagnosticsTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder builder = new();

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, runs the semantic model
    /// builder, and returns the diagnostics it produced.
    /// </summary>
    private ImmutableArray<Diagnostic> BuildAndCollectDiagnostics(string source)
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
        _ = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        return semBag.ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ─── MIST_SB_0034 — arity mismatch (Requirements 4.8, 4.9) ───────────

    [Fact]
    public void MIST_SB_0034_FiresOnBinaryPlusWithOneParameter()
    {
        // Adversarial: `operator +` is binary and requires exactly 2
        // parameters; declaring one parameter must fire 0034.
        var source =
            "public struct Vec { " +
            "public static Vec operator +(Vec a) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var arityDiags = WithId(diagnostics, "MIST_SB_0034");

        _ = Assert.Single(arityDiags);
        var d = arityDiags[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal(
            "Binary operator '+' must declare exactly 2 parameters; found 1.",
            d.Message);
    }

    [Fact]
    public void MIST_SB_0034_FiresOnUnaryNotWithTwoParameters()
    {
        // Adversarial: `operator !` is unary and requires exactly 1
        // parameter; declaring two parameters must fire 0034 with a
        // unary-arity message.
        var source =
            "public struct Flag { " +
            "public static Flag operator !(Flag a, Flag b) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var arityDiags = WithId(diagnostics, "MIST_SB_0034");

        _ = Assert.Single(arityDiags);
        var d = arityDiags[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal(
            "Unary operator '!' must declare exactly 1 parameter; found 2.",
            d.Message);
    }

    [Fact]
    public void MIST_SB_0034_DoesNotFireOnBinaryMinusWithTwoParameters()
    {
        // Counter-example: `operator -` with 2 parameters is a legal
        // binary subtraction operator — arity matches.
        var source =
            "public struct Vec { " +
            "public static Vec operator -(Vec a, Vec b) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0034"));
    }

    [Fact]
    public void MIST_SB_0034_DoesNotFireOnUnaryMinusWithOneParameter()
    {
        // Counter-example: `operator -` with 1 parameter is a legal unary
        // negation operator. The arity disambiguation rule for `-` must
        // select unary arity (1 expected).
        var source =
            "public struct Vec { " +
            "public static Vec operator -(Vec a) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0034"));
    }

    [Fact]
    public void MIST_SB_0034_FiresOnBinaryEqualsWithThreeParameters()
    {
        // Adversarial: `operator ==` is binary; three parameters trips
        // the binary-arity diagnostic.
        var source =
            "public struct Vec { " +
            "public static Vec operator ==(Vec a, Vec b, Vec c) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var arityDiags = WithId(diagnostics, "MIST_SB_0034");

        _ = Assert.Single(arityDiags);
        Assert.Equal(
            "Binary operator '==' must declare exactly 2 parameters; found 3.",
            arityDiags[0].Message);
    }

    // ─── MIST_SB_0035 — operator must be public static (Requirement 4.2) ─

    [Fact]
    public void MIST_SB_0035_FiresWhenOperatorMissingPublic()
    {
        // Adversarial: operator declared `static` only (no `public`).
        var source =
            "public struct Vec { " +
            "static Vec operator +(Vec a, Vec b) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var publicStaticDiags = WithId(diagnostics, "MIST_SB_0035");

        _ = Assert.Single(publicStaticDiags);
        var d = publicStaticDiags[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal(
            "Operator '+' must be declared 'public static'.",
            d.Message);
    }

    [Fact]
    public void MIST_SB_0035_FiresWhenOperatorMissingStatic()
    {
        // Adversarial: operator declared `public` only (no `static`).
        var source =
            "public struct Vec { " +
            "public Vec operator +(Vec a, Vec b) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var publicStaticDiags = WithId(diagnostics, "MIST_SB_0035");

        _ = Assert.Single(publicStaticDiags);
        Assert.Equal(
            "Operator '+' must be declared 'public static'.",
            publicStaticDiags[0].Message);
    }

    [Fact]
    public void MIST_SB_0035_FiresWhenOperatorHasNoModifiers()
    {
        // Adversarial: operator declared with no modifiers at all.
        var source =
            "public struct Vec { " +
            "Vec operator +(Vec a, Vec b) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var publicStaticDiags = WithId(diagnostics, "MIST_SB_0035");

        _ = Assert.Single(publicStaticDiags);
    }

    [Fact]
    public void MIST_SB_0035_DoesNotFireWhenOperatorIsPublicStatic()
    {
        // Counter-example: operator declared `public static` — the required
        // shape per Requirement 4.2 — must not fire 0035.
        var source =
            "public struct Vec { " +
            "public static Vec operator +(Vec a, Vec b) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0035"));
    }

    // ─── MIST_SB_0033 — operator outside a class/struct body (Req 4.7) ────

    [Fact]
    public void MIST_SB_0033_DoesNotFireOnOperatorInsideStructBody()
    {
        // The grammar only admits operator declarations inside a class or
        // struct body, so 0033 is unreachable from parsed source today.
        // This test pins the defensive behaviour: a legal in-body operator
        // never trips 0033 regardless of other shape diagnostics.
        var source =
            "public struct Vec { " +
            "public static Vec operator +(Vec a, Vec b) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0033"));
    }

    [Fact]
    public void MIST_SB_0033_DoesNotFireOnOperatorInsideClassBody()
    {
        // Counter-example on a class instead of a struct: still no 0033,
        // because the operator is inside a registered type's member scope.
        var source =
            "public class Wrap { " +
            "public static Wrap operator +(Wrap a, Wrap b) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0033"));
    }

    // ─── Correct-usage counter-example: all three diagnostics silent ─────

    [Fact]
    public void CorrectPublicStaticBinaryOperator_DoesNotFireAnyShapeDiagnostic()
    {
        // The canonical correct shape for a user-defined operator per
        // Requirements 4.1, 4.2, 4.3: `public static` with exactly 2
        // parameters on a binary operator, declared as a class/struct
        // member. None of MIST_SB_0033/0034/0035 may fire.
        var source =
            "public struct Vec { " +
            "public static Vec operator +(Vec a, Vec b) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_SB_0033"));
        Assert.Empty(WithId(diagnostics, "MIST_SB_0034"));
        Assert.Empty(WithId(diagnostics, "MIST_SB_0035"));
    }

    [Fact]
    public void CorrectPublicStaticUnaryOperator_DoesNotFireAnyShapeDiagnostic()
    {
        // The canonical correct shape for a unary operator: `public static`
        // with exactly 1 parameter. Uses unary `-` (legal per
        // Requirement 4.4); the `-` arity disambiguation must pick unary
        // when there is one declared parameter.
        var source =
            "public struct Vec { " +
            "public static Vec operator -(Vec a) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_SB_0033"));
        Assert.Empty(WithId(diagnostics, "MIST_SB_0034"));
        Assert.Empty(WithId(diagnostics, "MIST_SB_0035"));
    }
}
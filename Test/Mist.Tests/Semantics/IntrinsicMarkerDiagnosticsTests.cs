using System.Collections.Immutable;
using System.Linq;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for Task 9 of the Mist Standard Library spec:
/// <c>Pass1e_ValidateIntrinsicMarkers</c> emits three new diagnostics on
/// adversarial uses of the <c>intrinsic</c> modifier and stays silent on
/// the design document's correct-usage counter-example.
///
/// Validates:
/// * Requirement 5.3  — <c>MIST_SB_0030</c> fires when an intrinsic member
///   declaration carries a body.
/// * Requirements 5.6 &amp; 5.13 — <c>MIST_SB_0031</c> fires on any use of
///   <c>intrinsic</c> in a user (non-stdlib) compilation unit, at both
///   type and member level. <c>CompilationUnit.IsStdlib</c> lands in Task 26;
///   until then every unit is treated as non-stdlib so every <c>intrinsic</c>
///   use trips this diagnostic.
/// * Requirement 5.15 — <c>MIST_SB_0032</c> fires when a member-level
///   <c>intrinsic</c> sits inside a containing type that is not itself
///   declared <c>intrinsic</c>.
/// * Requirements 14.3–14.5 — the three diagnostic IDs and their message
///   forms match the design document (§ <c>Error Handling and Diagnostics</c>).
/// </summary>
public class IntrinsicMarkerDiagnosticsTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder _builder = new();

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
        _builder.Build(
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

    // ── MIST_SB_0030 — intrinsic member with a body (Requirement 5.3) ──────

    [Fact]
    public void MIST_SB_0030_FiresOnIntrinsicMemberWithABody()
    {
        // Adversarial: the design § Error Handling table lists this exact
        // trigger — `intrinsic public static Int32 operator +(Int32 a, Int32 b) { return 0; }`.
        var source =
            "intrinsic public struct Int32 { " +
            "intrinsic public static Int32 operator +(Int32 a, Int32 b) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var zeroThirties = WithId(diagnostics, "MIST_SB_0030");

        Assert.Single(zeroThirties);
        var d = zeroThirties[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Int32.operator+", d.Message);
        Assert.Contains("must not have a body", d.Message);
        Assert.Contains("';'", d.Message);
    }

    [Fact]
    public void MIST_SB_0030_DoesNotFireWhenIntrinsicMemberHasNoBody()
    {
        // Correct counter-example: intrinsic operator with `;` (no body).
        var source =
            "intrinsic public struct Int32 { " +
            "intrinsic public static Int32 operator +(Int32 a, Int32 b); " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0030"));
    }

    [Fact]
    public void MIST_SB_0030_FiresOnIntrinsicMethodWithABody()
    {
        var source =
            "intrinsic public struct Int32 { " +
            "intrinsic public Int32 Next() { return 0; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var zeroThirties = WithId(diagnostics, "MIST_SB_0030");

        Assert.Single(zeroThirties);
        Assert.Contains("Int32.Next", zeroThirties[0].Message);
    }

    [Fact]
    public void MIST_SB_0030_FiresOnIntrinsicConstructorWithABody()
    {
        var source =
            "intrinsic public struct Int32 { " +
            "intrinsic public Int32() { } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var zeroThirties = WithId(diagnostics, "MIST_SB_0030");

        Assert.Single(zeroThirties);
        Assert.Contains("Int32.Int32", zeroThirties[0].Message);
    }

    // ── MIST_SB_0031 — 'intrinsic' used in user code (Requirements 5.6, 5.13) ──

    [Fact]
    public void MIST_SB_0031_FiresOnTypeLevelIntrinsicInUserCode()
    {
        // Adversarial: design § Error Handling lists this exact trigger —
        // `intrinsic public struct MyThing { }` in user code.
        var source = "intrinsic public struct MyThing { }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var zeroThirtyOnes = WithId(diagnostics, "MIST_SB_0031");

        Assert.Single(zeroThirtyOnes);
        var d = zeroThirtyOnes[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal(
            "The 'intrinsic' modifier is reserved for the Standard Library and cannot be used in user code.",
            d.Message);
    }

    [Fact]
    public void MIST_SB_0031_FiresOnMemberLevelIntrinsicInUserCode()
    {
        // A non-intrinsic container with an intrinsic member: the 'intrinsic'
        // modifier still shows up in user code, so MIST_SB_0031 must fire
        // (in addition to MIST_SB_0032; both diagnostics are independent).
        var source =
            "public struct NotIntrinsic { " +
            "intrinsic public Int32 Foo(); " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var zeroThirtyOnes = WithId(diagnostics, "MIST_SB_0031");

        Assert.Single(zeroThirtyOnes);
        Assert.Equal(
            "The 'intrinsic' modifier is reserved for the Standard Library and cannot be used in user code.",
            zeroThirtyOnes[0].Message);
    }

    [Fact]
    public void MIST_SB_0031_FiresOnBothTypeAndMemberUses()
    {
        // When both markers are present in user code, 0031 fires once per
        // declaration (type-level + member-level = 2 occurrences).
        var source =
            "intrinsic public struct Int32 { " +
            "intrinsic public static Int32 operator +(Int32 a, Int32 b); " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var zeroThirtyOnes = WithId(diagnostics, "MIST_SB_0031");

        Assert.Equal(2, zeroThirtyOnes.Length);
        Assert.All(
            zeroThirtyOnes,
            d => Assert.Equal(
                "The 'intrinsic' modifier is reserved for the Standard Library and cannot be used in user code.",
                d.Message));
    }

    [Fact]
    public void MIST_SB_0031_DoesNotFireOnOrdinaryUserCode()
    {
        // Counter-example: no 'intrinsic' anywhere → 0031 never fires.
        var source =
            "public struct Vec { " +
            "public Int32 x; " +
            "public static Vec operator +(Vec a, Vec b) { return a; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0031"));
    }

    // ── MIST_SB_0032 — member-level intrinsic inside a non-intrinsic type ──
    //     (Requirement 5.15)

    [Fact]
    public void MIST_SB_0032_FiresWhenContainingTypeIsNotIntrinsic()
    {
        // Adversarial: design § Error Handling lists this exact trigger —
        // a non-intrinsic class in stdlib contains `intrinsic public void Foo();`.
        // In user code the same pattern produces both MIST_SB_0031 and
        // MIST_SB_0032; this test pins down only the 5.15 pairing rule.
        var source =
            "public struct NotIntrinsic { " +
            "intrinsic public Int32 Foo(); " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var zeroThirtyTwos = WithId(diagnostics, "MIST_SB_0032");

        Assert.Single(zeroThirtyTwos);
        var d = zeroThirtyTwos[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Member-level 'intrinsic'", d.Message);
        Assert.Contains("Type 'NotIntrinsic' is not intrinsic", d.Message);
    }

    [Fact]
    public void MIST_SB_0032_DoesNotFireWhenContainingTypeIsIntrinsic()
    {
        // Correct counter-example: an intrinsic member in an intrinsic type
        // is permitted — the 5.15 pairing rule is satisfied.
        var source =
            "intrinsic public struct Int32 { " +
            "intrinsic public static Int32 operator +(Int32 a, Int32 b); " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0032"));
    }

    [Fact]
    public void MIST_SB_0032_DoesNotFireOnOrdinaryMemberInNonIntrinsicType()
    {
        // Counter-example: a non-intrinsic member in a non-intrinsic type
        // is ordinary Mist code and cannot trigger the 5.15 pairing rule.
        var source =
            "public struct Ordinary { " +
            "public Int32 Foo() { return 0; } " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0032"));
    }

    [Fact]
    public void MIST_SB_0032_FiresPerIntrinsicMember()
    {
        // Two intrinsic members inside a non-intrinsic type → two 0032s.
        var source =
            "public class Container { " +
            "intrinsic public Int32 A(); " +
            "intrinsic public Int32 B(); " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var zeroThirtyTwos = WithId(diagnostics, "MIST_SB_0032");

        Assert.Equal(2, zeroThirtyTwos.Length);
        Assert.All(
            zeroThirtyTwos,
            d => Assert.Contains("Type 'Container' is not intrinsic", d.Message));
    }

    // ── Correct-usage counter-example (design § Error Handling) ───────────

    [Fact]
    public void CorrectIntrinsicStdlibShape_DoesNotFire_0030_Or_0032()
    {
        // The stdlib's intended shape for a bootstrap type — a type-level
        // 'intrinsic' with member-level 'intrinsic' on each operator and no
        // body. Per the task, MIST_SB_0031 still fires today because
        // CompilationUnit.IsStdlib is not yet modelled (Task 26 will make it
        // suppress 0031 on stdlib units), but neither 0030 nor 0032 should
        // fire: the bodies are absent and the containing type is intrinsic.
        var source =
            "intrinsic public struct Int32 { " +
            "intrinsic public static Int32 operator +(Int32 a, Int32 b); " +
            "intrinsic public static Int32 operator -(Int32 a, Int32 b); " +
            "intrinsic public static Int32 operator -(Int32 a); " +
            "}";

        var diagnostics = BuildAndCollectDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_SB_0030"));
        Assert.Empty(WithId(diagnostics, "MIST_SB_0032"));
    }
}

// Feature: unknown-type (Phase D, Task 26.3)
// **Validates: Requirements 13.4, 13.5, 13.6**
//
// Unit tests for Task 26: the Type Checker resolves cast expressions
// whose operand is typed `unknown` via the cast-operators feature.
//
// Three sub-rules exercised here:
//
//   * `(T) someUnknown` where `T` declares `implicit operator T(unknown)`
//     (or the `explicit` form) resolves to the operator and records it
//     on the cast expression (Task 26.1 / Req 13.4).
//
//   * `(T) someUnknown` where `T` declares no such operator emits
//     `MIST_TC_0067` — the targeted "no cast operator from unknown"
//     diagnostic. Reallocated from the design-document-proposed
//     `MIST_TC_0055`, which was already in use by the pattern-matching
//     feature's `PatternListTooManyElements` (a warning). See the
//     `NoCastOperatorFromUnknown` allocation comment in
//     `TypeCheckerAnalyzer.cs` for the full reallocation rationale.
//
//   * `(unknown) 42` (and `(unknown) expr` for any non-`void` `expr`)
//     is accepted without any operator search because `UnknownType` is
//     the user-facing top type (Req 4.1 / Req 13.5). The top-type rule
//     now lives in `IsDirectlyAssignable` (Task 26.2) so the cast
//     expression path and the assignment path agree.
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
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for cast-operator integration with the user-facing
/// <c>unknown</c> type introduced by Task 26. Each test compiles a
/// small Mist program that exercises a cast expression whose operand
/// or target is <c>unknown</c>, runs the type checker, and asserts
/// the presence or absence of the appropriate diagnostic code.
/// </summary>
public class UnknownCastIntegrationTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    /// <summary>
    /// Diagnostic code emitted when a cast <c>(T) expr</c> has an
    /// operand of exactly <see cref="Mist.Semantics.Model.Types.UnknownType"/>
    /// and no implicit or explicit operator <c>T(unknown)</c> is declared
    /// on <c>T</c> (Req 13.4). Reallocated from the design-document-
    /// proposed <c>MIST_TC_0055</c>, already owned by the pattern-matching
    /// feature's <c>PatternListTooManyElements</c>.
    /// </summary>
    private const string NoCastOperatorFromUnknownDiagnosticId = "MIST_TC_0067";

    /// <summary>
    /// Diagnostic code emitted when the cast-operator resolution falls
    /// back to the generic "no cast operator" case. We assert it is
    /// <em>not</em> emitted for the unknown-source case — the targeted
    /// <c>MIST_TC_0067</c> must fire instead.
    /// </summary>
    private const string GenericNoCastOperatorDiagnosticId = "MIST_TC_0042";

    /// <summary>
    /// Diagnostic code emitted when multiple cast operators are
    /// applicable to the same cast expression.
    /// </summary>
    private const string AmbiguousCastDiagnosticId = "MIST_TC_0043";

    /// <summary>
    /// Lexes, parses, builds the semantic model, then runs the type
    /// checker against <paramref name="source"/>. Returns the combined
    /// diagnostic set from the semantic model builder and the type
    /// checker.
    /// </summary>
    private (ImmutableArray<Diagnostic> Diagnostics, ISemanticModel Model) CompileAndCollect(string source)
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
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var tcBag = new DiagnosticBag();
        _analyzer.Analyze(model, tcBag);

        var all = semBag.ToImmutableArray().Concat(tcBag.ToImmutableArray()).ToImmutableArray();
        return (all, model);
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 26.3 bullet 1 — `class MyInt { implicit operator MyInt(unknown u) { ... } }`
    // `(MyInt) someUnknown` resolves to the implicit operator.
    //
    // Req 13.4: a cast `(T) expr` where `expr` is typed `unknown` must
    // search for a cast operator whose source type is `unknown` and
    // target is `T`. Because `UnknownType` has no member scope
    // (Req 3.4), only `T`'s scope is searched — which is exactly where
    // the operator is declared per the cast-operators feature's
    // "enclosing type" rule.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An implicit operator <c>MyInt(unknown)</c> declared on <c>MyInt</c>
    /// must be discovered by the cast-expression path, and no cast
    /// diagnostic should fire.
    /// </summary>
    [Fact]
    public void CastFromUnknown_WithImplicitOperatorOnTarget_Resolves()
    {
        // `MyInt` declares `implicit operator MyInt(unknown u)`. The
        // cast `(MyInt) x` where `x: unknown` must resolve to this
        // operator without emitting any cast diagnostic.
        const string source =
            "class MyInt {\n" +
            "    public int value;\n" +
            "    public MyInt(int v) { this.value = v; }\n" +
            "    public static implicit operator MyInt(unknown u) {\n" +
            "        return new MyInt(0);\n" +
            "    }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public MyInt Convert(unknown x) {\n" +
            "        return (MyInt) x;\n" +
            "    }\n" +
            "}";

        var (diagnostics, _) = CompileAndCollect(source);

        // Neither the targeted unknown-source diagnostic nor the
        // generic no-cast-operator diagnostic should fire.
        Assert.Empty(WithId(diagnostics, NoCastOperatorFromUnknownDiagnosticId));
        Assert.Empty(WithId(diagnostics, GenericNoCastOperatorDiagnosticId));
        Assert.Empty(WithId(diagnostics, AmbiguousCastDiagnosticId));
    }

    /// <summary>
    /// An <c>explicit</c> operator <c>MyInt(unknown)</c> on <c>MyInt</c>
    /// also resolves the cast — Req 13.4 accepts both implicit and
    /// explicit operators.
    /// </summary>
    [Fact]
    public void CastFromUnknown_WithExplicitOperatorOnTarget_Resolves()
    {
        const string source =
            "class MyInt {\n" +
            "    public int value;\n" +
            "    public MyInt(int v) { this.value = v; }\n" +
            "    public static explicit operator MyInt(unknown u) {\n" +
            "        return new MyInt(0);\n" +
            "    }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public MyInt Convert(unknown x) {\n" +
            "        return (MyInt) x;\n" +
            "    }\n" +
            "}";

        var (diagnostics, _) = CompileAndCollect(source);

        Assert.Empty(WithId(diagnostics, NoCastOperatorFromUnknownDiagnosticId));
        Assert.Empty(WithId(diagnostics, GenericNoCastOperatorDiagnosticId));
        Assert.Empty(WithId(diagnostics, AmbiguousCastDiagnosticId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 26.3 bullet 2 — `(int) someUnknown` with no operator declared →
    // `MIST_TC_0067` (the targeted "no cast operator from unknown"
    // diagnostic, reallocated from the design-document-proposed 0055).
    //
    // Req 13.4: when no implicit or explicit operator `T(unknown)` is
    // declared on `T`, the cast must be rejected. Req 13.6 explains
    // why: Mist's compile-time cast-operator requirement eliminates
    // the runtime cast failures that C# permits when casting `object`
    // to an arbitrary type. The targeted MIST_TC_0067 explains the
    // `unknown`-specific case rather than the generic MIST_TC_0042.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A cast from <c>unknown</c> to <c>int</c> with no declared
    /// operator emits the targeted <c>MIST_TC_0067</c> diagnostic and
    /// <em>not</em> the generic <c>MIST_TC_0042</c>.
    /// </summary>
    [Fact]
    public void CastFromUnknown_ToIntWithoutOperator_EmitsTargetedDiagnostic()
    {
        const string source =
            "class Foo {\n" +
            "    public int Convert(unknown x) {\n" +
            "        return (int) x;\n" +
            "    }\n" +
            "}";

        var (diagnostics, _) = CompileAndCollect(source);

        var targeted = WithId(diagnostics, NoCastOperatorFromUnknownDiagnosticId);
        Assert.Single(targeted);

        var d = targeted[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        // Message names the target type and mentions `unknown`, and
        // suggests using an `is` pattern (Req 16.4).
        Assert.Contains("unknown", d.Message);
        Assert.Contains("int", d.Message);
        Assert.Contains("is", d.Message);

        // The generic no-cast-operator diagnostic must NOT fire for
        // the unknown-source case — MIST_TC_0067 owns it.
        Assert.Empty(WithId(diagnostics, GenericNoCastOperatorDiagnosticId));
    }

    /// <summary>
    /// A user-defined class <c>Bar</c> with no <c>(unknown)</c> cast
    /// operator also emits <c>MIST_TC_0067</c>. The rule is not
    /// specific to primitive targets.
    /// </summary>
    [Fact]
    public void CastFromUnknown_ToUserClassWithoutOperator_EmitsTargetedDiagnostic()
    {
        const string source =
            "class Bar {\n" +
            "    public int value;\n" +
            "    public Bar(int v) { this.value = v; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public Bar Convert(unknown x) {\n" +
            "        return (Bar) x;\n" +
            "    }\n" +
            "}";

        var (diagnostics, _) = CompileAndCollect(source);

        var targeted = WithId(diagnostics, NoCastOperatorFromUnknownDiagnosticId);
        Assert.Single(targeted);
        Assert.Contains("Bar", targeted[0].Message);
        Assert.Contains("unknown", targeted[0].Message);

        Assert.Empty(WithId(diagnostics, GenericNoCastOperatorDiagnosticId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 26.3 bullet 3 — `(unknown) 42` is accepted without searching for
    // a cast operator.
    //
    // Req 13.5: a cast `(unknown) expr` is always valid for any
    // non-`void` `expr` because `unknown` is the user-facing top type
    // (Req 4.1). No operator lookup is performed. The top-type rule
    // lives in `IsDirectlyAssignable` (Task 26.2) so that
    // `CheckCastExpression` short-circuits before any operator search.
    //
    // Emission of the `(object)` C# cast is verified by Task 30, not
    // here — this test only checks the type-checker path.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A cast from a concrete primitive to <c>unknown</c> is
    /// accepted with no diagnostic emitted.
    /// </summary>
    [Fact]
    public void CastToUnknown_FromIntLiteral_IsAccepted()
    {
        const string source =
            "class Foo {\n" +
            "    public unknown Widen() {\n" +
            "        return (unknown) 42;\n" +
            "    }\n" +
            "}";

        var (diagnostics, _) = CompileAndCollect(source);

        // No cast-related diagnostic should fire. `(unknown) expr` is
        // trivially valid per Req 13.5.
        Assert.Empty(WithId(diagnostics, NoCastOperatorFromUnknownDiagnosticId));
        Assert.Empty(WithId(diagnostics, GenericNoCastOperatorDiagnosticId));
        Assert.Empty(WithId(diagnostics, AmbiguousCastDiagnosticId));
    }

    /// <summary>
    /// The top-type cast also applies to user-defined types — a cast
    /// from a user-defined class value to <c>unknown</c> is accepted.
    /// </summary>
    [Fact]
    public void CastToUnknown_FromUserClassInstance_IsAccepted()
    {
        const string source =
            "class Bar {\n" +
            "    public int value;\n" +
            "    public Bar(int v) { this.value = v; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public unknown Widen(Bar b) {\n" +
            "        return (unknown) b;\n" +
            "    }\n" +
            "}";

        var (diagnostics, _) = CompileAndCollect(source);

        Assert.Empty(WithId(diagnostics, NoCastOperatorFromUnknownDiagnosticId));
        Assert.Empty(WithId(diagnostics, GenericNoCastOperatorDiagnosticId));
        Assert.Empty(WithId(diagnostics, AmbiguousCastDiagnosticId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Counter-examples — the targeted MIST_TC_0067 must not fire in
    // cases that are owned by other diagnostics.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A cast between two concrete types (neither is <c>unknown</c>)
    /// with no operator still routes through the generic
    /// <c>MIST_TC_0042</c> path. The targeted <c>MIST_TC_0067</c>
    /// diagnostic fires only when the source is <c>unknown</c>.
    /// </summary>
    [Fact]
    public void CastBetweenConcreteTypes_WithoutOperator_DoesNotEmitUnknownDiagnostic()
    {
        const string source =
            "class A {\n" +
            "    public int value;\n" +
            "    public A(int v) { this.value = v; }\n" +
            "}\n" +
            "class B {\n" +
            "    public int value;\n" +
            "    public B(int v) { this.value = v; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public B Convert(A a) {\n" +
            "        return (B) a;\n" +
            "    }\n" +
            "}";

        var (diagnostics, _) = CompileAndCollect(source);

        // The targeted unknown-source diagnostic must NOT fire.
        Assert.Empty(WithId(diagnostics, NoCastOperatorFromUnknownDiagnosticId));
        // The generic MIST_TC_0042 owns the concrete→concrete case.
        Assert.NotEmpty(WithId(diagnostics, GenericNoCastOperatorDiagnosticId));
    }
}

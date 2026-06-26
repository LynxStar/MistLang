// Feature: unknown-type (Phase D, Task 24.4)
// **Validates: Requirements 11.1, 11.2, 11.3, 11.4**
//
// Unit tests for Task 24: the Type Checker rejects pattern narrowing
// when the pattern type is `unknown` (Req 11.3) or a
// `Closed_Generic_With_Unknown` (Req 11.4), and preserves the
// existing narrowing behavior for concrete constructed pattern types
// (Req 11.1, 11.2).
//
// Diagnostics under test:
//   * `MIST_TC_0063` — narrow-to-`unknown`. Reallocated from the
//     design-document-proposed `MIST_TC_0051`, which was already in
//     use by the pattern-matching feature's
//     `PatternConstantTypeMismatch`.
//   * `MIST_TC_0064` — narrow-to-Closed_Generic_With_Unknown.
//     Reallocated from the design-document-proposed `MIST_TC_0052`,
//     already in use by `PatternRelationalNonComparable`.
// See the `PatternNarrowToUnknown` / `PatternNarrowToClosedGenericWithUnknown`
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
/// Unit tests for the pattern-narrowing rejection rules introduced by
/// Task 24. Each test compiles a small Mist program that exercises an
/// <c>is</c> type pattern, runs the type checker, and asserts the
/// presence or absence of the appropriate diagnostic code.
/// </summary>
public class UnknownPatternNarrowingTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    /// <summary>
    /// Diagnostic code emitted by the Type Checker when a type pattern
    /// narrows to <c>unknown</c> (Req 11.3). Reallocated from the
    /// design-document-proposed <c>MIST_TC_0051</c>.
    /// </summary>
    private const string NarrowToUnknownDiagnosticId = "MIST_TC_0063";

    /// <summary>
    /// Diagnostic code emitted by the Type Checker when a type pattern
    /// narrows to a <c>Closed_Generic_With_Unknown</c> (Req 11.4).
    /// Reallocated from the design-document-proposed <c>MIST_TC_0052</c>.
    /// </summary>
    private const string NarrowToClosedGenericWithUnknownDiagnosticId = "MIST_TC_0064";

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
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var tcBag = new DiagnosticBag();
        _analyzer.Analyze(model, tcBag);

        return semBag.ToImmutableArray().Concat(tcBag.ToImmutableArray()).ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 24.4 bullet 1 — `x is unknown u` fires MIST_TC_0063.
    //
    // Req 11.3: narrowing to `unknown` is not meaningful because every
    // value is already assignable to `unknown` (Req 4.1), making the
    // pattern tautologically true. The binding introduced by the pattern
    // has no more specific type than the scrutinee — it discards
    // information rather than recovering it.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypePattern_NarrowToUnknown_IsRejected()
    {
        // An `unknown`-typed parameter pattern-matched against `unknown`
        // itself. The pattern `x is unknown u` is tautologically true
        // and introduces no new type information — it must be rejected.
        const string source =
            "class Foo {\n" +
            "    public void M(unknown x) {\n" +
            "        if (x is unknown u) { }\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, NarrowToUnknownDiagnosticId);

        Assert.Single(hits);
        var d = hits[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        // The message names `unknown` and states the rule.
        Assert.Contains("unknown", d.Message);
        Assert.Contains("tautologically true", d.Message);
    }

    /// <summary>
    /// Counter-example: narrowing an <c>unknown</c> to a concrete
    /// non-constructed type must <em>not</em> trigger the narrow-to-unknown
    /// rejection. The diagnostic is specific to the pattern-type being
    /// <c>unknown</c>, not to the scrutinee being <c>unknown</c>.
    /// </summary>
    [Fact]
    public void TypePattern_NarrowUnknownScrutineeToConcrete_IsAccepted()
    {
        // Scrutinee is `unknown`, pattern is `int` — the designated
        // narrowing mechanism (Req 11.1) must be accepted.
        const string source =
            "class Foo {\n" +
            "    public void M(unknown x) {\n" +
            "        if (x is int i) { }\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, NarrowToUnknownDiagnosticId));
        Assert.Empty(WithId(diagnostics, NarrowToClosedGenericWithUnknownDiagnosticId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 24.4 bullet 2 — `x is List<unknown> lu` fires MIST_TC_0064.
    //
    // Req 11.4: narrowing to a Closed_Generic_With_Unknown is not
    // meaningful because Covariant_Unknown_Widening (Req 8) makes the
    // pattern tautologically true, and the binding loses information —
    // members classified `WritesT` with respect to the `unknown`-bound
    // type parameter are filtered per Requirement 10 without any
    // information gain over the scrutinee.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypePattern_NarrowToClosedGenericWithUnknown_IsRejected()
    {
        // `x is List<unknown> lu` on a `List<int>` scrutinee. The
        // pattern is tautologically true under Covariant_Unknown_Widening
        // and the binding is strictly less useful than the original.
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M(List<int> xs) {\n" +
            "        if (xs is List<unknown> lu) { }\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, NarrowToClosedGenericWithUnknownDiagnosticId);

        Assert.Single(hits);
        var d = hits[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        // The message names the closed-generic-with-unknown form.
        Assert.Contains("List<unknown>", d.Message);
        Assert.Contains("tautologically true", d.Message);
    }

    /// <summary>
    /// Variant: the same rejection must fire even when the scrutinee is
    /// itself <c>List&lt;unknown&gt;</c>. The rule is defined by the
    /// <em>pattern</em> type, not the scrutinee type.
    /// </summary>
    [Fact]
    public void TypePattern_NarrowToClosedGenericWithUnknown_ScrutineeAlsoClosed_IsRejected()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M(List<unknown> xs) {\n" +
            "        if (xs is List<unknown> lu) { }\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.Single(WithId(diagnostics, NarrowToClosedGenericWithUnknownDiagnosticId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 24.4 bullet 3 — `x is List<int> li` is accepted and `li.Add(...)`
    // works inside the pattern branch.
    //
    // Req 11.1, 11.2: concrete constructed pattern types introduce a
    // binding of that type with full member access and no filtering.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypePattern_NarrowToConcreteConstructedType_IsAcceptedAndAllowsWrite()
    {
        // `x is List<int> li` on a scrutinee of type `List<unknown>`.
        // The binding `li : List<int>` permits full member access,
        // including the `Add(T)` method that would be filtered out
        // on the original `List<unknown>` receiver.
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M(List<unknown> xs) {\n" +
            "        if (xs is List<int> li) { li.Add(0); }\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);

        // No narrow-to-Closed_Generic_With_Unknown rejection: the
        // pattern type `List<int>` has no `unknown` type argument.
        Assert.Empty(WithId(diagnostics, NarrowToClosedGenericWithUnknownDiagnosticId));
        Assert.Empty(WithId(diagnostics, NarrowToUnknownDiagnosticId));

        // No member-filter diagnostic on the narrowed binding: `li.Add(0)`
        // is accepted because `li` has the concrete type `List<int>`,
        // not a Closed_Generic_With_Unknown (Req 11.2).
        Assert.Empty(WithId(diagnostics, "MIST_TC_0062"));
    }

    /// <summary>
    /// Variant: <c>Optional&lt;int&gt;</c> narrowing on an <c>unknown</c>
    /// scrutinee — accepted because <c>Optional&lt;int&gt;</c> has no
    /// <c>unknown</c> type argument. This confirms the rule is about
    /// the <em>pattern</em> type, and struct-generic definitions are
    /// not special-cased in the pattern path.
    /// </summary>
    [Fact]
    public void TypePattern_NarrowUnknownScrutineeToConcreteConstructed_IsAccepted()
    {
        const string source =
            "public struct Optional<T> {\n" +
            "    public T Value { get; }\n" +
            "    public Optional(T value) { this.Value = value; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M(unknown x) {\n" +
            "        if (x is Optional<int> oi) { }\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, NarrowToUnknownDiagnosticId));
        Assert.Empty(WithId(diagnostics, NarrowToClosedGenericWithUnknownDiagnosticId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Interaction check — the Task 24 rejection must not fire when the
    // pattern's resolved type is `ErrorType` (the Task 1 MIST_TC_0050
    // diagnostic for undefined types takes precedence). This confirms
    // the ordering of the checks in `CheckPrimaryPattern`.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypePattern_UndefinedType_DoesNotTriggerUnknownPatternRejection()
    {
        const string source =
            "class Foo {\n" +
            "    public void M(unknown x) {\n" +
            "        if (x is NoSuchType t) { }\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);

        // The undefined-type diagnostic fires (cascade suppression for
        // the Task 24 rules).
        Assert.NotEmpty(WithId(diagnostics, "MIST_TC_0050"));
        Assert.Empty(WithId(diagnostics, NarrowToUnknownDiagnosticId));
        Assert.Empty(WithId(diagnostics, NarrowToClosedGenericWithUnknownDiagnosticId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Nested closed-generic-with-unknown — `Optional<List<unknown>>`
    // must also be rejected, since its type-argument tree transitively
    // contains `unknown` at a position.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypePattern_NarrowToNestedClosedGenericWithUnknown_IsRejected()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "public struct Optional<T> {\n" +
            "    public T Value { get; }\n" +
            "    public Optional(T value) { this.Value = value; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M(unknown x) {\n" +
            "        if (x is Optional<List<unknown>> o) { }\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.Single(WithId(diagnostics, NarrowToClosedGenericWithUnknownDiagnosticId));
    }
}
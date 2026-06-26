// Feature: unknown-type (Phase B, Task 12) — Enforce storage ban at `new`,
// `new[]`, and `default` expressions.
//
// Validates Requirements 5.4, 5.5, 5.6, 5.7, 7.5, 16.2 — every `new`,
// `new[]`, and `default` expression whose resolved type satisfies
// `IsStorageBanned` emits the appropriate storage-ban diagnostic:
//
//   MIST_SB_0053 — `new T(...)` where T is a constructed generic with
//                  `unknown` at one or more type-argument positions
//                  (Requirements 5.7, 7.5, 16.2).
//   MIST_SB_0055 — `new T[n]` where T contains `unknown` as the element
//                  type (Requirement 5.5).
//   MIST_SB_0056 — `new unknown(...)` directly — a defensive check;
//                  the grammar rejects this form at parse time because
//                  `unknown` is a reserved keyword and
//                  `ObjectCreationExpression` requires a
//                  `QualifiedIdentifier` (Requirement 5.4).
//   MIST_SB_0057 — `default(unknown)` or bare `default` whose inferred
//                  context type is `UnknownType` (Requirement 5.6).
//
// Every test source declares its own minimal generic classes
// (`List<T>`, `Map<K, V>`) rather than relying on the stdlib, matching
// the convention in `StorageBanDeclarationTests` — the stdlib does not
// auto-load those collections, and self-contained sources keep test
// failures easy to diagnose.
//
// All tests run fully in-process via a shared `SemanticModelBuilder`
// and `TypeCheckerAnalyzer` — no `dotnet` process is spawned (in keeping
// with the workspace test-process-spawning policy).

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for the storage-ban checks added in Task 12 that cover
/// <c>new</c>, <c>new[]</c>, and <c>default</c> expressions.
/// </summary>
/// <remarks>
/// <para>
/// The Type Checker emits these diagnostics — unlike Task 11's tests
/// (which only need the <see cref="SemanticModelBuilder"/>), these tests
/// run the full pipeline because the relevant <c>CheckObjectCreation</c>,
/// <c>CheckArrayCreation</c>, and <c>ValidateDefaultableType</c> guards
/// live in <see cref="TypeCheckerAnalyzer"/>.
/// </para>
/// <para>
/// Negative cases exercise a concrete sibling of each banned construct
/// (e.g., <c>new List&lt;int&gt;()</c>) so we catch any over-triggering
/// of the storage-ban cascade on legitimate generic construction.
/// </para>
/// </remarks>
public class StorageBanNewDefaultTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

    /// <summary>
    /// Lexes, parses, builds the semantic model, then runs the type
    /// checker against <paramref name="source"/>. Returns the combined
    /// diagnostic set from the semantic model builder and the type
    /// checker so tests can filter by <c>MIST_SB_*</c> id regardless of
    /// which component emitted the diagnostic.
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
    // MIST_SB_0053 — `new List<unknown>()` / `new Map<unknown, int>()`
    // (Requirements 5.7, 7.5, 16.2).
    //
    // Generic construction with `unknown` at any type-argument position
    // (directly or transitively) is forbidden because the constructor
    // would need to persist an `unknown`-typed value in a T-typed slot.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_SB_0053_FiresOn_New_List_Of_Unknown()
    {
        // `new List<unknown>()` — the top-level type argument is `unknown`.
        const string source =
            "class List<T> { } " +
            "class Foo { public void M() { var xs = new List<unknown>(); } }";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0053");

        _ = Assert.Single(hits);
        var d = hits[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        // Diagnostic must name the generic type (Req 16.2).
        Assert.Contains("List", d.Message);
        Assert.Contains("unknown", d.Message);
    }

    [Fact]
    public void MIST_SB_0053_FiresOn_New_Map_Of_Unknown_Int()
    {
        // `new Map<unknown, int>()` — the key position is `unknown`;
        // the value position is concrete. Req 16.2 says the diagnostic
        // names the generic type; the exact position phrasing is not
        // pinned by the tests.
        const string source =
            "class Map<K, V> { } " +
            "class Foo { public void M() { var m = new Map<unknown, int>(); } }";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0053");

        _ = Assert.Single(hits);
        Assert.Contains("Map", hits[0].Message);
    }

    [Fact]
    public void MIST_SB_0053_FiresOn_New_List_Of_Optional_Of_Unknown_RecursiveCascade()
    {
        // `new List<Optional<unknown>>()` — `unknown` lives two levels
        // deep. The storage-ban cascade must still fire (Req 7.5, 7.6).
        const string source =
            "class List<T> { } " +
            "class Optional<T> { } " +
            "class Foo { public void M() { var o = new List<Optional<unknown>>(); } }";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0053");

        _ = Assert.Single(hits);
    }

    [Fact]
    public void MIST_SB_0053_DoesNotFireOn_New_List_Of_Int()
    {
        // Negative counter-example: `new List<int>()` contains no
        // `unknown`; the ban must not over-trigger.
        const string source =
            "class List<T> { } " +
            "class Foo { public void M() { var xs = new List<int>(); } }";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0053"));
    }

    [Fact]
    public void MIST_SB_0053_DoesNotFireOn_New_NonGeneric()
    {
        // Negative counter-example: plain `new Foo()` on a concrete
        // class must not trip the cascade.
        const string source =
            "class Bar { } " +
            "class Foo { public void M() { var b = new Bar(); } }";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0053"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_SB_0055 — `new unknown[n]` (Requirement 5.5).
    //
    // Array elements are writable storage; `new unknown[n]` would
    // persist every slot as an `unknown`-typed value.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_SB_0055_FiresOn_New_BareUnknown_Array()
    {
        // `new unknown[5]` — bare `unknown` as array element type.
        const string source =
            "class Foo { public void M() { var xs = new unknown[5]; } }";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0055");

        _ = Assert.Single(hits);
        var d = hits[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("unknown", d.Message);
    }

    [Fact]
    public void MIST_SB_0055_DoesNotFireOn_New_Int_Array()
    {
        // Negative counter-example: `new int[5]` contains no `unknown`.
        const string source =
            "class Foo { public void M() { var xs = new int[5]; } }";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0055"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_SB_0056 — `new unknown(...)` (Requirement 5.4).
    //
    // Defensive check. The grammar (<c>ObjectCreationExpression = NEW
    // QualifiedIdentifier ...</c>) rejects `new unknown(...)` at parse
    // time because `unknown` is a reserved keyword that lexes to
    // <c>UNKNOWN</c>, not <c>IDENTIFIER</c>. There is therefore no
    // positive-source test — attempting to compile `new unknown()`
    // yields a <c>MIST_PARSE_ERROR</c> and the type checker never runs.
    // We lock in that no <c>MIST_SB_0056</c> is erroneously emitted on
    // concrete types so the defensive guard cannot over-trigger.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_SB_0056_DoesNotFireOn_ConcreteConstruction()
    {
        // Negative counter-example: `new Foo()` on a concrete class
        // must not trip the instantiate-unknown guard.
        const string source =
            "class Bar { } " +
            "class Foo { public void M() { var b = new Bar(); } }";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0056"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_SB_0057 — `default(unknown)` (Requirement 5.6).
    //
    // Fires for both explicit `default(unknown)` and a bare `default`
    // whose inferred context type is `UnknownType`.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_SB_0057_FiresOn_Explicit_Default_Of_Unknown()
    {
        // `default(unknown)` — the type reference resolves to
        // `UnknownType.Instance` via `ResolveTypeReference`, and
        // `ValidateDefaultableType` emits the diagnostic before the
        // generic abstract/type-parameter rejections.
        const string source =
            "class Foo { public void M() { var x = default(unknown); } }";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0057");

        _ = Assert.Single(hits);
        var d = hits[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("unknown", d.Message);
    }

    [Fact]
    public void MIST_SB_0057_FiresOn_Bare_Default_Inferred_To_Unknown()
    {
        // `unknown x = default;` — bare `default` whose type is
        // inferred from the declared type of the initializer's target,
        // which is `UnknownType`. The type checker's
        // `TryInferDefaultLiteralType` path should produce the same
        // diagnostic because `ValidateDefaultableType` runs on whatever
        // type was inferred.
        const string source =
            "class Foo { public void M() { unknown x = default; } }";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0057");

        _ = Assert.Single(hits);
        Assert.Contains("unknown", hits[0].Message);
    }

    [Fact]
    public void MIST_SB_0057_DoesNotFireOn_Default_Of_Int()
    {
        // Negative counter-example: `default(int)` is a valid
        // defaultable primitive and must not trip the guard.
        const string source =
            "class Foo { public void M() { var x = default(int); } }";

        var diagnostics = CompileAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0057"));
    }
}
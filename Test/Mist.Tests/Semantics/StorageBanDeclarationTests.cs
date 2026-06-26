// Feature: unknown-type (Phase B, Task 11) — Enforce storage ban at declaration sites.
//
// Validates Requirements 5.1, 5.2, 5.3, 7.5, 7.6, 16.1 — every field, property,
// and indexer declaration whose declared type satisfies `IsStorageBanned` emits
// `MIST_SB_0050`, `MIST_SB_0051`, or `MIST_SB_0052` respectively. The cascade
// also fires for recursively-banned types like `List<unknown>`,
// `Map<string, unknown>`, and `List<Optional<unknown>>` (Req 7.5, 7.6).
//
// All tests run fully in-process via the shared `SemanticModelBuilder` — no
// `dotnet` process is spawned per iteration (in keeping with the workspace
// test-process-spawning policy). The lexer/parser are reused across tests.

using System.Collections.Immutable;
using System.Linq;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for the storage-ban declaration checks added in Task 11:
///
/// <list type="bullet">
/// <item><c>MIST_SB_0050</c> — <c>unknown</c> used as a field type (Req 5.1).</item>
/// <item><c>MIST_SB_0051</c> — <c>unknown</c> used as a property type (Req 5.2).</item>
/// <item><c>MIST_SB_0052</c> — <c>unknown</c> used as an indexer element type (Req 5.3).</item>
/// </list>
///
/// <para>
/// The cascade cases (<c>List&lt;unknown&gt;</c>, <c>Map&lt;string, unknown&gt;</c>,
/// <c>List&lt;Optional&lt;unknown&gt;&gt;</c>) all map to <c>MIST_SB_0050</c> on
/// the field declaration because the storage-ban predicate is applied to the
/// fully-substituted declared type — a container-of-unknown is a storage-banned
/// field type for the same reason as bare <c>unknown</c> (Requirements 7.5, 7.6).
/// </para>
/// <para>
/// Every test source declares its own minimal generic classes (<c>List&lt;T&gt;</c>,
/// <c>Map&lt;K, V&gt;</c>, <c>Optional&lt;T&gt;</c>) rather than relying on the
/// stdlib, for the same reason <see cref="UnknownTypeResolutionTests"/> does:
/// the stdlib does not auto-load those collections, and this keeps the tests
/// self-contained.
/// </para>
/// </summary>
public class StorageBanDeclarationTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder _builder = new();

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, runs the semantic model
    /// builder, and returns the diagnostics it produced. Modeled after
    /// <see cref="IntrinsicMarkerDiagnosticsTests"/>'s harness.
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

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_SB_0050 — `unknown` used as a field type (Requirement 5.1).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_SB_0050_FiresOn_BareUnknown_FieldDeclaration()
    {
        // `unknown f;` as a field declaration is forbidden per Requirement 5.1.
        const string source = "class Foo { unknown f; }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0050");

        Assert.Single(hits);
        var d = hits[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        // Diagnostic must name the declaring type and the field (Req 16.1).
        Assert.Contains("Foo", d.Message);
        Assert.Contains("f", d.Message);
        Assert.Contains("unknown", d.Message);
    }

    [Fact]
    public void MIST_SB_0050_FiresOn_List_Of_Unknown_Field_Cascade()
    {
        // Storage-ban cascade: `List<unknown>` as a field is equivalent to
        // storing `unknown` in the list's T-typed slot, which is forbidden
        // per Requirements 7.5 and 7.6.
        const string source =
            "class List<T> { } " +
            "class Foo { List<unknown> xs; }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0050");

        Assert.Single(hits);
        var d = hits[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Foo", d.Message);
        Assert.Contains("xs", d.Message);
    }

    [Fact]
    public void MIST_SB_0050_FiresOn_Map_String_Unknown_Field_Cascade()
    {
        // `Map<string, unknown>` as a field — the value slot is transitively
        // `unknown`, so the storage ban applies (Req 7.5).
        const string source =
            "class Map<K, V> { } " +
            "class Foo { Map<string, unknown> m; }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0050");

        Assert.Single(hits);
        Assert.Contains("Foo", hits[0].Message);
        Assert.Contains("m", hits[0].Message);
    }

    [Fact]
    public void MIST_SB_0050_FiresOn_List_Of_Optional_Of_Unknown_Field_RecursiveCascade()
    {
        // Requirement 7.6 — recursive application of the storage rule.
        // `List<Optional<unknown>>` is storage-banned because the inner
        // `Optional<unknown>` has `unknown` at a type-argument position.
        const string source =
            "class List<T> { } " +
            "class Optional<T> { } " +
            "class Foo { List<Optional<unknown>> nested; }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0050");

        Assert.Single(hits);
        Assert.Contains("Foo", hits[0].Message);
        Assert.Contains("nested", hits[0].Message);
    }

    [Fact]
    public void MIST_SB_0050_DoesNotFireOn_ConcreteField()
    {
        // Negative counter-example: a concrete-typed field must NOT trip
        // the storage ban.
        const string source = "class Foo { int x; }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0050"));
    }

    [Fact]
    public void MIST_SB_0050_DoesNotFireOn_ListOfInt_Field()
    {
        // Negative counter-example: `List<int>` as a field must NOT trip
        // the cascade — the ban is driven by `unknown` appearing in the
        // type tree, not by genericity itself.
        const string source =
            "class List<T> { } " +
            "class Foo { List<int> xs; }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0050"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_SB_0051 — `unknown` used as a property type (Requirement 5.2).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_SB_0051_FiresOn_BareUnknown_AutoProperty()
    {
        // `public unknown P { get; set; }` — auto-property with `unknown`
        // declared type is forbidden per Requirement 5.2.
        const string source = "class Foo { public unknown P { get; set; } }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0051");

        Assert.Single(hits);
        var d = hits[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Foo", d.Message);
        Assert.Contains("P", d.Message);
        Assert.Contains("unknown", d.Message);
    }

    [Fact]
    public void MIST_SB_0051_FiresOn_List_Of_Unknown_Property_Cascade()
    {
        // Storage-ban cascade for properties.
        const string source =
            "class List<T> { } " +
            "class Foo { public List<unknown> Xs { get; set; } }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0051");

        Assert.Single(hits);
        Assert.Contains("Foo", hits[0].Message);
        Assert.Contains("Xs", hits[0].Message);
    }

    [Fact]
    public void MIST_SB_0051_DoesNotFireOn_ConcreteProperty()
    {
        const string source = "class Foo { public int P { get; set; } }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0051"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_SB_0052 — `unknown` used as an indexer element type
    // (Requirement 5.3).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_SB_0052_FiresOn_BareUnknown_IndexerElementType()
    {
        // `public unknown this[int i] { get; set; }` — indexer element type
        // of `unknown` is forbidden per Requirement 5.3.
        const string source =
            "class Foo { public unknown this[int i] { get; set; } }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0052");

        Assert.Single(hits);
        var d = hits[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Foo", d.Message);
        Assert.Contains("this", d.Message);
        Assert.Contains("unknown", d.Message);
    }

    [Fact]
    public void MIST_SB_0052_FiresOn_List_Of_Unknown_IndexerElementType_Cascade()
    {
        // Storage-ban cascade on indexer element type.
        const string source =
            "class List<T> { } " +
            "class Foo { public List<unknown> this[int i] { get; set; } }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, "MIST_SB_0052");

        Assert.Single(hits);
        Assert.Contains("Foo", hits[0].Message);
    }

    [Fact]
    public void MIST_SB_0052_DoesNotFireOn_ConcreteIndexer()
    {
        const string source =
            "class Foo { public int this[int i] { get; set; } }";

        var diagnostics = BuildAndCollectDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_SB_0052"));
    }
}
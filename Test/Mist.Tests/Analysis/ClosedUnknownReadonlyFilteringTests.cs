// Feature: unknown-readonly-semantics (Task 10)
// **Validates: Requirements 2, 3, 4, 8, 11**
//
// Comprehensive test class validating the readonly-based approach for filtering
// member access on Closed_Generic_With_Unknown types. Covers method calls,
// property access, field access, self-calls inside declaring bodies, nested
// unknowns, variable reassignment, and open vs closed type parameters.
//
// These tests run fully in-process via the shared SemanticModelBuilder +
// TypeCheckerAnalyzer + ReadonlyAnalyzer — no `dotnet` process is spawned,
// per the workspace test-process-spawning policy.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Validates the new readonly-based approach for filtering member access on
/// Closed_Generic_With_Unknown types. Each test exercises a specific scenario
/// from the design document's fifth analysis pass.
/// </summary>
public class ClosedUnknownReadonlyFilteringTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer typeChecker = new();
    private readonly ReadonlyAnalyzer readonlyAnalyzer = new();

    private const string ClosedUnknownDiagnosticId = "MIST_TC_0062";

    /// <summary>
    /// Lexes, parses, builds the semantic model, runs readonly inference,
    /// then runs both the type checker and readonly analyzer. Returns the
    /// combined diagnostic set.
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

        // Run readonly inference (classifies members as InferredReadonly/Mutable)
        ReadonlyInferenceEngine.RunInference(model);

        var tcBag = new DiagnosticBag();
        typeChecker.Analyze(model, tcBag);

        var roBag = new DiagnosticBag();
        readonlyAnalyzer.Analyze(model, roBag);

        return semBag.ToImmutableArray()
            .Concat(tcBag.ToImmutableArray())
            .Concat(roBag.ToImmutableArray())
            .ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 1: Method call on List<unknown> where method is Mutable → MIST_TC_0062
    //
    // Requirement 2.1: Non-readonly method on Closed_Generic_With_Unknown
    // is blocked.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MutableMethodCall_OnClosedUnknown_EmitsMIST_TC_0062()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        xs.Add(1);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        _ = Assert.Single(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: Method call on List<unknown> where method is InferredReadonly
    // → no diagnostic
    //
    // Requirement 2.1: Readonly method on Closed_Generic_With_Unknown is
    // permitted.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InferredReadonlyMethodCall_OnClosedUnknown_NoDiagnostic()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public int count;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "    public int Count() { return this.count; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        xs.Count();\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        Assert.Empty(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 3: Method call on List<int> (concrete) → no diagnostic
    //
    // Requirement 2.2: Only Closed_Generic_With_Unknown triggers the
    // readonly type context. A fully-concrete type is not affected.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MutableMethodCall_OnConcreteType_NoDiagnostic()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M() {\n" +
            "        List<int> xs = new List<int>();\n" +
            "        xs.Add(1);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        Assert.Empty(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 4: Property read on List<unknown> where getter is InferredReadonly
    // → no diagnostic
    //
    // Requirement 4.2, 5.4: Readonly property getter on
    // Closed_Generic_With_Unknown is permitted.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadonlyPropertyGetter_OnClosedUnknown_NoDiagnostic()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public int count;\n" +
            "    public int Count { get { return this.count; } }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        int c = xs.Count;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        Assert.Empty(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 5: Property setter on List<unknown> → MIST_TC_0062
    //
    // Requirement 4.3: Property setter on Closed_Generic_With_Unknown is
    // always blocked (setters are never callable in readonly context).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PropertySetter_OnClosedUnknown_EmitsMIST_TC_0062()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public int count;\n" +
            "    public int Count { get { return this.count; } set { this.count = value; } }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        xs.Count = 5;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        _ = Assert.Single(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 6: Field read on List<unknown> → no diagnostic
    //
    // Requirement 2.1 (design table): Field reads are always permitted on
    // Closed_Generic_With_Unknown.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FieldRead_OnClosedUnknown_NoDiagnostic()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public int count;\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        int c = xs.count;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        Assert.Empty(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 7: Field write on List<unknown> → MIST_TC_0062
    //
    // Requirement 2.1 (design table): Field writes on
    // Closed_Generic_With_Unknown are blocked.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FieldWrite_OnClosedUnknown_EmitsMIST_TC_0062()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public int count;\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        xs.count = 5;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        _ = Assert.Single(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 8: Self-call inside declaring body (this.Add(x) inside List<T>)
    // → no diagnostic (open generic)
    //
    // Requirement 8.1: Inside the body of a generic type Foo<T>, `this` has
    // the open type Foo<T> where T is an unsubstituted type parameter —
    // therefore it is NOT a Closed_Generic_With_Unknown.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SelfCall_InsideDeclaringBody_OpenGeneric_NoDiagnostic()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "    public void AddTwice(T x) { this.Add(x); }\n" +
            "}\n";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        Assert.Empty(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 9: Nested unknown (Optional<List<unknown>>) → MIST_TC_0062 on
    // non-readonly access
    //
    // Requirement 2.3: Nested generics like Optional<List<unknown>> are
    // caught — the outermost variable has a type argument at depth that is
    // unknown, so the readonly type context applies.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NestedUnknown_NonReadonlyAccess_EmitsMIST_TC_0062()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Optional<T> {\n" +
            "    public T value;\n" +
            "    public void Set(T v) { this.value = v; }\n" +
            "    public T Get() { return this.value; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M() {\n" +
            "        Optional<List<unknown>> opt = new Optional<List<unknown>>();\n" +
            "        opt.Set(new List<int>());\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        _ = Assert.Single(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 10: Variable reassignment List<unknown> xs = ...; xs = ...;
    // → no diagnostic
    //
    // Requirement 3.1–3.3: The readonly type context applies only to member
    // access, not to the variable binding itself.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VariableReassignment_ClosedUnknown_NoDiagnostic()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        xs = new List<string>();\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        Assert.Empty(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 11: Map<T, unknown> inside generic body → no diagnostic (open
    // type parameter)
    //
    // Requirement 8.5: A type containing an open type parameter (Map<T, unknown>)
    // is NOT a Closed_Generic_With_Unknown because it has an unsubstituted
    // type parameter T.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OpenTypeParameter_MapTUnknown_InsideGenericBody_NoDiagnostic()
    {
        const string source =
            "class Map<K, V> {\n" +
            "    public K key;\n" +
            "    public V value;\n" +
            "    public void Put(K k, V v) { this.key = k; this.value = v; }\n" +
            "}\n" +
            "class Container<T> {\n" +
            "    public void M() {\n" +
            "        Map<T, unknown> m = new Map<T, unknown>();\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        Assert.Empty(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 12: Map<string, unknown> inside generic body → MIST_TC_0062 on
    // non-readonly access (independently closed)
    //
    // Requirement 8.4: A local variable declared with a type that is
    // independently a Closed_Generic_With_Unknown (Map<string, unknown>)
    // gets the readonly type context applied, even inside a generic body.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IndependentlyClosed_MapStringUnknown_InsideGenericBody_EmitsMIST_TC_0062()
    {
        const string source =
            "class Map<K, V> {\n" +
            "    public K key;\n" +
            "    public V value;\n" +
            "    public void Put(K k, V v) { this.key = k; this.value = v; }\n" +
            "}\n" +
            "class Container<T> {\n" +
            "    public void M() {\n" +
            "        Map<string, unknown> m = new Map<string, unknown>();\n" +
            "        m.Put(\"hello\", 42);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        _ = Assert.Single(hits);
    }
}
// Feature: unknown-readonly-semantics (Task 13)
// **Validates: Requirements 7.1–7.4**
//
// Unit tests verifying the interaction between covariant widening, return types,
// and the Closed_Generic_With_Unknown readonly type context. When a value is
// widened from a concrete type (e.g., List<int>) to List<unknown>, the readonly
// type context applies to the resulting variable. Similarly, when a method returns
// a Closed_Generic_With_Unknown, the caller is subject to the readonly type
// context on the result. Generic methods whose type parameter is inferred as
// `unknown` also produce a Closed_Generic_With_Unknown result type.
//
// These tests run fully in-process via the shared SemanticModelBuilder +
// TypeCheckerAnalyzer + ReadonlyAnalyzer — no `dotnet` process is spawned,
// per the workspace test-process-spawning policy.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Validates the interaction between covariant unknown widening, return types,
/// and the Closed_Generic_With_Unknown readonly type context (pass 5).
/// </summary>
public class ClosedUnknownCovariantWideningTests
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
    // Requirement 7.1: Covariant widening from List<int> to List<unknown>
    // then calling non-readonly method → MIST_TC_0062.
    //
    // When a value of type Foo<A> is assigned to a variable of type
    // Foo<unknown> via Covariant_Unknown_Widening, the readonly type context
    // applies to the target variable.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CovariantWidening_ListIntToListUnknown_NonReadonlyMethodCall_EmitsMIST_TC_0062()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "    public readonly int Count() { return 0; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        xs.Add(1);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        // Pass 5 fires MIST_TC_0062 for calling non-readonly Add on List<unknown>.
        _ = Assert.Single(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.3: Method returning List<unknown> — caller accessing
    // non-readonly member on result → MIST_TC_0062.
    //
    // When a method's declared return type is a Closed_Generic_With_Unknown,
    // the readonly type context applies to any variable that receives the
    // return value.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodReturningClosedUnknown_CallerAccessesNonReadonlyMember_EmitsMIST_TC_0062()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "    public readonly int Count() { return 0; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public List<unknown> GetList() {\n" +
            "        return new List<int>();\n" +
            "    }\n" +
            "    public void M() {\n" +
            "        List<unknown> result = this.GetList();\n" +
            "        result.Add(1);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        // Pass 5 fires MIST_TC_0062 for calling non-readonly Add on the
        // List<unknown> result.
        _ = Assert.Single(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.3: GetList().Count where GetList() returns List<unknown>
    // and Count is readonly → no diagnostic.
    //
    // When a method's declared return type is a Closed_Generic_With_Unknown,
    // the readonly type context applies to member access directly on the
    // return expression. Readonly members remain accessible.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodReturningClosedUnknown_CallerAccessesReadonlyMember_NoDiagnostic()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public int count;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "    public readonly int Count() { return this.count; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public List<unknown> GetList() {\n" +
            "        return new List<int>();\n" +
            "    }\n" +
            "    public void M() {\n" +
            "        int c = this.GetList().Count();\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        // No MIST_TC_0062 — Count() is readonly, so it's accessible on
        // the List<unknown> return value.
        Assert.Empty(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.4: Generic method inferred as unknown at call site →
    // MIST_TC_0062 on non-readonly access.
    //
    // When a generic method's type parameter is inferred as `unknown` at the
    // call site, the readonly type context applies to any variable or
    // expression whose resolved type is a Closed_Generic_With_Unknown as a
    // result of that inference.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenericMethodInferredAsUnknown_NonReadonlyAccess_EmitsMIST_TC_0062()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "    public readonly int Count() { return 0; }\n" +
            "}\n" +
            "class Factory {\n" +
            "    public List<T> Create<T>() {\n" +
            "        return new List<T>();\n" +
            "    }\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = this.Create<unknown>();\n" +
            "        xs.Add(1);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        // Pass 5 fires MIST_TC_0062 for calling non-readonly Add on the
        // List<unknown> result from the generic method.
        _ = Assert.Single(hits);
    }
}
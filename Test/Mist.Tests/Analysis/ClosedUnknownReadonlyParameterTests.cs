// Feature: unknown-readonly-semantics (Task 12)
// **Validates: Requirements 9.1–9.4**
//
// Unit tests verifying the interaction between readonly parameters and the
// Closed_Generic_With_Unknown readonly type context. Passing a value whose type
// is a Closed_Generic_With_Unknown to a method parameter (readonly or not) does
// not trigger MIST_TC_0062 at the call site. Inside the callee, the readonly
// type context still applies to the parameter's type. When a parameter is both
// explicitly `readonly` and its type is a Closed_Generic_With_Unknown, only one
// diagnostic fires (not both MIST_RO and MIST_TC_0062).
//
// These tests run fully in-process via the shared SemanticModelBuilder +
// TypeCheckerAnalyzer + ReadonlyAnalyzer — no `dotnet` process is spawned,
// per the workspace test-process-spawning policy.

using System.Collections.Immutable;
using System.Linq;
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
/// Verifies the interaction between readonly parameters (pass 3) and the
/// Closed_Generic_With_Unknown readonly type context (pass 5).
/// </summary>
public class ClosedUnknownReadonlyParameterTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer typeChecker = new();
    private readonly ReadonlyAnalyzer readonlyAnalyzer = new();

    private const string ClosedUnknownDiagnosticId = "MIST_TC_0062";
    private const string ReadonlyParamMethodCallId = "MIST_RO_0006";

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
    // Requirement 9.1: Passing List<unknown> to a readonly parameter → no
    // error at the call site.
    //
    // The readonly type context restricts what the callee can do with the
    // value, not whether the value can be passed.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PassingClosedUnknown_ToReadonlyParameter_NoErrorAtCallSite()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "    public readonly int Count() { return 0; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void Accept(readonly List<unknown> items) {\n" +
            "        items.Count();\n" +
            "    }\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        this.Accept(xs);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var tc0062Hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        // No MIST_TC_0062 at the call site (this.Accept(xs)).
        Assert.Empty(tc0062Hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 9.2: Passing List<unknown> to a non-readonly parameter →
    // no error at the call site.
    //
    // The readonly type context restricts what the callee can do with the
    // value through the Readonly_Analyzer, not whether the value can be passed.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PassingClosedUnknown_ToNonReadonlyParameter_NoErrorAtCallSite()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "    public readonly int Count() { return 0; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void Accept(List<unknown> items) {\n" +
            "        items.Count();\n" +
            "    }\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        this.Accept(xs);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var tc0062Hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        // No MIST_TC_0062 at the call site (this.Accept(xs)).
        Assert.Empty(tc0062Hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 9.3: Inside callee with List<unknown> parameter, calling
    // non-readonly method → MIST_TC_0062.
    //
    // The readonly type context applies to the parameter's type inside the
    // callee's body, regardless of whether the parameter is explicitly readonly.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsideCallee_ClosedUnknownParam_NonReadonlyMethodCall_EmitsMIST_TC_0062()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "    public readonly int Count() { return 0; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void Accept(List<unknown> items) {\n" +
            "        items.Add(1);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var tc0062Hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        // Pass 5 fires MIST_TC_0062 for calling non-readonly Add on List<unknown>.
        _ = Assert.Single(tc0062Hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 9.4: When a parameter is both `readonly` and its type is
    // a Closed_Generic_With_Unknown, only one diagnostic fires — not both
    // MIST_RO_0006 and MIST_TC_0062.
    //
    // Both pass 3 (readonly parameters) and pass 5 (closed-unknown) would
    // detect the violation. Per Req 9.4, the restrictions are equivalent in
    // practice, so no additional diagnostic is emitted beyond what either
    // mechanism alone would produce.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadonlyParam_ClosedUnknown_NonReadonlyMethodCall_NoDuplicateDiagnostics()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "    public readonly int Count() { return 0; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void Accept(readonly List<unknown> items) {\n" +
            "        items.Add(1);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var tc0062Hits = WithId(diagnostics, ClosedUnknownDiagnosticId);
        var ro0006Hits = WithId(diagnostics, ReadonlyParamMethodCallId);

        // The total number of diagnostics for this violation should be exactly 1.
        // Either MIST_TC_0062 (from pass 5) or MIST_RO_0006 (from pass 3) fires,
        // but not both. Per the design, MIST_TC_0062 takes precedence as the more
        // specific diagnostic for the unknown-type-context.
        var totalHits = tc0062Hits.Length + ro0006Hits.Length;
        Assert.Equal(1, totalHits);

        // Specifically, MIST_TC_0062 should be the one that fires.
        _ = Assert.Single(tc0062Hits);
        Assert.Empty(ro0006Hits);
    }
}
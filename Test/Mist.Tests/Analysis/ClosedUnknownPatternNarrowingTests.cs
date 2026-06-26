// Feature: unknown-readonly-semantics (Task 14)
// **Validates: Requirements 6.1–6.4**
//
// Unit tests for the interaction between pattern narrowing and the
// readonly type context applied to Closed_Generic_With_Unknown types.
//
// Key scenarios:
// 1. Narrowing List<unknown> to List<int> via pattern matching removes
//    the readonly type context — full member access is permitted on the
//    narrowed binding (Req 6.1, 6.4).
// 2. Narrowing `unknown` to a concrete non-generic type via pattern
//    matching — no readonly type context applies (Req 6.2).
// 3. Existing rejection tests for narrowing TO `unknown` or TO a
//    Closed_Generic_With_Unknown remain in effect (Req 6.3) — verified
//    by running the existing UnknownPatternNarrowingTests.
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
/// Validates that pattern narrowing from a Closed_Generic_With_Unknown or
/// from <c>unknown</c> to a concrete type removes the readonly type context,
/// allowing full member access on the narrowed binding.
/// </summary>
public class ClosedUnknownPatternNarrowingTests
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
    // Test 1: List<unknown> narrowed to List<int> via pattern matching —
    // the narrowed binding has type List<int> which is NOT a
    // Closed_Generic_With_Unknown, so full member access is permitted.
    //
    // Requirement 6.1: When a variable of type List<unknown> is narrowed
    // via pattern matching to a fully-concrete constructed type (e.g.,
    // List<int>), the Readonly_Analyzer SHALL NOT apply the readonly type
    // context to the narrowed binding.
    //
    // Requirement 6.4: The Readonly_Analyzer determines whether the
    // readonly type context applies by inspecting the narrowed binding's
    // resolved type (not the original variable's type).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PatternNarrow_ListUnknownToListInt_MutableMethodCall_NoDiagnostic()
    {
        // `xs` has type List<unknown> (Closed_Generic_With_Unknown).
        // After narrowing to List<int>, the binding `li` has type List<int>
        // which is fully concrete — no readonly type context applies.
        // Calling the mutable method `Add` on `li` must be permitted.
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        if (xs is List<int> li) { li.Add(1); }\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        // No MIST_TC_0062: the narrowed binding `li` has type List<int>,
        // which is not a Closed_Generic_With_Unknown.
        Assert.Empty(hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: `unknown` narrowed to a concrete non-generic type via pattern
    // matching — no readonly type context applies to the narrowed binding.
    //
    // Requirement 6.2: When a variable of type `unknown` is narrowed via
    // pattern matching to a type T that is not a Closed_Generic_With_Unknown,
    // the Readonly_Analyzer SHALL NOT apply the readonly type context to
    // the narrowed binding.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PatternNarrow_UnknownToConcreteType_MemberAccess_NoDiagnostic()
    {
        // `x` has type `unknown`. After narrowing to `MyString`, the
        // binding `s` has type `MyString` — a concrete non-generic type.
        // Accessing the `Length` method on `s` must be permitted without
        // any readonly type context restriction.
        const string source =
            "class MyString {\n" +
            "    public int len;\n" +
            "    public int Length() { return this.len; }\n" +
            "    public void SetLen(int v) { this.len = v; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M(unknown x) {\n" +
            "        if (x is MyString s) { s.SetLen(5); }\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        // No MIST_TC_0062: the narrowed binding `s` has type `MyString`,
        // which is not a Closed_Generic_With_Unknown (it's not even generic).
        Assert.Empty(hits);
    }
}
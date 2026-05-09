// Feature: unknown-readonly-semantics (Task 5)
// **Validates: Requirements 4.4, 4.6**
//
// Unit tests verifying that when a member access on a Closed_Generic_With_Unknown
// receiver occurs inside a readonly method body, only MIST_TC_0062 fires — not
// both MIST_TC_0062 and MIST_RO_0002. The guard in pass 1 skips the MIST_RO
// emission when the receiver is a Closed_Generic_With_Unknown, letting
// MIST_TC_0062 (from pass 5) take precedence as the more specific diagnostic.
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
/// Verifies diagnostic differentiation: MIST_TC_0062 takes precedence over
/// MIST_RO_0002 when the receiver is a Closed_Generic_With_Unknown.
/// </summary>
public class ClosedUnknownDiagnosticDifferentiationTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _typeChecker = new();
    private readonly ReadonlyAnalyzer _readonlyAnalyzer = new();

    private const string ClosedUnknownDiagnosticId = "MIST_TC_0062";
    private const string ReadonlyMethodCallDiagnosticId = "MIST_RO_0002";

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
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        // Run readonly inference (classifies members as InferredReadonly/Mutable)
        ReadonlyInferenceEngine.RunInference(model);

        var tcBag = new DiagnosticBag();
        _typeChecker.Analyze(model, tcBag);

        var roBag = new DiagnosticBag();
        _readonlyAnalyzer.Analyze(model, roBag);

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
    // Requirement 4.4, 4.6: Non-readonly method calling xs.Add(1) where
    // xs: List<unknown> → only MIST_TC_0062 (pass 1 doesn't fire because
    // the enclosing method is not readonly).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NonReadonlyMethod_ClosedUnknownReceiver_OnlyMIST_TC_0062()
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

        var tc0062Hits = WithId(diagnostics, ClosedUnknownDiagnosticId);
        var ro0002Hits = WithId(diagnostics, ReadonlyMethodCallDiagnosticId);

        // Pass 5 fires MIST_TC_0062 for the non-readonly Add call on List<unknown>.
        Assert.Single(tc0062Hits);

        // Pass 1 does NOT fire because the enclosing method M() is not readonly.
        Assert.Empty(ro0002Hits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 4.4, 4.6: Readonly method calling xs.Add(1) where
    // xs: List<unknown> → only MIST_TC_0062 (pass 1 would fire MIST_RO_0002
    // but the guard prevents it because the receiver is Closed_Generic_With_Unknown).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadonlyMethod_ClosedUnknownReceiver_OnlyMIST_TC_0062_NotBoth()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "    public readonly int Count() { return 0; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public readonly void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        xs.Add(1);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);

        var tc0062Hits = WithId(diagnostics, ClosedUnknownDiagnosticId);
        var ro0002Hits = WithId(diagnostics, ReadonlyMethodCallDiagnosticId);

        // Pass 5 fires MIST_TC_0062 for the non-readonly Add call on List<unknown>.
        Assert.Single(tc0062Hits);

        // Pass 1 would normally fire MIST_RO_0002 for calling non-readonly Add
        // inside a readonly method body, but the guard skips it because the
        // receiver is a Closed_Generic_With_Unknown — MIST_TC_0062 takes precedence.
        Assert.Empty(ro0002Hits);
    }
}

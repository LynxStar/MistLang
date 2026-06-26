// Feature: unknown-readonly-semantics (Task 4)
// **Validates: Requirements 3.1–3.5**
//
// Unit tests verifying that the fifth analysis pass (Closed_Generic_With_Unknown
// readonly enforcement) does NOT fire on bare variable reassignment or argument
// passing. The readonly type context applies only to member access through the
// variable, not to the variable binding itself.
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
/// Verifies that the fifth pass only fires on PostfixNode suffixes and
/// AssignmentNode targets that go through member access — bare variable
/// reassignment and argument passing do not trigger MIST_TC_0062.
/// </summary>
public class ClosedUnknownVariableReassignmentTests
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
    // Requirement 3.1–3.2: Variable reassignment of List<unknown> is permitted.
    //
    // A bare assignment `xs = new List<string>()` does not involve member
    // access on the receiver — it is handled by the TypeChecker's existing
    // assignability rules (covariant unknown widening), not by the fifth
    // pass's readonly enforcement.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void BareVariableReassignment_ClosedGenericWithUnknown_NoMIST_TC_0062()
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
    // Requirement 3.4: Passing a Closed_Generic_With_Unknown as an argument
    // to a method does not trigger MIST_TC_0062 at the call site.
    //
    // Passing a value is not member access on the receiver — it is an
    // argument expression that the TypeChecker validates for assignability.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PassingClosedGenericWithUnknown_AsArgument_NoMIST_TC_0062()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void Accept(List<unknown> items) { }\n" +
            "    public void M() {\n" +
            "        List<unknown> xs = new List<int>();\n" +
            "        this.Accept(xs);\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndCollectDiagnostics(source);
        var hits = WithId(diagnostics, ClosedUnknownDiagnosticId);

        Assert.Empty(hits);
    }
}
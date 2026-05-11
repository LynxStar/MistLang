// Feature: structured-context, Property 2: Context checker soundness and completeness
//
// **Validates: Requirements 1.9, 2.4, 3.4, 3.6, 3.7, 8.6, 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 13.2, 13.3**
//
// For any valid Mist program where every call site of a method with `with` clauses
// has a matching context declaration (of the exact resolved type) reachable in the
// call graph, the context checker SHALL emit zero context-availability diagnostics.
// Conversely, for any program where a call site lacks a reachable context declaration
// for a required type, the context checker SHALL emit exactly one diagnostic per
// unsatisfied type at that call site.
//
// All work runs in-process — no external process is spawned per iteration
// (in keeping with the workspace test-process-spawning policy).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

// ─── Scenario types ──────────────────────────────────────────────────────

/// <summary>
/// Describes the kind of context checker scenario being tested.
/// </summary>
public enum ContextCheckerScenarioKind
{
    /// <summary>
    /// Direct call to a method with a with-clause. Context is provided
    /// before the call site via a with-declaration statement.
    /// Expected: zero diagnostics.
    /// </summary>
    DirectCallSatisfied,

    /// <summary>
    /// Direct call to a method with a with-clause. No context is provided.
    /// Expected: one diagnostic per unsatisfied type.
    /// </summary>
    DirectCallUnsatisfied,

    /// <summary>
    /// Call to a method with a with-clause inside a with-block that provides
    /// the required context type.
    /// Expected: zero diagnostics.
    /// </summary>
    WithBlockSatisfied,

    /// <summary>
    /// Call to a method with a with-clause outside a with-block (block provides
    /// context but call is after the block exits).
    /// Expected: one diagnostic.
    /// </summary>
    WithBlockUnsatisfied,

    /// <summary>
    /// Caller method has a with-clause for the required type, satisfying the
    /// callee's requirement through propagation.
    /// Expected: zero diagnostics.
    /// </summary>
    CallerWithClauseSatisfied,

    /// <summary>
    /// Multiple with-clauses on a method, all satisfied by declarations.
    /// Expected: zero diagnostics.
    /// </summary>
    MultipleClausesSatisfied,

    /// <summary>
    /// Multiple with-clauses on a method, some unsatisfied.
    /// Expected: one diagnostic per unsatisfied type.
    /// </summary>
    MultipleClausesPartiallyUnsatisfied,

    /// <summary>
    /// Interface dispatch: calling through an interface type where the interface
    /// method declares a with-clause. Context is provided.
    /// Expected: zero diagnostics.
    /// </summary>
    InterfaceDispatchSatisfied,

    /// <summary>
    /// Interface dispatch: calling through an interface type where the interface
    /// method declares a with-clause. Context is NOT provided.
    /// Expected: one diagnostic.
    /// </summary>
    InterfaceDispatchUnsatisfied,

    /// <summary>
    /// Context declared after the call site in the same block.
    /// Expected: one diagnostic (context not yet available).
    /// </summary>
    ContextDeclaredAfterCallSite,
}

/// <summary>
/// A generated scenario for testing context checker soundness and completeness.
/// </summary>
public sealed class ContextCheckerScenario
{
    public ContextCheckerScenarioKind Kind { get; }
    public string Source { get; }

    /// <summary>
    /// The expected number of MIST_CTX_0001 diagnostics for this scenario.
    /// </summary>
    public int ExpectedDiagnosticCount { get; }

    public ContextCheckerScenario(
        ContextCheckerScenarioKind kind,
        string source,
        int expectedDiagnosticCount)
    {
        Kind = kind;
        Source = source;
        ExpectedDiagnosticCount = expectedDiagnosticCount;
    }

    public override string ToString() =>
        $"[{Kind}] expect {ExpectedDiagnosticCount} MIST_CTX_0001 diagnostic(s)";
}

// ─── Property test class ─────────────────────────────────────────────────

/// <summary>
/// Property-based tests for context checker soundness and completeness.
///
/// Property 2: Context checker soundness and completeness
///
/// **Validates: Requirements 1.9, 2.4, 3.4, 3.6, 3.7, 8.6, 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 13.2, 13.3**
/// </summary>
public sealed class ContextCheckerSoundnessPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ContextCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] ContextTypeNames =
        ["AppCtx", "DbCtx", "LogCtx", "AuthCtx", "CacheCtx"];

    private static readonly string[] MethodNames =
        ["Process", "Execute", "Handle", "Perform", "Run"];

    private static readonly string[] InterfaceNames =
        ["IProcessor", "IExecutor", "IHandler", "IPerformer", "IRunner"];

    private static readonly string[] ImplClassNames =
        ["ProcessorImpl", "ExecutorImpl", "HandlerImpl", "PerformerImpl", "RunnerImpl"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<ContextCheckerScenario> GenDirectCallSatisfied()
    {
        return from ctxType in Gen.Elements(ContextTypeNames)
               from methodName in Gen.Elements(MethodNames)
               from declKind in Gen.Choose(0, 3)
               select BuildDirectCallSatisfied(ctxType, methodName, declKind);
    }

    private static Gen<ContextCheckerScenario> GenDirectCallUnsatisfied()
    {
        return from ctxType in Gen.Elements(ContextTypeNames)
               from methodName in Gen.Elements(MethodNames)
               select BuildDirectCallUnsatisfied(ctxType, methodName);
    }

    private static Gen<ContextCheckerScenario> GenWithBlockSatisfied()
    {
        return from ctxType in Gen.Elements(ContextTypeNames)
               from methodName in Gen.Elements(MethodNames)
               select BuildWithBlockSatisfied(ctxType, methodName);
    }

    private static Gen<ContextCheckerScenario> GenWithBlockUnsatisfied()
    {
        return from ctxType in Gen.Elements(ContextTypeNames)
               from methodName in Gen.Elements(MethodNames)
               select BuildWithBlockUnsatisfied(ctxType, methodName);
    }

    private static Gen<ContextCheckerScenario> GenCallerWithClauseSatisfied()
    {
        return from ctxType in Gen.Elements(ContextTypeNames)
               from methodName in Gen.Elements(MethodNames)
               select BuildCallerWithClauseSatisfied(ctxType, methodName);
    }

    private static Gen<ContextCheckerScenario> GenMultipleClausesSatisfied()
    {
        return from idx1 in Gen.Choose(0, ContextTypeNames.Length - 1)
               from idx2 in Gen.Choose(0, ContextTypeNames.Length - 1)
               where idx1 != idx2
               from methodName in Gen.Elements(MethodNames)
               select BuildMultipleClausesSatisfied(
                   ContextTypeNames[idx1], ContextTypeNames[idx2], methodName);
    }

    private static Gen<ContextCheckerScenario> GenMultipleClausesPartiallyUnsatisfied()
    {
        return from idx1 in Gen.Choose(0, ContextTypeNames.Length - 1)
               from idx2 in Gen.Choose(0, ContextTypeNames.Length - 1)
               where idx1 != idx2
               from methodName in Gen.Elements(MethodNames)
               select BuildMultipleClausesPartiallyUnsatisfied(
                   ContextTypeNames[idx1], ContextTypeNames[idx2], methodName);
    }

    private static Gen<ContextCheckerScenario> GenInterfaceDispatchSatisfied()
    {
        return from idx in Gen.Choose(0, InterfaceNames.Length - 1)
               from ctxType in Gen.Elements(ContextTypeNames)
               select BuildInterfaceDispatchSatisfied(
                   InterfaceNames[idx], ImplClassNames[idx], ctxType);
    }

    private static Gen<ContextCheckerScenario> GenInterfaceDispatchUnsatisfied()
    {
        return from idx in Gen.Choose(0, InterfaceNames.Length - 1)
               from ctxType in Gen.Elements(ContextTypeNames)
               select BuildInterfaceDispatchUnsatisfied(
                   InterfaceNames[idx], ImplClassNames[idx], ctxType);
    }

    private static Gen<ContextCheckerScenario> GenContextDeclaredAfterCallSite()
    {
        return from ctxType in Gen.Elements(ContextTypeNames)
               from methodName in Gen.Elements(MethodNames)
               select BuildContextDeclaredAfterCallSite(ctxType, methodName);
    }

    public static Gen<ContextCheckerScenario> GenScenario()
    {
        return Gen.OneOf(
            GenDirectCallSatisfied(),
            GenDirectCallUnsatisfied(),
            GenWithBlockSatisfied(),
            GenWithBlockUnsatisfied(),
            GenCallerWithClauseSatisfied(),
            GenMultipleClausesSatisfied(),
            GenMultipleClausesPartiallyUnsatisfied(),
            GenInterfaceDispatchSatisfied(),
            GenInterfaceDispatchUnsatisfied(),
            GenContextDeclaredAfterCallSite());
    }

    public static Arbitrary<ContextCheckerScenario> ArbitraryContextCheckerScenario() =>
        GenScenario().ToArbitrary();

    // ─── Scenario builders ──────────────────────────────────────────────

    /// <summary>
    /// Builds a scenario where a method with a with-clause is called after a
    /// context declaration statement. The declaration kind varies:
    /// 0 = with var, 1 = with TypeRef, 2 = with TypeRef _, 3 = with expr
    /// </summary>
    private static ContextCheckerScenario BuildDirectCallSatisfied(
        string ctxType, string methodName, int declKind)
    {
        var declaration = declKind switch
        {
            0 => $"with var ctx = new {ctxType}();",
            1 => $"with {ctxType} ctx = new {ctxType}();",
            2 => $"with {ctxType} _ = new {ctxType}();",
            3 => $"with new {ctxType}();",
            _ => $"with var ctx = new {ctxType}();",
        };

        var source =
            $"class {ctxType} {{ }}\n" +
            "\n" +
            "class Program {\n" +
            $"    void {methodName}() with {ctxType} ctx {{ }}\n" +
            "\n" +
            "    public static void Main() {\n" +
            "        var p = new Program();\n" +
            $"        {declaration}\n" +
            $"        p.{methodName}();\n" +
            "    }\n" +
            "}\n";

        return new ContextCheckerScenario(
            ContextCheckerScenarioKind.DirectCallSatisfied,
            source,
            expectedDiagnosticCount: 0);
    }

    /// <summary>
    /// Builds a scenario where a method with a with-clause is called without
    /// any context declaration in scope.
    /// </summary>
    private static ContextCheckerScenario BuildDirectCallUnsatisfied(
        string ctxType, string methodName)
    {
        var source =
            $"class {ctxType} {{ }}\n" +
            "\n" +
            "class Program {\n" +
            $"    void {methodName}() with {ctxType} ctx {{ }}\n" +
            "\n" +
            "    public static void Main() {\n" +
            "        var p = new Program();\n" +
            $"        p.{methodName}();\n" +
            "    }\n" +
            "}\n";

        return new ContextCheckerScenario(
            ContextCheckerScenarioKind.DirectCallUnsatisfied,
            source,
            expectedDiagnosticCount: 1);
    }

    /// <summary>
    /// Builds a scenario where a method with a with-clause is called inside a
    /// with-block that provides the required context type.
    /// </summary>
    private static ContextCheckerScenario BuildWithBlockSatisfied(
        string ctxType, string methodName)
    {
        var source =
            $"class {ctxType} {{ }}\n" +
            "\n" +
            "class Program {\n" +
            $"    void {methodName}() with {ctxType} ctx {{ }}\n" +
            "\n" +
            "    public static void Main() {\n" +
            "        var p = new Program();\n" +
            $"        with(new {ctxType}()) {{\n" +
            $"            p.{methodName}();\n" +
            "        }\n" +
            "    }\n" +
            "}\n";

        return new ContextCheckerScenario(
            ContextCheckerScenarioKind.WithBlockSatisfied,
            source,
            expectedDiagnosticCount: 0);
    }

    /// <summary>
    /// Builds a scenario where a method with a with-clause is called AFTER a
    /// with-block exits (context is no longer available).
    /// </summary>
    private static ContextCheckerScenario BuildWithBlockUnsatisfied(
        string ctxType, string methodName)
    {
        var source =
            $"class {ctxType} {{ }}\n" +
            "\n" +
            "class Program {\n" +
            $"    void {methodName}() with {ctxType} ctx {{ }}\n" +
            "\n" +
            "    public static void Main() {\n" +
            "        var p = new Program();\n" +
            $"        with(new {ctxType}()) {{\n" +
            "        }\n" +
            $"        p.{methodName}();\n" +
            "    }\n" +
            "}\n";

        return new ContextCheckerScenario(
            ContextCheckerScenarioKind.WithBlockUnsatisfied,
            source,
            expectedDiagnosticCount: 1);
    }

    /// <summary>
    /// Builds a scenario where the calling method itself has a with-clause for
    /// the required type, satisfying the callee's requirement through propagation.
    /// The caller is invoked from Main with context provided.
    /// </summary>
    private static ContextCheckerScenario BuildCallerWithClauseSatisfied(
        string ctxType, string methodName)
    {
        var source =
            $"class {ctxType} {{ }}\n" +
            "\n" +
            "class Program {\n" +
            $"    void {methodName}() with {ctxType} ctx {{ }}\n" +
            "\n" +
            $"    void Caller() with {ctxType} c {{\n" +
            $"        {methodName}();\n" +
            "    }\n" +
            "\n" +
            "    public static void Main() {\n" +
            "        var p = new Program();\n" +
            $"        with var c = new {ctxType}();\n" +
            "        p.Caller();\n" +
            "    }\n" +
            "}\n";

        return new ContextCheckerScenario(
            ContextCheckerScenarioKind.CallerWithClauseSatisfied,
            source,
            expectedDiagnosticCount: 0);
    }

    /// <summary>
    /// Builds a scenario where a method has multiple with-clauses and all are
    /// satisfied by declarations before the call site.
    /// </summary>
    private static ContextCheckerScenario BuildMultipleClausesSatisfied(
        string ctxType1, string ctxType2, string methodName)
    {
        var source =
            $"class {ctxType1} {{ }}\n" +
            $"class {ctxType2} {{ }}\n" +
            "\n" +
            "class Program {\n" +
            $"    void {methodName}() with {ctxType1} a with {ctxType2} b {{ }}\n" +
            "\n" +
            "    public static void Main() {\n" +
            "        var p = new Program();\n" +
            $"        with var a = new {ctxType1}();\n" +
            $"        with var b = new {ctxType2}();\n" +
            $"        p.{methodName}();\n" +
            "    }\n" +
            "}\n";

        return new ContextCheckerScenario(
            ContextCheckerScenarioKind.MultipleClausesSatisfied,
            source,
            expectedDiagnosticCount: 0);
    }

    /// <summary>
    /// Builds a scenario where a method has multiple with-clauses but only one
    /// is satisfied. Expects one diagnostic for the unsatisfied type.
    /// </summary>
    private static ContextCheckerScenario BuildMultipleClausesPartiallyUnsatisfied(
        string ctxType1, string ctxType2, string methodName)
    {
        var source =
            $"class {ctxType1} {{ }}\n" +
            $"class {ctxType2} {{ }}\n" +
            "\n" +
            "class Program {\n" +
            $"    void {methodName}() with {ctxType1} a with {ctxType2} b {{ }}\n" +
            "\n" +
            "    public static void Main() {\n" +
            "        var p = new Program();\n" +
            $"        with var a = new {ctxType1}();\n" +
            $"        p.{methodName}();\n" +
            "    }\n" +
            "}\n";

        return new ContextCheckerScenario(
            ContextCheckerScenarioKind.MultipleClausesPartiallyUnsatisfied,
            source,
            expectedDiagnosticCount: 1);
    }

    /// <summary>
    /// Builds a scenario where a method is called through an interface type.
    /// The interface method declares a with-clause and context is provided.
    /// </summary>
    private static ContextCheckerScenario BuildInterfaceDispatchSatisfied(
        string ifaceName, string implName, string ctxType)
    {
        var source =
            $"class {ctxType} {{ }}\n" +
            "\n" +
            $"interface {ifaceName} {{\n" +
            $"    void DoWork() with {ctxType} ctx;\n" +
            "}\n" +
            "\n" +
            $"class {implName} : {ifaceName} {{\n" +
            $"    public void DoWork() with {ctxType} ctx {{ }}\n" +
            "}\n" +
            "\n" +
            "class Program {\n" +
            "    public static void Main() {\n" +
            $"        {ifaceName} svc = new {implName}();\n" +
            $"        with var c = new {ctxType}();\n" +
            "        svc.DoWork();\n" +
            "    }\n" +
            "}\n";

        return new ContextCheckerScenario(
            ContextCheckerScenarioKind.InterfaceDispatchSatisfied,
            source,
            expectedDiagnosticCount: 0);
    }

    /// <summary>
    /// Builds a scenario where a method is called through an interface type.
    /// The interface method declares a with-clause but no context is provided.
    /// </summary>
    private static ContextCheckerScenario BuildInterfaceDispatchUnsatisfied(
        string ifaceName, string implName, string ctxType)
    {
        var source =
            $"class {ctxType} {{ }}\n" +
            "\n" +
            $"interface {ifaceName} {{\n" +
            $"    void DoWork() with {ctxType} ctx;\n" +
            "}\n" +
            "\n" +
            $"class {implName} : {ifaceName} {{\n" +
            $"    public void DoWork() with {ctxType} ctx {{ }}\n" +
            "}\n" +
            "\n" +
            "class Program {\n" +
            "    public static void Main() {\n" +
            $"        {ifaceName} svc = new {implName}();\n" +
            "        svc.DoWork();\n" +
            "    }\n" +
            "}\n";

        return new ContextCheckerScenario(
            ContextCheckerScenarioKind.InterfaceDispatchUnsatisfied,
            source,
            expectedDiagnosticCount: 1);
    }

    /// <summary>
    /// Builds a scenario where context is declared AFTER the call site in the
    /// same block. The context is not yet available at the call site.
    /// </summary>
    private static ContextCheckerScenario BuildContextDeclaredAfterCallSite(
        string ctxType, string methodName)
    {
        var source =
            $"class {ctxType} {{ }}\n" +
            "\n" +
            "class Program {\n" +
            $"    void {methodName}() with {ctxType} ctx {{ }}\n" +
            "\n" +
            "    public static void Main() {\n" +
            "        var p = new Program();\n" +
            $"        p.{methodName}();\n" +
            $"        with var c = new {ctxType}();\n" +
            "    }\n" +
            "}\n";

        return new ContextCheckerScenario(
            ContextCheckerScenarioKind.ContextDeclaredAfterCallSite,
            source,
            expectedDiagnosticCount: 1);
    }

    // ─── Compilation helper ─────────────────────────────────────────────

    /// <summary>
    /// Compiles the given Mist source using the shared driver (in-process,
    /// no external process spawning) and returns only the MIST_CTX_0001
    /// diagnostics.
    /// </summary>
    private static (ImmutableArray<Diagnostic> ContextDiags, ImmutableArray<Diagnostic> AllDiags, bool CompileSucceeded)
        CompileAndGetContextDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_ctx_prop_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);

            var contextDiags = result.Diagnostics
                .Where(d => d.Id == ContextCheckerAnalyzer.MissingContext)
                .ToImmutableArray();

            return (contextDiags, result.Diagnostics, result.Succeeded || contextDiags.Length > 0);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Context checker soundness and completeness
    //
    // **Validates: Requirements 1.9, 2.4, 3.4, 3.6, 3.7, 8.6, 9.1, 9.2,
    //              9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 13.2, 13.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any valid Mist program where every call site of a method with `with`
    /// clauses has a matching context declaration (of the exact resolved type)
    /// reachable in the call graph, the context checker SHALL emit zero
    /// context-availability diagnostics. Conversely, for any program where a call
    /// site lacks a reachable context declaration for a required type, the context
    /// checker SHALL emit exactly one diagnostic per unsatisfied type at that call site.
    ///
    /// **Validates: Requirements 1.9, 2.4, 3.4, 3.6, 3.7, 8.6, 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 13.2, 13.3**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ContextCheckerSoundnessPropertyTests) },
        MaxTest = 100)]
    public Property ContextChecker_SoundnessAndCompletenessProperty(ContextCheckerScenario scenario)
    {
        var (contextDiags, allDiags, compiled) = CompileAndGetContextDiagnostics(scenario.Source);

        // Filter out non-context errors that would prevent the checker from running.
        // If there are parse or type errors unrelated to context, the scenario is
        // invalid and we skip it.
        var nonContextErrors = allDiags
            .Where(d => d.Severity == DiagnosticSeverity.Error
                        && d.Id != ContextCheckerAnalyzer.MissingContext
                        && d.Id != ContextCheckerAnalyzer.WithClauseContractViolation
                        && d.Id != ContextCheckerAnalyzer.DuplicateContextBinding)
            .ToImmutableArray();

        if (nonContextErrors.Length > 0)
        {
            // Scenario produced non-context errors — this shouldn't happen with
            // our generators, but if it does, treat as a test failure to investigate.
            return false.ToProperty()
                .Label($"Scenario produced unexpected non-context errors.\n" +
                       $"  Errors: {string.Join("; ", nonContextErrors.Select(d => $"{d.Id.Value}: {d.Message}"))}\n" +
                       $"  Scenario: {scenario}\n" +
                       $"  Source:\n{scenario.Source}");
        }

        var actualCount = contextDiags.Length;
        var expectedCount = scenario.ExpectedDiagnosticCount;

        if (actualCount != expectedCount)
        {
            return false.ToProperty()
                .Label($"Expected {expectedCount} MIST_CTX_0001 diagnostic(s) but got {actualCount}.\n" +
                       $"  Scenario: {scenario}\n" +
                       $"  Diagnostics: [{string.Join("; ", contextDiags.Select(d => d.Message))}]\n" +
                       $"  Source:\n{scenario.Source}");
        }

        return true.ToProperty()
            .Label($"Correct: {scenario.Kind} → {actualCount} diagnostic(s)");
    }
}

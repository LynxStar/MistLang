// Feature: structured-context, Property 5: Abstract type with-clause contract enforcement
//
// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**
//
// For any Mist program where an abstract type method declares `with` clauses, every
// implementing type's corresponding method SHALL declare the exact same `with`
// clauses (matched by resolved type, no variance). Conversely, for any implementing
// method that omits or mismatches a `with` clause from the abstract type method, the
// compiler SHALL emit a diagnostic.
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
/// Describes the kind of contract enforcement scenario being tested.
/// </summary>
public enum ContractScenarioKind
{
    /// <summary>
    /// Abstract type method has with-clause(s), implementing method matches exactly.
    /// No diagnostic expected.
    /// </summary>
    AbstractExactMatch,

    /// <summary>
    /// Abstract type method has with-clause(s), implementing method omits one or more.
    /// MIST_CTX_0006 diagnostic expected for each omitted clause.
    /// </summary>
    AbstractOmittedClause,

    /// <summary>
    /// Abstract type method has with-clause(s), implementing method declares a different type.
    /// MIST_CTX_0006 diagnostic expected for the mismatched clause.
    /// </summary>
    AbstractMismatchedType,

    /// <summary>
    /// Abstract type method has with-clause(s), implementing method has all required
    /// clauses plus an extra one. No diagnostic expected (extra clauses are allowed).
    /// </summary>
    AbstractExtraClause,
}

/// <summary>
/// A generated scenario for testing abstract type with-clause contract enforcement.
/// </summary>
public sealed class ContractEnforcementScenario
{
    public ContractScenarioKind Kind { get; }
    public string Source { get; }

    /// <summary>
    /// Whether MIST_CTX_0006 diagnostics are expected (for abstract type scenarios).
    /// </summary>
    public int ExpectedContractDiagnosticCount { get; }

    /// <summary>
    /// <summary>
    /// The context type names that should appear in diagnostics (if any).
    /// </summary>
    public ImmutableArray<string> ExpectedDiagnosticContextTypes { get; }

    public ContractEnforcementScenario(
        ContractScenarioKind kind,
        string source,
        int expectedContractDiagnosticCount,
        ImmutableArray<string> expectedDiagnosticContextTypes)
    {
        Kind = kind;
        Source = source;
        ExpectedContractDiagnosticCount = expectedContractDiagnosticCount;
        ExpectedDiagnosticContextTypes = expectedDiagnosticContextTypes;
    }

    public override string ToString()
    {
        return $"[{Kind}] expect {ExpectedContractDiagnosticCount} contract diag(s)";
    }
}

// ─── Property test class ─────────────────────────────────────────────────

/// <summary>
/// Property-based tests for abstract type with-clause contract enforcement.
///
/// Property 5: Abstract type with-clause contract enforcement
///
/// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**
/// </summary>
public sealed class ContextContractEnforcementPropertyTests
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
        ["Execute", "Process", "Handle", "Run", "Invoke"];

    private static readonly string[] AbstractNames =
        ["IService", "IHandler", "IProcessor", "IWorker", "IRunner"];

    private static readonly string[] ImplClassNames =
        ["ServiceImpl", "HandlerImpl", "ProcessorImpl", "WorkerImpl", "RunnerImpl"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<ContractEnforcementScenario> GenAbstractExactMatch()
    {
        return from ctxIdx in Gen.Choose(0, ContextTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from abstractIdx in Gen.Choose(0, AbstractNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplClassNames.Length - 1)
               select BuildAbstractExactMatchScenario(
                   ContextTypeNames[ctxIdx],
                   MethodNames[methodIdx],
                   AbstractNames[abstractIdx],
                   ImplClassNames[implIdx]);
    }

    private static Gen<ContractEnforcementScenario> GenAbstractOmittedClause()
    {
        return from ctxIdx in Gen.Choose(0, ContextTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from abstractIdx in Gen.Choose(0, AbstractNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplClassNames.Length - 1)
               select BuildAbstractOmittedClauseScenario(
                   ContextTypeNames[ctxIdx],
                   MethodNames[methodIdx],
                   AbstractNames[abstractIdx],
                   ImplClassNames[implIdx]);
    }

    private static Gen<ContractEnforcementScenario> GenAbstractMismatchedType()
    {
        return from ctxIdx1 in Gen.Choose(0, ContextTypeNames.Length - 1)
               from ctxIdx2 in Gen.Choose(0, ContextTypeNames.Length - 1)
               where ctxIdx1 != ctxIdx2
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from abstractIdx in Gen.Choose(0, AbstractNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplClassNames.Length - 1)
               select BuildAbstractMismatchedTypeScenario(
                   ContextTypeNames[ctxIdx1],
                   ContextTypeNames[ctxIdx2],
                   MethodNames[methodIdx],
                   AbstractNames[abstractIdx],
                   ImplClassNames[implIdx]);
    }

    private static Gen<ContractEnforcementScenario> GenAbstractExtraClause()
    {
        return from ctxIdx1 in Gen.Choose(0, ContextTypeNames.Length - 1)
               from ctxIdx2 in Gen.Choose(0, ContextTypeNames.Length - 1)
               where ctxIdx1 != ctxIdx2
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from abstractIdx in Gen.Choose(0, AbstractNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplClassNames.Length - 1)
               select BuildAbstractExtraClauseScenario(
                   ContextTypeNames[ctxIdx1],
                   ContextTypeNames[ctxIdx2],
                   MethodNames[methodIdx],
                   AbstractNames[abstractIdx],
                   ImplClassNames[implIdx]);
    }

    public static Gen<ContractEnforcementScenario> GenScenario()
    {
        return Gen.OneOf(
            GenAbstractExactMatch(),
            GenAbstractOmittedClause(),
            GenAbstractMismatchedType(),
            GenAbstractExtraClause());
    }

    public static Arbitrary<ContractEnforcementScenario> ArbitraryContractEnforcementScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Scenario builders ──────────────────────────────────────────────

    /// <summary>
    /// Abstract type method has a with-clause, implementing method matches exactly.
    /// No MIST_CTX_0006 diagnostic expected.
    /// </summary>
    private static ContractEnforcementScenario BuildAbstractExactMatchScenario(
        string ctxType, string methodName, string abstractName, string implName)
    {
        var source =
            $"class {ctxType} {{ }}\n" +
            $"abstract {abstractName} {{\n" +
            $"    void {methodName}() with {ctxType} ctx;\n" +
            "}\n" +
            $"class {implName} : {abstractName} {{\n" +
            $"    public void {methodName}() with {ctxType} ctx {{ }}\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() { }\n" +
            "}\n";

        return new ContractEnforcementScenario(
            ContractScenarioKind.AbstractExactMatch,
            source,
            expectedContractDiagnosticCount: 0,
            expectedDiagnosticContextTypes: ImmutableArray<string>.Empty);
    }

    /// <summary>
    /// Abstract type method has a with-clause, implementing method omits it.
    /// MIST_CTX_0006 diagnostic expected.
    /// </summary>
    private static ContractEnforcementScenario BuildAbstractOmittedClauseScenario(
        string ctxType, string methodName, string abstractName, string implName)
    {
        var source =
            $"class {ctxType} {{ }}\n" +
            $"abstract {abstractName} {{\n" +
            $"    void {methodName}() with {ctxType} ctx;\n" +
            "}\n" +
            $"class {implName} : {abstractName} {{\n" +
            $"    public void {methodName}() {{ }}\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() { }\n" +
            "}\n";

        return new ContractEnforcementScenario(
            ContractScenarioKind.AbstractOmittedClause,
            source,
            expectedContractDiagnosticCount: 1,
            expectedDiagnosticContextTypes: ImmutableArray.Create(ctxType));
    }

    /// <summary>
    /// Abstract type method has a with-clause of one type, implementing method declares
    /// a with-clause of a different type. MIST_CTX_0006 diagnostic expected for the
    /// missing required type.
    /// </summary>
    private static ContractEnforcementScenario BuildAbstractMismatchedTypeScenario(
        string requiredCtxType, string wrongCtxType, string methodName,
        string abstractName, string implName)
    {
        var source =
            $"class {requiredCtxType} {{ }}\n" +
            $"class {wrongCtxType} {{ }}\n" +
            $"abstract {abstractName} {{\n" +
            $"    void {methodName}() with {requiredCtxType} ctx;\n" +
            "}\n" +
            $"class {implName} : {abstractName} {{\n" +
            $"    public void {methodName}() with {wrongCtxType} ctx {{ }}\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() { }\n" +
            "}\n";

        return new ContractEnforcementScenario(
            ContractScenarioKind.AbstractMismatchedType,
            source,
            expectedContractDiagnosticCount: 1,
            expectedDiagnosticContextTypes: ImmutableArray.Create(requiredCtxType));
    }

    /// <summary>
    /// Abstract type method has a with-clause, implementing method has the required clause
    /// plus an extra one. No diagnostic expected (extra clauses are allowed).
    /// </summary>
    private static ContractEnforcementScenario BuildAbstractExtraClauseScenario(
        string requiredCtxType, string extraCtxType, string methodName,
        string abstractName, string implName)
    {
        var source =
            $"class {requiredCtxType} {{ }}\n" +
            $"class {extraCtxType} {{ }}\n" +
            $"abstract {abstractName} {{\n" +
            $"    void {methodName}() with {requiredCtxType} ctx;\n" +
            "}\n" +
            $"class {implName} : {abstractName} {{\n" +
            $"    public void {methodName}() with {requiredCtxType} ctx with {extraCtxType} extra {{ }}\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() { }\n" +
            "}\n";

        return new ContractEnforcementScenario(
            ContractScenarioKind.AbstractExtraClause,
            source,
            expectedContractDiagnosticCount: 0,
            expectedDiagnosticContextTypes: ImmutableArray<string>.Empty);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles the given Mist source using the shared CompilerDriver (in-process,
    /// no external process spawning).
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_ctx_pbt_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDir);
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
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Abstract type with-clause contract enforcement
    //
    // **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any Mist program where an abstract type method declares `with` clauses,
    /// every implementing type's corresponding method SHALL declare the exact same
    /// `with` clauses (matched by resolved type, no variance). Conversely, for any
    /// implementing method that omits or mismatches a `with` clause from the abstract type
    /// method, the compiler SHALL emit a diagnostic.
    ///
    /// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ContextContractEnforcementPropertyTests) },
        MaxTest = 100)]
    public Property ContractEnforcement_Property(ContractEnforcementScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var contractDiags = diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.WithClauseContractViolation)
            .ToImmutableArray();

        // Check abstract type contract diagnostics count.
        if(contractDiags.Length != scenario.ExpectedContractDiagnosticCount)
        {
            return false.ToProperty()
                .Label($"Expected {scenario.ExpectedContractDiagnosticCount} MIST_CTX_0006 diagnostic(s) " +
                       $"but got {contractDiags.Length}.\n" +
                       $"Diagnostics: [{string.Join("; ", contractDiags.Select(d => d.Message))}]\n" +
                       $"All diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{scenario.Source}");
        }

        // Check that expected context types appear in the diagnostic messages.
        foreach(var expectedType in scenario.ExpectedDiagnosticContextTypes)
        {
            if(!contractDiags.Any(d => d.Message.Contains(expectedType)))
            {
                return false.ToProperty()
                    .Label($"Expected MIST_CTX_0006 diagnostic mentioning '{expectedType}' " +
                           $"but none found.\n" +
                           $"Diagnostics: [{string.Join("; ", contractDiags.Select(d => d.Message))}]\n" +
                           $"Scenario: {scenario}\n" +
                           $"Source:\n{scenario.Source}");
            }
        }

        return true.ToProperty()
            .Label($"Contract enforcement correct for {scenario.Kind}");
    }
}
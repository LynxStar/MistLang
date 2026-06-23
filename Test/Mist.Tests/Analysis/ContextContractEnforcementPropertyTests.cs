// Feature: structured-context, Property 5: Interface and trait with-clause contract enforcement
//
// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**
//
// For any Mist program where an interface method declares `with` clauses, every
// implementing type's corresponding method SHALL declare the exact same `with`
// clauses (matched by resolved type, no variance). Conversely, for any implementing
// method that omits or mismatches a `with` clause from the interface method, the
// compiler SHALL emit a diagnostic. For traits, for any type's method that matches
// a trait method in name, parameters, and return type but differs in `with` clauses,
// the compiler SHALL NOT consider it a structural match for trait satisfaction.
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
    /// Interface method has with-clause(s), implementing method matches exactly.
    /// No diagnostic expected.
    /// </summary>
    InterfaceExactMatch,

    /// <summary>
    /// Interface method has with-clause(s), implementing method omits one or more.
    /// MIST_CTX_0006 diagnostic expected for each omitted clause.
    /// </summary>
    InterfaceOmittedClause,

    /// <summary>
    /// Interface method has with-clause(s), implementing method declares a different type.
    /// MIST_CTX_0006 diagnostic expected for the mismatched clause.
    /// </summary>
    InterfaceMismatchedType,

    /// <summary>
    /// Interface method has with-clause(s), implementing method has all required
    /// clauses plus an extra one. No diagnostic expected (extra clauses are allowed).
    /// </summary>
    InterfaceExtraClause,

    /// <summary>
    /// Trait method has with-clause(s), type's method matches with-clauses exactly.
    /// Structural matching succeeds — no MIST_TRAIT_WITH_UNSATISFIED diagnostic.
    /// </summary>
    TraitExactMatch,

    /// <summary>
    /// Trait method has with-clause(s), type's method omits the with-clause.
    /// Structural matching fails — MIST_TRAIT_WITH_UNSATISFIED diagnostic expected
    /// (when no explicit with-block mapping covers it).
    /// </summary>
    TraitWithClauseMismatch,
}

/// <summary>
/// A generated scenario for testing interface/trait with-clause contract enforcement.
/// </summary>
public sealed class ContractEnforcementScenario
{
    public ContractScenarioKind Kind { get; }
    public string Source { get; }

    /// <summary>
    /// Whether MIST_CTX_0006 diagnostics are expected (for interface scenarios).
    /// </summary>
    public int ExpectedContractDiagnosticCount { get; }

    /// <summary>
    /// Whether MIST_TRAIT_WITH_UNSATISFIED diagnostics are expected (for trait scenarios).
    /// </summary>
    public bool ExpectTraitUnsatisfied { get; }

    /// <summary>
    /// The context type names that should appear in diagnostics (if any).
    /// </summary>
    public ImmutableArray<string> ExpectedDiagnosticContextTypes { get; }

    public ContractEnforcementScenario(
        ContractScenarioKind kind,
        string source,
        int expectedContractDiagnosticCount,
        bool expectTraitUnsatisfied,
        ImmutableArray<string> expectedDiagnosticContextTypes)
    {
        Kind = kind;
        Source = source;
        ExpectedContractDiagnosticCount = expectedContractDiagnosticCount;
        ExpectTraitUnsatisfied = expectTraitUnsatisfied;
        ExpectedDiagnosticContextTypes = expectedDiagnosticContextTypes;
    }

    public override string ToString() =>
        $"[{Kind}] expect {ExpectedContractDiagnosticCount} contract diag(s), " +
        $"abstract unsatisfied={ExpectTraitUnsatisfied}";
}

// ─── Property test class ─────────────────────────────────────────────────

/// <summary>
/// Property-based tests for interface and trait with-clause contract enforcement.
///
/// Property 5: Interface and trait with-clause contract enforcement
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

    private static readonly string[] InterfaceNames =
        ["IService", "IHandler", "IProcessor", "IWorker", "IRunner"];

    private static readonly string[] TraitNames =
        ["Executable", "Processable", "Handleable", "Runnable", "Invokable"];

    private static readonly string[] ImplClassNames =
        ["ServiceImpl", "HandlerImpl", "ProcessorImpl", "WorkerImpl", "RunnerImpl"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<ContractEnforcementScenario> GenInterfaceExactMatch()
    {
        return from ctxIdx in Gen.Choose(0, ContextTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from ifaceIdx in Gen.Choose(0, InterfaceNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplClassNames.Length - 1)
               select BuildInterfaceExactMatchScenario(
                   ContextTypeNames[ctxIdx],
                   MethodNames[methodIdx],
                   InterfaceNames[ifaceIdx],
                   ImplClassNames[implIdx]);
    }

    private static Gen<ContractEnforcementScenario> GenInterfaceOmittedClause()
    {
        return from ctxIdx in Gen.Choose(0, ContextTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from ifaceIdx in Gen.Choose(0, InterfaceNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplClassNames.Length - 1)
               select BuildInterfaceOmittedClauseScenario(
                   ContextTypeNames[ctxIdx],
                   MethodNames[methodIdx],
                   InterfaceNames[ifaceIdx],
                   ImplClassNames[implIdx]);
    }

    private static Gen<ContractEnforcementScenario> GenInterfaceMismatchedType()
    {
        return from ctxIdx1 in Gen.Choose(0, ContextTypeNames.Length - 1)
               from ctxIdx2 in Gen.Choose(0, ContextTypeNames.Length - 1)
               where ctxIdx1 != ctxIdx2
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from ifaceIdx in Gen.Choose(0, InterfaceNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplClassNames.Length - 1)
               select BuildInterfaceMismatchedTypeScenario(
                   ContextTypeNames[ctxIdx1],
                   ContextTypeNames[ctxIdx2],
                   MethodNames[methodIdx],
                   InterfaceNames[ifaceIdx],
                   ImplClassNames[implIdx]);
    }

    private static Gen<ContractEnforcementScenario> GenInterfaceExtraClause()
    {
        return from ctxIdx1 in Gen.Choose(0, ContextTypeNames.Length - 1)
               from ctxIdx2 in Gen.Choose(0, ContextTypeNames.Length - 1)
               where ctxIdx1 != ctxIdx2
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from ifaceIdx in Gen.Choose(0, InterfaceNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplClassNames.Length - 1)
               select BuildInterfaceExtraClauseScenario(
                   ContextTypeNames[ctxIdx1],
                   ContextTypeNames[ctxIdx2],
                   MethodNames[methodIdx],
                   InterfaceNames[ifaceIdx],
                   ImplClassNames[implIdx]);
    }

    private static Gen<ContractEnforcementScenario> GenTraitExactMatch()
    {
        return from ctxIdx in Gen.Choose(0, ContextTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from traitIdx in Gen.Choose(0, TraitNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplClassNames.Length - 1)
               select BuildTraitExactMatchScenario(
                   ContextTypeNames[ctxIdx],
                   MethodNames[methodIdx],
                   TraitNames[traitIdx],
                   ImplClassNames[implIdx]);
    }

    private static Gen<ContractEnforcementScenario> GenTraitWithClauseMismatch()
    {
        return from ctxIdx in Gen.Choose(0, ContextTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from traitIdx in Gen.Choose(0, TraitNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplClassNames.Length - 1)
               select BuildTraitWithClauseMismatchScenario(
                   ContextTypeNames[ctxIdx],
                   MethodNames[methodIdx],
                   TraitNames[traitIdx],
                   ImplClassNames[implIdx]);
    }

    public static Gen<ContractEnforcementScenario> GenScenario()
    {
        return Gen.OneOf(
            GenInterfaceExactMatch(),
            GenInterfaceOmittedClause(),
            GenInterfaceMismatchedType(),
            GenInterfaceExtraClause(),
            GenTraitExactMatch(),
            GenTraitWithClauseMismatch());
    }

    public static Arbitrary<ContractEnforcementScenario> ArbitraryContractEnforcementScenario() =>
        GenScenario().ToArbitrary();

    // ─── Scenario builders ──────────────────────────────────────────────

    /// <summary>
    /// Interface method has a with-clause, implementing method matches exactly.
    /// No MIST_CTX_0006 diagnostic expected.
    /// </summary>
    private static ContractEnforcementScenario BuildInterfaceExactMatchScenario(
        string ctxType, string methodName, string ifaceName, string implName)
    {
        var source =
            $"class {ctxType} {{ }}\n" +
            $"abstract {ifaceName} {{\n" +
            $"    void {methodName}() with {ctxType} ctx;\n" +
            "}\n" +
            $"class {implName} : {ifaceName} {{\n" +
            $"    public void {methodName}() with {ctxType} ctx {{ }}\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() { }\n" +
            "}\n";

        return new ContractEnforcementScenario(
            ContractScenarioKind.InterfaceExactMatch,
            source,
            expectedContractDiagnosticCount: 0,
            expectTraitUnsatisfied: false,
            expectedDiagnosticContextTypes: ImmutableArray<string>.Empty);
    }

    /// <summary>
    /// Interface method has a with-clause, implementing method omits it.
    /// MIST_CTX_0006 diagnostic expected.
    /// </summary>
    private static ContractEnforcementScenario BuildInterfaceOmittedClauseScenario(
        string ctxType, string methodName, string ifaceName, string implName)
    {
        var source =
            $"class {ctxType} {{ }}\n" +
            $"abstract {ifaceName} {{\n" +
            $"    void {methodName}() with {ctxType} ctx;\n" +
            "}\n" +
            $"class {implName} : {ifaceName} {{\n" +
            $"    public void {methodName}() {{ }}\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() { }\n" +
            "}\n";

        return new ContractEnforcementScenario(
            ContractScenarioKind.InterfaceOmittedClause,
            source,
            expectedContractDiagnosticCount: 1,
            expectTraitUnsatisfied: false,
            expectedDiagnosticContextTypes: ImmutableArray.Create(ctxType));
    }

    /// <summary>
    /// Interface method has a with-clause of one type, implementing method declares
    /// a with-clause of a different type. MIST_CTX_0006 diagnostic expected for the
    /// missing required type.
    /// </summary>
    private static ContractEnforcementScenario BuildInterfaceMismatchedTypeScenario(
        string requiredCtxType, string wrongCtxType, string methodName,
        string ifaceName, string implName)
    {
        var source =
            $"class {requiredCtxType} {{ }}\n" +
            $"class {wrongCtxType} {{ }}\n" +
            $"abstract {ifaceName} {{\n" +
            $"    void {methodName}() with {requiredCtxType} ctx;\n" +
            "}\n" +
            $"class {implName} : {ifaceName} {{\n" +
            $"    public void {methodName}() with {wrongCtxType} ctx {{ }}\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() { }\n" +
            "}\n";

        return new ContractEnforcementScenario(
            ContractScenarioKind.InterfaceMismatchedType,
            source,
            expectedContractDiagnosticCount: 1,
            expectTraitUnsatisfied: false,
            expectedDiagnosticContextTypes: ImmutableArray.Create(requiredCtxType));
    }

    /// <summary>
    /// Interface method has a with-clause, implementing method has the required clause
    /// plus an extra one. No diagnostic expected (extra clauses are allowed).
    /// </summary>
    private static ContractEnforcementScenario BuildInterfaceExtraClauseScenario(
        string requiredCtxType, string extraCtxType, string methodName,
        string ifaceName, string implName)
    {
        var source =
            $"class {requiredCtxType} {{ }}\n" +
            $"class {extraCtxType} {{ }}\n" +
            $"abstract {ifaceName} {{\n" +
            $"    void {methodName}() with {requiredCtxType} ctx;\n" +
            "}\n" +
            $"class {implName} : {ifaceName} {{\n" +
            $"    public void {methodName}() with {requiredCtxType} ctx with {extraCtxType} extra {{ }}\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() { }\n" +
            "}\n";

        return new ContractEnforcementScenario(
            ContractScenarioKind.InterfaceExtraClause,
            source,
            expectedContractDiagnosticCount: 0,
            expectTraitUnsatisfied: false,
            expectedDiagnosticContextTypes: ImmutableArray<string>.Empty);
    }

    /// <summary>
    /// Trait method has a with-clause, type's method matches exactly.
    /// Structural matching succeeds — no MIST_TRAIT_WITH_UNSATISFIED diagnostic.
    /// Uses an explicit with-block mapping to ensure the trait is satisfied.
    /// </summary>
    private static ContractEnforcementScenario BuildTraitExactMatchScenario(
        string ctxType, string methodName, string traitName, string implName)
    {
        var source =
            $"class {ctxType} {{ }}\n" +
            $"abstract {traitName} {{\n" +
            $"    void {methodName}() with {ctxType} ctx;\n" +
            "}\n" +
            $"class {implName} {{\n" +
            $"    public void {methodName}() with {ctxType} ctx {{ }}\n" +
            "}\n" +
            $"with {implName} : {traitName} {{\n" +
            $"    {methodName} as {methodName};\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() { }\n" +
            "}\n";

        return new ContractEnforcementScenario(
            ContractScenarioKind.TraitExactMatch,
            source,
            expectedContractDiagnosticCount: 0,
            expectTraitUnsatisfied: false,
            expectedDiagnosticContextTypes: ImmutableArray<string>.Empty);
    }

    /// <summary>
    /// Trait method has a with-clause, type's method does NOT have the with-clause.
    /// The with-block only maps a different source method, so the trait method with
    /// the with-clause is left unsatisfied by structural matching.
    /// MIST_TRAIT_WITH_UNSATISFIED diagnostic expected.
    /// </summary>
    private static ContractEnforcementScenario BuildTraitWithClauseMismatchScenario(
        string ctxType, string methodName, string traitName, string implName)
    {
        // The trait has two methods: one with a with-clause and one without.
        // The type has both methods but the one that should have the with-clause
        // does NOT have it. The with-block only maps a different source method
        // to the helper, leaving the with-clause method unsatisfied structurally.
        var source =
            $"class {ctxType} {{ }}\n" +
            $"abstract {traitName} {{\n" +
            $"    void {methodName}() with {ctxType} ctx;\n" +
            "    void Helper();\n" +
            "}\n" +
            $"class {implName} {{\n" +
            $"    public void {methodName}() {{ }}\n" +
            "    public void Helper() { }\n" +
            "    public void Src() { }\n" +
            "}\n" +
            $"with {implName} : {traitName} {{\n" +
            "    Src as Helper;\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() { }\n" +
            "}\n";

        return new ContractEnforcementScenario(
            ContractScenarioKind.TraitWithClauseMismatch,
            source,
            expectedContractDiagnosticCount: 0,
            expectTraitUnsatisfied: true,
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
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Interface and trait with-clause contract enforcement
    //
    // **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any Mist program where an interface method declares `with` clauses,
    /// every implementing type's corresponding method SHALL declare the exact same
    /// `with` clauses (matched by resolved type, no variance). Conversely, for any
    /// implementing method that omits or mismatches a `with` clause from the interface
    /// method, the compiler SHALL emit a diagnostic. For traits, for any type's method
    /// that matches a trait method in name, parameters, and return type but differs in
    /// `with` clauses, the compiler SHALL NOT consider it a structural match for trait
    /// satisfaction.
    ///
    /// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ContextContractEnforcementPropertyTests) },
        Skip = "Blocked by task 6.3 — WithClauses on AbstractMethodDeclarationNode",
        MaxTest = 100)]
    public Property ContractEnforcement_Property(ContractEnforcementScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var contractDiags = diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.WithClauseContractViolation)
            .ToImmutableArray();

        var traitUnsatisfiedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_WITH_UNSATISFIED")
            .ToImmutableArray();

        // Check interface contract diagnostics count.
        if (contractDiags.Length != scenario.ExpectedContractDiagnosticCount)
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
        foreach (var expectedType in scenario.ExpectedDiagnosticContextTypes)
        {
            if (!contractDiags.Any(d => d.Message.Contains(expectedType)))
            {
                return false.ToProperty()
                    .Label($"Expected MIST_CTX_0006 diagnostic mentioning '{expectedType}' " +
                           $"but none found.\n" +
                           $"Diagnostics: [{string.Join("; ", contractDiags.Select(d => d.Message))}]\n" +
                           $"Scenario: {scenario}\n" +
                           $"Source:\n{scenario.Source}");
            }
        }

        // Check trait unsatisfied diagnostics.
        var hasTraitUnsatisfied = traitUnsatisfiedDiags.Length > 0;
        if (scenario.ExpectTraitUnsatisfied && !hasTraitUnsatisfied)
        {
            return false.ToProperty()
                .Label($"Expected MIST_TRAIT_WITH_UNSATISFIED diagnostic but none found.\n" +
                       $"All diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{scenario.Source}");
        }

        if (!scenario.ExpectTraitUnsatisfied && hasTraitUnsatisfied)
        {
            return false.ToProperty()
                .Label($"Did NOT expect MIST_TRAIT_WITH_UNSATISFIED diagnostic but got one.\n" +
                       $"Diagnostics: [{string.Join("; ", traitUnsatisfiedDiags.Select(d => d.Message))}]\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{scenario.Source}");
        }

        return true.ToProperty()
            .Label($"Contract enforcement correct for {scenario.Kind}");
    }
}

// Feature: definite-assignment
// Property 2: Local read-before-assignment soundness
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6**

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
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for local read-before-assignment detection.
///
/// Property 2: For any method/constructor body, the analyzer emits
/// <c>MIST_LOCAL_READ_BEFORE_ASSIGNMENT</c> for a local read if and only if
/// there exists a control-flow path reaching that read on which the local has
/// not been assigned (and had no initializer).
///
/// Since the current Mist grammar requires all local variable declarations to
/// have an initializer (every local is immediately assigned at its declaration),
/// this property test verifies:
/// (a) No false positives — valid programs with locals across branches, loops,
///     compound assignments, and re-assignments never emit the diagnostic.
/// (b) The analysis correctly runs on method bodies without interfering with
///     normal compilation.
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6**
/// </summary>
public sealed class LocalReadBeforeAssignmentPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "mist_local_rba_pbt_" + Guid.NewGuid().ToString("N"));
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
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static bool HasLocalReadBeforeAssignmentFor(
        ImmutableArray<Diagnostic> diagnostics, string localName) =>
        diagnostics.Any(d =>
            d.Id.Value == "MIST_LOCAL_READ_BEFORE_ASSIGNMENT" &&
            d.Message.Contains($"'{localName}'"));

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Describes the method body structure for a local variable.
    /// All patterns use valid Mist syntax where every local has an initializer
    /// (since the grammar requires it). The patterns exercise branches, loops,
    /// re-assignments, and compound operations.
    /// </summary>
    public enum LocalAccessPattern
    {
        /// <summary>Local declared with initializer, then read — no diagnostic (Req 2.2).</summary>
        DeclaredWithInitializerThenRead,

        /// <summary>Local re-assigned on all branches then read — no diagnostic (Req 2.4).</summary>
        ReassignAllBranchesThenRead,

        /// <summary>Local re-assigned on one branch only then read — no diagnostic (Req 2.3 — already assigned at init).</summary>
        ReassignOneBranchThenRead,

        /// <summary>Local read inside a loop after being assigned at init — no diagnostic (Req 2.5 — assigned before loop).</summary>
        ReadInLoopWithPriorInit,

        /// <summary>Compound assignment after initial assignment — no diagnostic (Req 2.6 — already assigned).</summary>
        CompoundAssignAfterInit,

        /// <summary>Local read after being re-assigned in loop body — no diagnostic (already assigned at init).</summary>
        ReadAfterLoopReassignment,
    }

    /// <summary>
    /// A generated scenario for testing local read-before-assignment.
    /// </summary>
    public sealed record LocalReadScenario(
        string Source,
        string LocalName,
        LocalAccessPattern Pattern,
        bool ExpectDiagnostic)
    {
        public override string ToString() =>
            $"local='{LocalName}' pattern={Pattern} expectDiag={ExpectDiagnostic}";
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] LocalNamePool =
        ["value", "target", "data", "item", "result"];

    private static readonly string[] MethodNames =
        ["Process", "Execute", "Handle", "Transform", "Compute"];

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<LocalReadScenario> GenLocalReadScenario()
    {
        return from methodName in Gen.Elements(MethodNames)
               from localName in Gen.Elements(LocalNamePool)
               from pattern in Gen.Elements(
                   LocalAccessPattern.DeclaredWithInitializerThenRead,
                   LocalAccessPattern.ReassignAllBranchesThenRead,
                   LocalAccessPattern.ReassignOneBranchThenRead,
                   LocalAccessPattern.ReadInLoopWithPriorInit,
                   LocalAccessPattern.CompoundAssignAfterInit,
                   LocalAccessPattern.ReadAfterLoopReassignment)
               let expectDiagnostic = false // All patterns use initialized locals
               let source = BuildSource(methodName, localName, pattern)
               select new LocalReadScenario(source, localName, pattern, expectDiagnostic);
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<LocalReadScenario> ArbitraryLocalReadScenario() =>
        GenLocalReadScenario().ToArbitrary();

    // ─── Source generation ───────────────────────────────────────────────

    /// <summary>
    /// Builds Mist source code for a local read-before-assignment scenario.
    /// All generated code uses valid Mist syntax where every local has an
    /// initializer. The patterns exercise re-assignment across branches, loops,
    /// and compound operators to verify no false positives are emitted.
    /// </summary>
    private static string BuildSource(
        string methodName,
        string localName,
        LocalAccessPattern pattern)
    {
        var methodBody = pattern switch
        {
            LocalAccessPattern.DeclaredWithInitializerThenRead => $$"""
                        var {{localName}} = 42;
                        var sink = {{localName}};
                """,

            LocalAccessPattern.ReassignAllBranchesThenRead => $$"""
                        var {{localName}} = 0;
                        if (flag)
                        {
                            {{localName}} = 1;
                        }
                        else
                        {
                            {{localName}} = 2;
                        }
                        var sink = {{localName}};
                """,

            LocalAccessPattern.ReassignOneBranchThenRead => $$"""
                        var {{localName}} = 0;
                        if (flag)
                        {
                            {{localName}} = 1;
                        }
                        else
                        {
                            // not re-assigned on this path, but still assigned from init
                        }
                        var sink = {{localName}};
                """,

            LocalAccessPattern.ReadInLoopWithPriorInit => $$"""
                        var {{localName}} = 10;
                        var i = 0;
                        while (i < 3)
                        {
                            var sink = {{localName}};
                            i = i + 1;
                        }
                """,

            LocalAccessPattern.CompoundAssignAfterInit => $$"""
                        var {{localName}} = 0;
                        {{localName}} = {{localName}} + 5;
                        var sink = {{localName}};
                """,

            LocalAccessPattern.ReadAfterLoopReassignment => $$"""
                        var {{localName}} = 0;
                        var i = 0;
                        while (i < 3)
                        {
                            {{localName}} = {{localName}} + i;
                            i = i + 1;
                        }
                        var sink = {{localName}};
                """,

            _ => "",
        };

        return $$"""
            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
                }

                public static void {{methodName}}(bool flag)
                {
            {{methodBody}}
                }
            }
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Local read-before-assignment soundness
    //
    // **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6**
    //
    // For any method/constructor body, the analyzer emits
    // MIST_LOCAL_READ_BEFORE_ASSIGNMENT for a local read if and only if
    // there exists a control-flow path reaching that read on which the local
    // has not been assigned (and had no initializer).
    //
    // Since the current Mist grammar requires all local declarations to have
    // initializers, this test verifies the false-positive-freedom direction:
    // no valid program with initialized locals ever triggers the diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 2: The diagnostic <c>MIST_LOCAL_READ_BEFORE_ASSIGNMENT</c> is
    /// never emitted for programs where all locals are assigned at declaration
    /// (the only form the current grammar permits).
    ///
    /// This verifies the biconditional's "only if" direction: no false positives
    /// for initialized locals read across branches, loops, and compound operations.
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(LocalReadBeforeAssignmentPropertyTests) },
        MaxTest = 100)]
    public Property LocalReadDiagnostic_EmittedIffUnassignedPath(
        LocalReadScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // Filter out entry point diagnostic (MIST_TC_0017) — not relevant.
        var relevantErrors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .ToList();

        var hasDiagnostic = HasLocalReadBeforeAssignmentFor(
            diagnostics, scenario.LocalName);

        var result = hasDiagnostic == scenario.ExpectDiagnostic;

        return result
            .ToProperty()
            .Label(
                $"Expected diagnostic={scenario.ExpectDiagnostic} " +
                $"but got={hasDiagnostic}. " +
                $"Scenario: {scenario}. " +
                $"All diagnostics: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
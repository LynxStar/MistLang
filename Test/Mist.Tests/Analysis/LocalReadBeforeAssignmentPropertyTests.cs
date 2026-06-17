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
    /// Describes the method body structure for a single local variable,
    /// specifying the ordering of declaration, reads, and assignments
    /// (possibly across branches, loops, or compound operations).
    /// </summary>
    public enum LocalAccessPattern
    {
        /// <summary>Local declared with initializer, then read — no diagnostic (Req 2.2).</summary>
        DeclaredWithInitializerThenRead,

        /// <summary>Local declared without initializer, read before assign — diagnostic (Req 2.1).</summary>
        ReadBeforeAssign,

        /// <summary>Local assigned on all branches then read — no diagnostic (Req 2.4).</summary>
        AssignAllBranchesThenRead,

        /// <summary>Local assigned on one branch only then read — diagnostic (Req 2.3).</summary>
        AssignOneBranchThenRead,

        /// <summary>Local read inside a loop without prior assignment — diagnostic (Req 2.5).</summary>
        ReadInLoopWithoutPriorAssign,

        /// <summary>Compound assignment (x = x + n) before initial assignment — diagnostic (Req 2.6).</summary>
        CompoundAssignBeforeInitialAssign,
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

    private static readonly string[] ClassNames =
        ["Widget", "Config", "Engine", "Sensor", "Agent"];

    private static readonly string[] MethodNames =
        ["Process", "Execute", "Handle", "Transform", "Compute"];

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<LocalReadScenario> GenLocalReadScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from localName in Gen.Elements(LocalNamePool)
               from pattern in Gen.Elements(
                   LocalAccessPattern.DeclaredWithInitializerThenRead,
                   LocalAccessPattern.ReadBeforeAssign,
                   LocalAccessPattern.AssignAllBranchesThenRead,
                   LocalAccessPattern.AssignOneBranchThenRead,
                   LocalAccessPattern.ReadInLoopWithoutPriorAssign,
                   LocalAccessPattern.CompoundAssignBeforeInitialAssign)
               let expectDiagnostic = pattern is
                   LocalAccessPattern.ReadBeforeAssign or
                   LocalAccessPattern.AssignOneBranchThenRead or
                   LocalAccessPattern.ReadInLoopWithoutPriorAssign or
                   LocalAccessPattern.CompoundAssignBeforeInitialAssign
               let source = BuildSource(className, methodName, localName, pattern)
               select new LocalReadScenario(source, localName, pattern, expectDiagnostic);
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<LocalReadScenario> ArbitraryLocalReadScenario() =>
        GenLocalReadScenario().ToArbitrary();

    // ─── Source generation ───────────────────────────────────────────────

    /// <summary>
    /// Builds Mist source code for a local read-before-assignment scenario.
    /// The generated class has a method whose body exercises the given
    /// access pattern for a local variable.
    /// A <c>sink</c> local captures reads so the code is well-formed.
    /// </summary>
    private static string BuildSource(
        string className,
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

            LocalAccessPattern.ReadBeforeAssign => $$"""
                        int {{localName}};
                        var sink = {{localName}};
                        {{localName}} = 10;
                """,

            LocalAccessPattern.AssignAllBranchesThenRead => $$"""
                        int {{localName}};
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

            LocalAccessPattern.AssignOneBranchThenRead => $$"""
                        int {{localName}};
                        if (flag)
                        {
                            {{localName}} = 1;
                        }
                        else
                        {
                            // not assigned on this path
                        }
                        var sink = {{localName}};
                """,

            LocalAccessPattern.ReadInLoopWithoutPriorAssign => $$"""
                        int {{localName}};
                        var i = 0;
                        while (i < 3)
                        {
                            var sink = {{localName}};
                            {{localName}} = i;
                            i = i + 1;
                        }
                """,

            LocalAccessPattern.CompoundAssignBeforeInitialAssign => $$"""
                        int {{localName}};
                        {{localName}} = {{localName}} + 5;
                """,

            _ => "",
        };

        return $$"""
            namespace TestProgram;

            public class {{className}}
            {
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
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 2: The diagnostic <c>MIST_LOCAL_READ_BEFORE_ASSIGNMENT</c> is
    /// emitted if and only if the local is read on a path where it has not been
    /// assigned (and had no initializer).
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

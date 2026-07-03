// Feature: adhoc-refinement-constraints, Property 7: Flow narrowing invalidation
// **Validates: Requirements 8.5**

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

/// <summary>
/// Property-based tests for flow narrowing invalidation on reassignment.
///
/// Property 7: For any method parameter with an adhoc refinement constraint, if the
/// parameter is reassigned within the method body, the type checker SHALL reject
/// subsequent uses of that parameter as the refined type.
///
/// Uses the full compilation pipeline (lexer → parser → semantic model → type checker) via
/// a shared CompilerDriver instance to comply with the test-process-spawning policy.
///
/// **Validates: Requirements 8.5**
/// </summary>
public class AdhocRefinementFlowNarrowingPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
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
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface scenario records

    /// <summary>
    /// Scenario for flow narrowing invalidation: a method with a parameter constrained
    /// by an adhoc refinement, where the parameter is reassigned and then used as the
    /// refined type. The type checker should reject the post-reassignment usage.
    /// </summary>
    public sealed class FlowNarrowingInvalidationScenario
    {
        public string Operator { get; }
        public int Threshold { get; }
        public int ReassignmentValue { get; }

        public FlowNarrowingInvalidationScenario(string op, int threshold, int reassignmentValue)
        {
            Operator = op;
            Threshold = threshold;
            ReassignmentValue = reassignmentValue;
        }

        /// <summary>
        /// Generates source where the parameter is reassigned and then used as the
        /// refined type. The type checker should always reject this because the
        /// narrowing is invalidated by the reassignment regardless of the new value.
        /// </summary>
        public string ToSourceWithReassignment()
        {
            return $$"""
            type Constrained = int {{Operator}} {{Threshold}};
            class Program {
                void Process(int x) where x {{Operator}} {{Threshold}} {
                    x = {{ReassignmentValue}};
                    Constrained y = x;
                }
                public static void Main() { }
            }
            """;
        }

        /// <summary>
        /// Generates source where the parameter is used as the refined type WITHOUT
        /// reassignment. The type checker should accept this because the adhoc
        /// refinement constraint establishes flow narrowing.
        /// </summary>
        public string ToSourceWithoutReassignment()
        {
            return $$"""
            type Constrained = int {{Operator}} {{Threshold}};
            class Program {
                void Process(int x) where x {{Operator}} {{Threshold}} {
                    Constrained y = x;
                }
                public static void Main() { }
            }
            """;
        }

        public override string ToString()
        {
            return $"Process(int x) where x {Operator} {Threshold}, reassign={ReassignmentValue}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] Operators = [">=", "<=", ">", "<"];

    private static Gen<FlowNarrowingInvalidationScenario> GenFlowNarrowingScenario()
    {
        return from op in Gen.Elements(Operators)
               from threshold in Gen.Choose(0, 100)
               from reassignValue in Gen.Choose(0, 100)
               select new FlowNarrowingInvalidationScenario(op, threshold, reassignValue);
    }

    public static Arbitrary<FlowNarrowingInvalidationScenario> ArbitraryFlowNarrowingScenario()
    {
        return GenFlowNarrowingScenario().ToArbitrary();
    }

    // ─── Property Tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 7a: Reassignment invalidates flow narrowing.
    ///
    /// For any method parameter with an adhoc refinement constraint, if the parameter
    /// is reassigned within the method body, subsequent usage of that parameter as the
    /// refined type SHALL be rejected with MIST_TC_0098 (regardless of the reassigned value).
    ///
    /// **Validates: Requirements 8.5**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AdhocRefinementFlowNarrowingPropertyTests) })]
    public Property Reassignment_InvalidatesNarrowing_RejectsRefinedUsage(
        FlowNarrowingInvalidationScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.ToSourceWithReassignment());

        var refinementDiags = diagnostics
            .Where(d => d.Id == TypeCheckDiagnostics.RefinementNonConstantAssignment.Id)
            .ToList();

        // After reassignment, the narrowing is invalidated — the type checker must
        // reject the assignment to the refined type with exactly one MIST_TC_0098.
        var passed = refinementDiags.Count == 1;

        return passed.ToProperty()
            .Label($"{scenario}. Expected 1 MIST_TC_0098, got {refinementDiags.Count}. " +
                   $"All errors: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// Property 7b: Without reassignment, flow narrowing allows refined usage.
    ///
    /// For any method parameter with an adhoc refinement constraint, if the parameter
    /// is NOT reassigned within the method body, usage of that parameter as the refined
    /// type SHALL be accepted (no MIST_TC_0098 diagnostic).
    ///
    /// This is the complementary property that confirms narrowing works correctly
    /// before invalidation, ensuring the test is meaningful.
    ///
    /// **Validates: Requirements 8.1**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AdhocRefinementFlowNarrowingPropertyTests) })]
    public Property WithoutReassignment_NarrowingAllowsRefinedUsage(
        FlowNarrowingInvalidationScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.ToSourceWithoutReassignment());

        var refinementDiags = diagnostics
            .Where(d => d.Id == TypeCheckDiagnostics.RefinementNonConstantAssignment.Id)
            .ToList();

        // Without reassignment, the adhoc refinement constraint establishes narrowing —
        // the type checker must accept the assignment to the refined type.
        var passed = refinementDiags.Count == 0;

        return passed.ToProperty()
            .Label($"{scenario}. Expected 0 MIST_TC_0098, got {refinementDiags.Count}. " +
                   $"All errors: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
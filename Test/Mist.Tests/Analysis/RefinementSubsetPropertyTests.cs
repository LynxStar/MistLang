// Feature: refinement-types, Property 5: Subset relationship correctness

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
/// Property-based tests verifying subset relationship correctness for refinement
/// types. For any two refinement types with the same base type and simple
/// relational predicates (<c>>= a</c> and <c>>= b</c>), the type checker SHALL
/// accept assignment from the first to the second if and only if <c>a >= b</c>
/// (the source is more constrained).
///
/// Also verifies compound predicate subset proving: for
/// <c>type Source = int >= a and &lt;= b;</c> and
/// <c>type Target = int >= c and &lt;= d;</c>, the type checker SHALL accept
/// assignment iff <c>a >= c</c> and <c>b &lt;= d</c>.
///
/// Uses the full compilation pipeline via a shared CompilerDriver instance to
/// comply with the test-process-spawning policy.
///
/// **Validates: Requirements 9.1, 9.2, 9.5**
/// </summary>
public class RefinementSubsetPropertyTests
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

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents a simple subset scenario: two refinement types with <c>>= N</c>
    /// predicates. <c>type Source = int >= SourceThreshold;</c> and
    /// <c>type Target = int >= TargetThreshold;</c>. Assignment from Source to
    /// Target is valid iff SourceThreshold >= TargetThreshold.
    /// </summary>
#pragma warning disable CA1034
    public record SimpleSubsetScenario(int SourceThreshold, int TargetThreshold)
    {
        /// <summary>Whether the source is a subset of the target (source is more constrained).</summary>
        public bool ShouldAccept => SourceThreshold >= TargetThreshold;

        /// <summary>A literal that satisfies the source predicate.</summary>
        public int Literal => Math.Max(SourceThreshold, TargetThreshold);

        public override string ToString() =>
            $"Source >= {SourceThreshold}, Target >= {TargetThreshold} (expect {(ShouldAccept ? "accept" : "reject")})";
    }
#pragma warning restore CA1034

    /// <summary>
    /// Represents a compound subset scenario: two refinement types with
    /// <c>>= lower and &lt;= upper</c> predicates.
    /// <c>type Source = int >= A and &lt;= B;</c> and
    /// <c>type Target = int >= C and &lt;= D;</c>.
    /// Assignment is valid iff A >= C and B &lt;= D.
    /// </summary>
#pragma warning disable CA1034
    public record CompoundSubsetScenario(int SourceLower, int SourceUpper, int TargetLower, int TargetUpper)
    {
        /// <summary>Whether the source range is a subset of the target range.</summary>
        public bool ShouldAccept => SourceLower >= TargetLower && SourceUpper <= TargetUpper;

        /// <summary>A literal that satisfies the source predicate (midpoint of source range).</summary>
        public int Literal => SourceLower + (SourceUpper - SourceLower) / 2;

        public override string ToString() =>
            $"Source >= {SourceLower} and <= {SourceUpper}, Target >= {TargetLower} and <= {TargetUpper} (expect {(ShouldAccept ? "accept" : "reject")})";
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<SimpleSubsetScenario> GenSimpleSubsetScenario()
    {
        return from sourceThreshold in Gen.Choose(0, 200)
               from targetThreshold in Gen.Choose(0, 200)
               select new SimpleSubsetScenario(sourceThreshold, targetThreshold);
    }

    private static Gen<CompoundSubsetScenario> GenCompoundSubsetScenario()
    {
        // Generate valid ranges where lower <= upper for both source and target
        return from sourceLower in Gen.Choose(0, 100)
               from sourceSpan in Gen.Choose(1, 50)
               let sourceUpper = sourceLower + sourceSpan
               from targetLower in Gen.Choose(0, 100)
               from targetSpan in Gen.Choose(1, 50)
               let targetUpper = targetLower + targetSpan
               select new CompoundSubsetScenario(sourceLower, sourceUpper, targetLower, targetUpper);
    }

    public static Arbitrary<SimpleSubsetScenario> ArbitrarySimpleSubsetScenario() =>
        GenSimpleSubsetScenario().ToArbitrary();

    public static Arbitrary<CompoundSubsetScenario> ArbitraryCompoundSubsetScenario() =>
        GenCompoundSubsetScenario().ToArbitrary();

    // ─── Property 5: Simple subset relationship ──────────────────────────

    /// <summary>
    /// Property 5: Subset relationship correctness (simple relational predicates).
    ///
    /// For <c>type Source = int >= S;</c> and <c>type Target = int >= T;</c>,
    /// assigning a Source-typed value to a Target-typed variable SHALL be accepted
    /// iff S >= T (source is more constrained).
    ///
    /// When S >= T: no MIST_TC_0099 diagnostic is emitted.
    /// When S &lt; T: exactly one MIST_TC_0099 diagnostic is emitted.
    ///
    /// **Validates: Requirements 9.1, 9.2**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RefinementSubsetPropertyTests) })]
    public bool SimpleSubset_AcceptedIffSourceMoreConstrained(SimpleSubsetScenario scenario)
    {
        var source = $$"""
            type Source = int >= {{scenario.SourceThreshold}};
            type Target = int >= {{scenario.TargetThreshold}};
            class Program {
                public static void Main() {
                    Source s = {{scenario.Literal}};
                    Target t = s;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var subsetErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0099")
            .ToList();

        if(scenario.ShouldAccept)
        {
            // Source is more constrained: no subset error expected
            return subsetErrors.Count == 0;
        }
        else
        {
            // Source is less constrained: exactly one subset error expected
            return subsetErrors.Count == 1;
        }
    }

    // ─── Property 5: Compound subset relationship ────────────────────────

    /// <summary>
    /// Property 5: Subset relationship correctness (compound predicates).
    ///
    /// For <c>type Source = int >= A and &lt;= B;</c> and
    /// <c>type Target = int >= C and &lt;= D;</c>, assigning a Source-typed value
    /// to a Target-typed variable SHALL be accepted iff A >= C and B &lt;= D
    /// (source range is contained within target range).
    ///
    /// When A >= C and B &lt;= D: no MIST_TC_0099 diagnostic is emitted.
    /// Otherwise: exactly one MIST_TC_0099 diagnostic is emitted.
    ///
    /// **Validates: Requirements 9.1, 9.5**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RefinementSubsetPropertyTests) })]
    public bool CompoundSubset_AcceptedIffSourceRangeContained(CompoundSubsetScenario scenario)
    {
        var source = $$"""
            type Source = int >= {{scenario.SourceLower}} and <= {{scenario.SourceUpper}};
            type Target = int >= {{scenario.TargetLower}} and <= {{scenario.TargetUpper}};
            class Program {
                public static void Main() {
                    Source s = {{scenario.Literal}};
                    Target t = s;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var subsetErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0099")
            .ToList();

        if(scenario.ShouldAccept)
        {
            // Source range is within target range: no subset error expected
            return subsetErrors.Count == 0;
        }
        else
        {
            // Source range is NOT within target range: exactly one subset error expected
            return subsetErrors.Count == 1;
        }
    }
}
// Feature: tuple-pattern-matching, Property 4: Arity mismatch diagnostic
// **Validates: Requirements 4.2, 8.2**

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
/// Property-based tests verifying:
///
/// Property 4: Arity mismatch diagnostic.
///
/// For any tuple type with arity N and any tuple positional pattern with M elements
/// where M ≠ N, the type checker SHALL emit exactly one diagnostic error whose message
/// includes both N (expected) and M (actual).
///
/// Uses the full compilation pipeline via a shared <see cref="CompilerDriver"/> instance
/// to comply with the test-process-spawning policy.
///
/// **Validates: Requirements 4.2, 8.2**
/// </summary>
public class TuplePositionalPatternArityMismatchPropertyTests
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
    /// Represents a scenario where a tuple positional pattern has a different
    /// number of elements than the scrutinee's tuple type arity.
    /// </summary>
#pragma warning disable CA1034
    public record ArityMismatchScenario(int TupleArity, int PatternArity)
    {
        public override string ToString()
        {
            return $"tuple arity={TupleArity}, pattern arity={PatternArity}";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates scenarios with tuple arity N (2-5) and pattern arity M (2-6)
    /// where M ≠ N. Both arities must be at least 2 (minimum for tuple positional patterns).
    /// </summary>
    public static Arbitrary<ArityMismatchScenario> ArbitraryArityMismatchScenario()
    {
        var gen =
            from tupleArity in Gen.Choose(2, 5)
            from patternArity in Gen.Choose(2, 6).Where(m => m != tupleArity)
            select new ArityMismatchScenario(tupleArity, patternArity);

        return gen.ToArbitrary();
    }

    // ─── Source generation helpers ───────────────────────────────────────

    /// <summary>
    /// Builds a Mist source program with a tuple of N int elements and a pattern
    /// with M discard elements (where M ≠ N).
    /// </summary>
    private static string BuildSource(ArityMismatchScenario scenario)
    {
        // Build tuple literal: (1, 2, 3, ..., N)
        var tupleLiteral = string.Join(", ", Enumerable.Range(1, scenario.TupleArity));

        // Build pattern with M discard elements: (_, _, ..., _)
        var patternElements = string.Join(", ", Enumerable.Repeat("_", scenario.PatternArity));

        return $$"""
            class Program {
                public static void Main() {
                    var t = ({{tupleLiteral}});
                    if (t is ({{patternElements}})) {
                    }
                }
            }
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Arity mismatch diagnostic
    //
    // **Validates: Requirements 4.2, 8.2**
    //
    // For any tuple type with arity N and any tuple positional pattern with M
    // elements where M ≠ N, the type checker SHALL emit exactly one diagnostic
    // error whose message includes both N (expected) and M (actual).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TuplePositionalPatternArityMismatchPropertyTests) })]
    public Property ArityMismatch_EmitsExactlyOneDiagnosticWithBothArities(
        ArityMismatchScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = CompileAndGetDiagnostics(source);

        // Filter to only the MIST_TC_0114 diagnostic (arity mismatch)
        var arityDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0114")
            .ToList();

        // Exactly one diagnostic should be emitted
        var hasExactlyOne = arityDiags.Count == 1;

        // The diagnostic message should include both the tuple arity (N) and pattern arity (M)
        var messageIncludesBothArities = hasExactlyOne &&
            arityDiags[0].Message.Contains(scenario.TupleArity.ToString(), StringComparison.Ordinal) &&
            arityDiags[0].Message.Contains(scenario.PatternArity.ToString(), StringComparison.Ordinal);

        return (hasExactlyOne && messageIncludesBothArities).ToProperty()
            .Label($"Expected exactly 1 MIST_TC_0114 diagnostic containing both '{scenario.TupleArity}' and '{scenario.PatternArity}'. " +
                   $"Got {arityDiags.Count} MIST_TC_0114 diagnostic(s). " +
                   $"Messages: [{string.Join("; ", arityDiags.Select(d => d.Message))}]. " +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
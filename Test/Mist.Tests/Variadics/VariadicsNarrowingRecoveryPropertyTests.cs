// Feature: variadics, task 16.6: property test for narrowing recovery (classic pipeline).
//
// Property 20: Narrowing recovers provable forwarding.
// **Validates: Requirements 6.7, 12.9**
//
// Inside an `args is T x` guard for a concrete tuple type T, the pack narrows to T so that
// spreading `x...` into a destination whose slots match T is well-typed (recovering the
// forwarding that Requirement 12.8 rejects for an unnarrowed symbolic pack). Different-shape
// branches enable their own forwarding independently, and a mismatched forward inside a branch
// is still MIST_VAR_0011.
//
// The property generates scenarios with:
//   * Matching forward: narrowed shape matches the destination (accepted, no MIST_VAR_0011).
//   * Mismatched forward: narrowed shape does not match the destination (MIST_VAR_0011).
//   * Multi-branch: two guards with different shapes, each forwarding into its own matching
//     destination (both accepted).
//
// All tests run entirely in-process via StdlibTestHelper.BuildModelWithStdlib +
// TypeCheckerAnalyzer.Analyze. ZERO external process spawns.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// The kind of narrowing scenario generated for Property 20.
/// </summary>
public enum NarrowingScenarioKind
{
    /// <summary>Narrowed shape matches destination: forward is accepted.</summary>
    MatchingForward,

    /// <summary>Narrowed shape does not match destination: MIST_VAR_0011.</summary>
    MismatchedForward,

    /// <summary>Two branches with different shapes, each forwarding into its own matching sink.</summary>
    MultiBranchMatching,
}

/// <summary>
/// A generated narrowing recovery scenario: a variadic method with an <c>args is T x</c> guard
/// that spreads the narrowed binding into a destination.
/// </summary>
public sealed record NarrowingRecoveryScenario(NarrowingScenarioKind Kind, int NarrowedArity, int DestinationArity)
{
    /// <summary>Whether the scenario should emit MIST_VAR_0011.</summary>
    public bool ExpectsRejection => Kind == NarrowingScenarioKind.MismatchedForward;

    public override string ToString()
    {
        return Kind switch
        {
            NarrowingScenarioKind.MatchingForward =>
                $"match(narrowed={NarrowedArity}, dest={DestinationArity}) -> accepted",
            NarrowingScenarioKind.MismatchedForward =>
                $"mismatch(narrowed={NarrowedArity}, dest={DestinationArity}) -> MIST_VAR_0011",
            NarrowingScenarioKind.MultiBranchMatching =>
                $"multi-branch(branch1={NarrowedArity}, branch2={DestinationArity}) -> accepted",
            _ => Kind.ToString(),
        };
    }
}

/// <summary>
/// Property test for <b>Property 20 (Narrowing recovers provable forwarding)</b> over the
/// classic pipeline. Generates <c>args is T x</c> patterns and asserts:
/// <list type="bullet">
///   <item>Matching forward: spreading <c>x...</c> into a destination matching T is accepted.</item>
///   <item>Mismatched forward: spreading <c>x...</c> into a wrong-shape destination emits MIST_VAR_0011.</item>
///   <item>Multi-branch: different shapes in different branches each enable their own forwarding.</item>
/// </list>
/// <b>Validates: Requirements 6.7, 12.9.</b>
/// </summary>
public sealed class VariadicsNarrowingRecoveryPropertyTests
{
    private const string SpreadDestinationMismatchId = "MIST_VAR_0011";

    // ═══════════════════════════════════════════════════════════════════════
    // Property 20: narrowing recovers forwarding for matching shapes, rejects mismatched
    // shapes, and different branches enable independent forwarding.
    // **Validates: Requirements 6.7, 12.9**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsNarrowingRecoveryPropertyTests) }, MaxTest = 150)]
    public bool NarrowingRecovery_ForwardAcceptedIffShapeMatches(NarrowingRecoveryScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = AnalyzeClassic(source);

        var mismatchCount = diagnostics.Count(d => d.Id.Value == SpreadDestinationMismatchId);

        if(scenario.ExpectsRejection)
        {
            // Mismatched forward inside a branch: exactly one MIST_VAR_0011.
            Assert.Equal(1, mismatchCount);
        }
        else
        {
            // Matching forward or multi-branch matching: no MIST_VAR_0011.
            Assert.Equal(0, mismatchCount);
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<NarrowingRecoveryScenario> NarrowingRecoveryScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<NarrowingRecoveryScenario> GenScenario()
    {
        return Gen.OneOf(
            // Matching forward: narrowed arity == destination arity (2..5).
            from arity in Gen.Choose(2, 5)
            select new NarrowingRecoveryScenario(NarrowingScenarioKind.MatchingForward, arity, arity),
            // Mismatched forward: narrowed arity != destination arity.
            from narrowed in Gen.Choose(2, 5)
            from destOffset in Gen.Choose(1, 3)
            let dest = narrowed + destOffset
            select new NarrowingRecoveryScenario(NarrowingScenarioKind.MismatchedForward, narrowed, dest),
            // Multi-branch matching: two different shapes, each forwarding into its own sink.
            from arity1 in Gen.Choose(2, 4)
            from arity2 in Gen.Choose(2, 4).Where(a2 => a2 != arity1)
            select new NarrowingRecoveryScenario(NarrowingScenarioKind.MultiBranchMatching, arity1, arity2));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Source generation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a complete Mist source with a variadic method containing an <c>args is T x</c>
    /// guard that spreads the narrowed binding into a destination.
    /// </summary>
    private static string BuildSource(NarrowingRecoveryScenario scenario)
    {
        return scenario.Kind switch
        {
            NarrowingScenarioKind.MatchingForward => BuildMatchingSource(scenario.NarrowedArity),
            NarrowingScenarioKind.MismatchedForward => BuildMismatchedSource(scenario.NarrowedArity, scenario.DestinationArity),
            NarrowingScenarioKind.MultiBranchMatching => BuildMultiBranchSource(scenario.NarrowedArity, scenario.DestinationArity),
            _ => throw new InvalidOperationException(),
        };
    }

    /// <summary>
    /// Builds source where the narrowed shape matches the destination (accepted).
    /// </summary>
    private static string BuildMatchingSource(int arity)
    {
        var tupleTypes = string.Join(", ", Enumerable.Repeat("int", arity));
        var sinkParams = string.Join(", ", Enumerable.Range(0, arity).Select(i => $"int p{i}"));

        return $$"""
            public class Handler<params TArgs>
            {
                public void Process(params TArgs args)
                {
                    if (args is ({{tupleTypes}}) narrowed)
                    {
                        this.Sink(narrowed...);
                    }
                }

                public void Sink({{sinkParams}}) { }
            }
            """;
    }

    /// <summary>
    /// Builds source where the narrowed shape does not match the destination (MIST_VAR_0011).
    /// </summary>
    private static string BuildMismatchedSource(int narrowedArity, int destArity)
    {
        var tupleTypes = string.Join(", ", Enumerable.Repeat("int", narrowedArity));
        var sinkParams = string.Join(", ", Enumerable.Range(0, destArity).Select(i => $"int p{i}"));

        return $$"""
            public class Handler<params TArgs>
            {
                public void Process(params TArgs args)
                {
                    if (args is ({{tupleTypes}}) narrowed)
                    {
                        this.Sink(narrowed...);
                    }
                }

                public void Sink({{sinkParams}}) { }
            }
            """;
    }

    /// <summary>
    /// Builds source with two branches of different shapes, each forwarding into its own
    /// matching sink (both accepted).
    /// </summary>
    private static string BuildMultiBranchSource(int arity1, int arity2)
    {
        var tupleTypes1 = string.Join(", ", Enumerable.Repeat("int", arity1));
        var tupleTypes2 = string.Join(", ", Enumerable.Repeat("int", arity2));
        var sinkParams1 = string.Join(", ", Enumerable.Range(0, arity1).Select(i => $"int p{i}"));
        var sinkParams2 = string.Join(", ", Enumerable.Range(0, arity2).Select(i => $"int q{i}"));

        return $$"""
            public class Router<params TArgs>
            {
                public void Route(params TArgs args)
                {
                    if (args is ({{tupleTypes1}}) first)
                    {
                        this.Sink1(first...);
                    }
                    else if (args is ({{tupleTypes2}}) second)
                    {
                        this.Sink2(second...);
                    }
                }

                public void Sink1({{sinkParams1}}) { }
                public void Sink2({{sinkParams2}}) { }
            }
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Classic-pipeline harness (in-process; no backend; ZERO external process spawns)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, builds a classic semantic model with the
    /// stdlib, runs TypeCheckerAnalyzer against it, and returns the analyzer diagnostics.
    /// No backend runs and no external process is spawned.
    /// </summary>
    private static ImmutableArray<Diagnostic> AnalyzeClassic(string source)
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText("test.mist", source);
        var tokens = lexer.Lex(sourceText, parseDiagnostics);
        var parseResult = parser.Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);

        var parseErrors = parseDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            parseErrors.Count == 0,
            $"Unexpected parse errors: {string.Join("; ", parseErrors.Select(d => $"{d.Id.Value} {d.Message}"))}");

        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(parseResult.Root!, source: source);
        var analyzerDiagnostics = new DiagnosticBag();
        new TypeCheckerAnalyzer().Analyze(model, analyzerDiagnostics);

        return analyzerDiagnostics.ToImmutableArray();
    }
}
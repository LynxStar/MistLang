// Feature: variadics, task 16.5: property test for generic destination enforcement
// (classic pipeline).
//
// Property 19: Spread respects destination typing (generic enforcement).
// **Validates: Requirements 8.4, 12.6**
//
// A symbolic value pack `args...` is accepted iff the destination provably accepts `(TArgs...)`
// for every `TArgs` — another `params TArgs` or `(TArgs...)` parameter, or a `Func<TArgs..., R>`
// value — and is rejected with MIST_VAR_0011 for a fixed-arity destination (e.g.,
// `Sum3(int, int, int)`) even though it would succeed for one specific instantiation.
//
// The property generates scenarios where a variadic method forwards `args...` into one of
// several destination categories:
//   * Provably compatible: `params TArgs`, `(TArgs...)` tuple parameter (accepted).
//   * Fixed-arity: concrete parameters of varying count (rejected with MIST_VAR_0011).
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
/// The kind of destination a generated scenario forwards `args...` into.
/// </summary>
public enum DestinationKind
{
    /// <summary>Destination declares <c>params TArgs</c> (provably compatible).</summary>
    ParamsTArgs,

    /// <summary>Destination declares <c>(TArgs...) tuple</c> (provably compatible).</summary>
    TupleTArgs,

    /// <summary>Destination has a fixed-arity signature (not provably compatible).</summary>
    FixedArity,
}

/// <summary>
/// A generated generic-destination enforcement scenario: a variadic method that forwards
/// <c>args...</c> into a destination of a given kind.
/// </summary>
public sealed record GenericDestinationScenario(DestinationKind Kind, int FixedArityCount)
{
    /// <summary>Whether the scenario should emit MIST_VAR_0011.</summary>
    public bool ExpectsRejection => Kind == DestinationKind.FixedArity;

    public override string ToString()
    {
        return Kind switch
        {
            DestinationKind.FixedArity => $"fixed-arity({FixedArityCount}) -> MIST_VAR_0011",
            _ => $"{Kind} -> accepted",
        };
    }
}

/// <summary>
/// Property test for <b>Property 19 (Spread respects destination typing)</b> over the classic
/// pipeline. Generates variadic methods that forward <c>args...</c> into provably-compatible or
/// fixed-arity destinations and asserts:
/// <list type="bullet">
///   <item>Accepted (no MIST_VAR_0011) for <c>params TArgs</c> or <c>(TArgs...)</c> destinations.</item>
///   <item>Rejected (exactly one MIST_VAR_0011) for fixed-arity destinations.</item>
/// </list>
/// <b>Validates: Requirements 8.4, 12.6.</b>
/// </summary>
public sealed class VariadicsGenericDestinationEnforcementPropertyTests
{
    private const string SpreadDestinationMismatchId = "MIST_VAR_0011";

    // ═══════════════════════════════════════════════════════════════════════
    // Property 19: accepted IFF destination provably accepts (TArgs...) for every TArgs,
    // rejected with MIST_VAR_0011 for a fixed-arity destination.
    // **Validates: Requirements 8.4, 12.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsGenericDestinationEnforcementPropertyTests) }, MaxTest = 150)]
    public bool GenericDestination_AcceptedIffProvablyCompatible(GenericDestinationScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = AnalyzeClassic(source);

        var mismatchCount = diagnostics.Count(d => d.Id.Value == SpreadDestinationMismatchId);

        if(scenario.ExpectsRejection)
        {
            // Fixed-arity destination: exactly one MIST_VAR_0011.
            Assert.Equal(1, mismatchCount);
        }
        else
        {
            // Provably compatible destination: no MIST_VAR_0011.
            Assert.Equal(0, mismatchCount);
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<GenericDestinationScenario> GenericDestinationScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<GenericDestinationScenario> GenScenario()
    {
        return Gen.OneOf(
            Gen.Constant(new GenericDestinationScenario(DestinationKind.ParamsTArgs, 0)),
            Gen.Constant(new GenericDestinationScenario(DestinationKind.TupleTArgs, 0)),
            from count in Gen.Choose(1, 5)
            select new GenericDestinationScenario(DestinationKind.FixedArity, count));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Source generation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a complete Mist source with a variadic method that forwards <c>args...</c>
    /// into the specified destination kind.
    /// </summary>
    private static string BuildSource(GenericDestinationScenario scenario)
    {
        var sinkDecl = scenario.Kind switch
        {
            DestinationKind.ParamsTArgs => "public void Sink(params TArgs other) { }",
            DestinationKind.TupleTArgs => "public void Sink((TArgs...) whole) { }",
            DestinationKind.FixedArity => BuildFixedAritySink(scenario.FixedArityCount),
            _ => throw new InvalidOperationException(),
        };

        return $$"""
            public class Relay<params TArgs>
            {
                public void Source(params TArgs args)
                {
                    this.Sink(args...);
                }

                {{sinkDecl}}
            }
            """;
    }

    /// <summary>
    /// Builds a fixed-arity sink method declaration with the specified number of int parameters.
    /// </summary>
    private static string BuildFixedAritySink(int count)
    {
        var parameters = string.Join(", ", Enumerable.Range(0, count).Select(i => $"int p{i}"));
        return $"public void Sink({parameters}) {{ }}";
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
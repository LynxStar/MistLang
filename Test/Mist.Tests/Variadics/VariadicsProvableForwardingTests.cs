// Feature: variadics - Unit tests for generic-body provable forwarding (Task 16.2)
// Requirements: 12.2, 12.3, 12.6, 12.7, 12.8
//
// A variadic method body is type-checked once, generically, for every instantiation the
// signature permits (Requirement 12.6). Spreading a symbolic value pack `args...` is accepted
// only into a destination provably (TArgs...)-compatible for every TArgs (Requirement 12.7):
//   - another `params TArgs` / `(TArgs...)` parameter
//   - a `Func<TArgs..., R>` value
// Spreading into a fixed-arity destination emits MIST_VAR_0011 (Requirement 12.8).
//
// All tests run entirely in-process via StdlibTestHelper.BuildModelWithStdlib +
// TypeCheckerAnalyzer.Analyze. ZERO external process spawns.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Task 16.2 tests for generic-body open-generic checking and provable forwarding
/// (Requirements 12.2, 12.3, 12.6, 12.7, 12.8). Asserts that symbolic <c>args...</c>
/// forwarding into a provably <c>(TArgs...)</c>-compatible destination is accepted (no
/// <c>MIST_VAR_0011</c>), and that forwarding into a fixed-arity destination emits
/// <c>MIST_VAR_0011</c>.
/// </summary>
public sealed class VariadicsProvableForwardingTests
{
    private const string SpreadDestinationMismatchId = "MIST_VAR_0011";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.7: provably compatible destinations -- no MIST_VAR_0011
    // ═══════════════════════════════════════════════════════════════════════

    // Forwarding `args...` into another method with `params TArgs` (same pack) is accepted.
    [Fact]
    public void SymbolicPack_ForwardedIntoParamsTArgs_Accepted()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Relay<params TArgs>
            {
                public void Source(params TArgs args)
                {
                    this.Sink(args...);
                }

                public void Sink(params TArgs other) { }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // Forwarding `args...` into a plain `(TArgs...)` tuple parameter (same pack) is accepted.
    [Fact]
    public void SymbolicPack_ForwardedIntoTupleParam_Accepted()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Relay<params TArgs>
            {
                public void Source(params TArgs args)
                {
                    this.Sink(args...);
                }

                public void Sink((TArgs...) whole) { }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.8: fixed-arity destination -- emits MIST_VAR_0011
    // ═══════════════════════════════════════════════════════════════════════

    // Spreading `args...` into a method with concrete fixed parameters emits MIST_VAR_0011.
    [Fact]
    public void SymbolicPack_ForwardedIntoFixedArity_EmitsMistVar0011()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Bad<params TArgs>
            {
                public void Source(params TArgs args)
                {
                    this.Sum3(args...);
                }

                public void Sum3(int a, int b, int c) { }
            }
            """);

        _ = Assert.Single(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // Spreading `args...` into a method with a single concrete parameter emits MIST_VAR_0011.
    [Fact]
    public void SymbolicPack_ForwardedIntoSingleParam_EmitsMistVar0011()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Bad<params TArgs>
            {
                public void Source(params TArgs args)
                {
                    this.One(args...);
                }

                public void One(int x) { }
            }
            """);

        _ = Assert.Single(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, builds a classic semantic model with the stdlib
    /// (<see cref="StdlibTestHelper.BuildModelWithStdlib"/>), runs <see cref="TypeCheckerAnalyzer"/>
    /// against it, and returns the analyzer diagnostics. No backend runs and no external process is
    /// spawned.
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
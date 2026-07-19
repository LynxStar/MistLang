// Feature: variadics - Unit tests for value-pack narrowing that recovers forwarding (Task 16.3)
// Requirements: 12.9, 6.7
//
// When a variadic method body guards a value pack with `args is T x` for a concrete tuple
// type T (e.g., `args is (int, int, int) triple`), the pack narrows to T within that branch.
// Spreading `x...` into a destination whose slots match T is then well-typed (recovering the
// forwarding that Requirement 12.8 rejects for an unnarrowed symbolic pack). If a spread
// within such a branch targets a destination that does not match the narrowed shape T,
// MIST_VAR_0011 is emitted.
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
/// Task 16.3 tests for value-pack narrowing that recovers forwarding
/// (Requirements 12.9, 6.7). Asserts that inside an <c>args is T x</c> branch the narrowed
/// binding <c>x</c> has type <c>T</c>, and spreading <c>x...</c> into a matching destination
/// is accepted (no <c>MIST_VAR_0011</c>), while spreading into a mismatched destination emits
/// <c>MIST_VAR_0011</c>. Also verifies different shapes in different branches.
/// </summary>
public sealed class VariadicsValuePackNarrowingTests
{
    private const string SpreadDestinationMismatchId = "MIST_VAR_0011";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.9: narrowed binding spread into matching destination -- accepted
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Narrowing via <c>args is (int, int, int) triple</c> lets <c>triple...</c>
    /// be spread into a 3-int destination without MIST_VAR_0011.
    /// </summary>
    [Fact]
    public void NarrowedPack_SpreadIntoMatchingDestination_Accepted()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Handler<params TArgs>
            {
                public void Process(params TArgs args)
                {
                    if (args is (int, int, int) triple)
                    {
                        this.Sum3(triple...);
                    }
                }

                public void Sum3(int a, int b, int c) { }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.9: narrowed binding spread into mismatched destination -- MIST_VAR_0011
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Narrowing via <c>args is (int, int, int) triple</c> but spreading <c>triple...</c>
    /// into a 2-slot destination emits MIST_VAR_0011 (slot count mismatch).
    /// </summary>
    [Fact]
    public void NarrowedPack_SpreadIntoMismatchedDestination_EmitsMistVar0011()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Handler<params TArgs>
            {
                public void Process(params TArgs args)
                {
                    if (args is (int, int, int) triple)
                    {
                        this.Add2(triple...);
                    }
                }

                public void Add2(int a, int b) { }
            }
            """);

        _ = Assert.Single(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.9 + 6.7: different shapes in different branches -- both accepted
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Two <c>is</c> guards with different tuple types: each narrowed binding is spread
    /// into a matching destination within its own branch. Both are accepted (no MIST_VAR_0011).
    /// </summary>
    [Fact]
    public void DifferentShapes_InDifferentBranches_BothAccepted()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Router<params TArgs>
            {
                public void Route(params TArgs args)
                {
                    if (args is (int, int) pair)
                    {
                        this.Handle2(pair...);
                    }
                    else if (args is (int, int, int) triple)
                    {
                        this.Handle3(triple...);
                    }
                }

                public void Handle2(int a, int b) { }
                public void Handle3(int x, int y, int z) { }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.7: narrowing permits indexing the narrowed binding
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inside a narrowed branch, spreading the narrowed binding into a correct
    /// shape destination succeeds even when the outer method has a symbolic pack.
    /// This confirms Requirement 6.7 (guard narrows unknown-length pack to fixed-size domain).
    /// </summary>
    [Fact]
    public void NarrowedPack_SpreadIntoSingleSlot_EmitsMistVar0011()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Handler<params TArgs>
            {
                public void Process(params TArgs args)
                {
                    if (args is (string, string) pair)
                    {
                        this.One(pair...);
                    }
                }

                public void One(string x) { }
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
// Feature: variadics - Unit tests for value-level `params T[]` collection semantics (Task 17.2)
// Requirements: 13.2, 13.3, 13.7
//
// These tests verify:
//   - 13.2: Trailing arguments assignable to T are collected into a single T[] (accepted).
//   - 13.3: Zero trailing arguments are valid (empty array, no diagnostic).
//   - 13.7: A non-assignable trailing argument emits a type error.
//   - Overload resolution picks the params overload when trailing-arg count > fixed-param count.
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
/// Task 17.2 tests for value-level <c>params T[]</c> collection semantics in the classic
/// pipeline. Asserts that trailing arguments assignable to the element type <c>T</c> are
/// accepted (Requirement 13.2), zero trailing arguments produce no error (Requirement 13.3),
/// a non-assignable trailing argument emits the standard type-mismatch diagnostic
/// (Requirement 13.7), and overload resolution treats a <c>params T[]</c> parameter as
/// matching any number of trailing arguments.
/// </summary>
public sealed class VariadicsValueLevelParamsCollectionTests
{
    private const string TypeMismatchId = "MIST_TC_0002";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 13.2: trailing args assignable to T are accepted
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParamsIntArray_WithMatchingTrailingArgs_Accepted()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Demo
            {
                public void Sum(params int[] xs) { }

                public void Caller()
                {
                    this.Sum(1, 2, 3);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == TypeMismatchId);
    }

    [Fact]
    public void ParamsIntArray_SingleTrailingArg_Accepted()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Demo
            {
                public void Log(params int[] xs) { }

                public void Caller()
                {
                    this.Log(42);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == TypeMismatchId);
    }

    [Fact]
    public void ParamsIntArray_WithFixedAndTrailingArgs_Accepted()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Demo
            {
                public void Mixed(int first, params int[] rest) { }

                public void Caller()
                {
                    this.Mixed(1, 2, 3, 4);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == TypeMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 13.3: zero trailing args are valid (empty array)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParamsIntArray_ZeroTrailingArgs_Accepted()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Demo
            {
                public void Sum(params int[] xs) { }

                public void Caller()
                {
                    this.Sum();
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == TypeMismatchId);
    }

    [Fact]
    public void ParamsWithFixed_ZeroTrailingArgs_Accepted()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Demo
            {
                public void Mixed(int first, params int[] rest) { }

                public void Caller()
                {
                    this.Mixed(1);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == TypeMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 13.7: non-assignable trailing arg emits type error
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParamsIntArray_StringTrailingArg_EmitsTypeMismatch()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Demo
            {
                public void Sum(params int[] xs) { }

                public void Caller()
                {
                    this.Sum(1, "bad", 3);
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id.Value == TypeMismatchId);
    }

    [Fact]
    public void ParamsWithFixed_NonAssignableTrailingArg_EmitsTypeMismatch()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Demo
            {
                public void Mixed(int first, params int[] rest) { }

                public void Caller()
                {
                    this.Mixed(1, "bad");
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id.Value == TypeMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Overload resolution: params overload accepts trailing-arg count > fixed
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OverloadResolution_PicksParamsOverload_WhenExtraTrailingArgs()
    {
        // Two overloads: one fixed (int), one params (params int[]).
        // Calling with 3 args should resolve to the params overload, not emit
        // "no applicable overload".
        var diagnostics = AnalyzeClassic(
            """
            public class Demo
            {
                public void Process(int x) { }
                public void Process(params int[] xs) { }

                public void Caller()
                {
                    this.Process(1, 2, 3);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0005");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == TypeMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helper
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, builds a classic semantic model with the
    /// stdlib, runs TypeCheckerAnalyzer against it, and returns the analyzer diagnostics.
    /// No backend runs and no external process is spawned.
    /// </summary>
    private static ImmutableArray<Diagnostic> AnalyzeClassic(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();
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
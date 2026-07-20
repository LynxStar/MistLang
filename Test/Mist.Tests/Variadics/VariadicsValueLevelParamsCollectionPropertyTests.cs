// Feature: variadics, task 17.6: property test for value-level params collection (classic
// pipeline).
//
// Property 14: Value-level params collection correctness.
// **Validates: Requirements 13.2, 13.3, 13.7**
//
// Asserts:
//   - A `params T[]` parameter collects trailing args assignable to T (accepted) (Req 13.2).
//   - Zero trailing arguments is accepted (empty array, no diagnostic) (Req 13.3).
//   - A non-assignable trailing argument is rejected with a type-mismatch error (Req 13.7).
//
// Drives end-to-end via StdlibTestHelper.BuildModelWithStdlib + TypeCheckerAnalyzer.Analyze.
// All tests run entirely in-process with ZERO external process spawns.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// The outcome expected from a generated value-level params scenario.
/// </summary>
public enum ParamsCollectionOutcome
{
    /// <summary>All trailing args are assignable to T; no diagnostic expected.</summary>
    Accepted,

    /// <summary>At least one trailing arg is not assignable to T; MIST_TC_0002 expected.</summary>
    Rejected,
}

/// <summary>
/// A generated scenario for testing value-level <c>params T[]</c> collection semantics.
/// Each scenario specifies the element type, the number of fixed leading parameters, and
/// the trailing argument types (which may or may not be assignable to the element type).
/// </summary>
public sealed record ValueLevelParamsScenario(
    string ElementType,
    int FixedLeadingCount,
    IReadOnlyList<string> TrailingArgTypes,
    ParamsCollectionOutcome ExpectedOutcome)
{
    public override string ToString()
    {
        var trailing = TrailingArgTypes.Count == 0 ? "none" : string.Join(", ", TrailingArgTypes);
        return $"params {ElementType}[] with {FixedLeadingCount} fixed, trailing=[{trailing}], expect={ExpectedOutcome}";
    }
}

/// <summary>
/// Boundary tests for <b>Property 14 (Value-level params collection correctness)</b> over the
/// classic pipeline. Exercises invocations of methods with <c>params T[]</c> parameters across
/// a curated set of <see cref="TheoryAttribute"/> cases -- both element types, zero/one/several
/// fixed leading parameters, zero-to-several trailing arguments, and a non-assignable trailing
/// argument at the first, middle, and last position in both mismatch directions -- rather than
/// sampling the same small domain 150 times. The general assignability diagnostic
/// (MIST_TC_0002) is itself covered broadly elsewhere.
/// <b>Validates: Requirements 13.2, 13.3, 13.7.</b>
/// </summary>
public sealed class VariadicsValueLevelParamsCollectionPropertyTests
{
    private const string TypeMismatchId = "MIST_TC_0002";

    // ═══════════════════════════════════════════════════════════════════════
    // Property 14: trailing args assignable to T -> accepted; non-assignable -> rejected.
    // **Validates: Requirements 13.2, 13.3, 13.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    // Accepted: all trailing args assignable to the element type (Req 13.2).
    [InlineData("int", 0, "int", false)]
    [InlineData("int", 2, "int,int,int,int", false)]
    [InlineData("string", 1, "string,string", false)]
    // Accepted: zero trailing arguments -> empty collection (Req 13.3).
    [InlineData("int", 0, "", false)]
    [InlineData("string", 2, "", false)]
    // Rejected: a non-assignable trailing arg at the first / middle / last position,
    // in both mismatch directions (Req 13.7).
    [InlineData("int", 0, "string", true)]              // bad at only position
    [InlineData("int", 1, "int,int,string", true)]      // bad at last position
    [InlineData("string", 0, "int,string,string", true)] // bad at first position
    [InlineData("int", 0, "int,string,int", true)]      // bad in the middle
    [InlineData("string", 2, "string,int", true)]       // bad at last, with fixed leading
    public void ValueLevelParamsCollection_AcceptedOrRejectedPerAssignability(
        string elementType, int fixedLeadingCount, string trailingCsv, bool expectRejected)
    {
        var trailing = trailingCsv.Length == 0
            ? Array.Empty<string>()
            : trailingCsv.Split(',');
        var expectedOutcome = expectRejected
            ? ParamsCollectionOutcome.Rejected
            : ParamsCollectionOutcome.Accepted;
        var scenario = new ValueLevelParamsScenario(
            elementType, fixedLeadingCount, trailing, expectedOutcome);

        var source = BuildSource(scenario);
        var diagnostics = AnalyzeClassic(source);

        var hasTypeMismatch = diagnostics.Any(d => d.Id.Value == TypeMismatchId);

        if(scenario.ExpectedOutcome == ParamsCollectionOutcome.Accepted)
        {
            Assert.False(hasTypeMismatch,
                $"Expected no type mismatch but got one. Source:\n{source}\n" +
                $"Diagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
        }
        else
        {
            Assert.True(hasTypeMismatch,
                $"Expected type mismatch but got none. Source:\n{source}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Source builder
    // ═══════════════════════════════════════════════════════════════════════

    private static string BuildSource(ValueLevelParamsScenario scenario)
    {
        // Build the method signature: fixed leading params of the element type + params T[] rest
        var fixedParams = new List<string>();
        for(var i = 0; i < scenario.FixedLeadingCount; i++)
        {
            fixedParams.Add($"{scenario.ElementType} p{i}");
        }

        fixedParams.Add($"params {scenario.ElementType}[] rest");
        var paramList = string.Join(", ", fixedParams);

        // Build the argument list: fixed leading args (same type, always valid) + trailing args
        var args = new List<string>();
        for(var i = 0; i < scenario.FixedLeadingCount; i++)
        {
            args.Add(LiteralForType(scenario.ElementType));
        }

        foreach(var trailingType in scenario.TrailingArgTypes)
        {
            args.Add(LiteralForType(trailingType));
        }

        var argList = string.Join(", ", args);

        return $$"""
            public class Demo
            {
                public void Target({{paramList}}) { }

                public void Caller()
                {
                    this.Target({{argList}});
                }
            }
            """;
    }

    /// <summary>
    /// Returns a literal expression for a given type name.
    /// </summary>
    private static string LiteralForType(string typeName)
    {
        return typeName switch
        {
            "int" => "42",
            "string" => "\"hello\"",
            "bool" => "true",
            "double" => "3.14",
            "long" => "100",
            "char" => "'x'",
            _ => "0",
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Analysis helper
    // ═══════════════════════════════════════════════════════════════════════

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
// Feature: tuple-pattern-matching, Property 8: Per-element independent diagnostics
// **Validates: Requirements 8.3, 8.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for per-element independent diagnostics in tuple positional patterns.
///
/// Property 8: For any tuple positional pattern with K elements (K ≤ tuple arity) where
/// J of those elements have type-incompatible subpatterns (J ≥ 1), the type checker SHALL
/// emit at least J separate diagnostics — one for each failing element — rather than
/// stopping at the first error.
///
/// **Validates: Requirements 8.3, 8.4**
/// </summary>
public class TuplePositionalPatternPerElementDiagnosticPropertyTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Scenario ────────────────────────────────────────────────────────

    /// <summary>
    /// Represents a test scenario: a tuple of N int elements with J positions
    /// having type-incompatible string constant subpatterns.
    /// </summary>
#pragma warning disable CA1034
    public sealed record PerElementDiagnosticScenario(
        int Arity,
        IReadOnlyList<bool> IsIncompatible,
        string Label)
    {
        /// <summary>Number of elements with type-incompatible subpatterns.</summary>
        public int IncompatibleCount => IsIncompatible.Count(x => x);

        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    public static Arbitrary<PerElementDiagnosticScenario> ArbitraryPerElementDiagnosticScenario()
    {
        var gen =
            from arity in Gen.Choose(2, 5)
            from flags in GenAtLeastOneTrue(arity)
            let label = $"arity={arity}, incompatible=[{string.Join(",", flags.Select(f => f ? "X" : "."))}]"
            select new PerElementDiagnosticScenario(arity, flags, label);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a bool array of the given length with at least one true value.
    /// Each position is independently true/false, but we ensure at least one is true.
    /// </summary>
    private static Gen<bool[]> GenAtLeastOneTrue(int length)
    {
        // If no true values, force the first element to be true
        return from flags in Gen.ArrayOf(Gen.Elements(true, false), length)
               let adjusted = flags.Any(f => f) ? flags : SetFirst(flags)
               select adjusted;
    }

    private static bool[] SetFirst(bool[] flags)
    {
        var copy = (bool[])flags.Clone();
        copy[0] = true;
        return copy;
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var tcBag = new DiagnosticBag();
        Analyzer.Analyze(model, tcBag);

        return semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
    }

    // ─── Source generation helpers ───────────────────────────────────────

    /// <summary>
    /// Builds a Mist source program with a tuple of int elements and a pattern
    /// that uses string constants at the incompatible positions.
    /// </summary>
    private static string BuildSource(PerElementDiagnosticScenario scenario)
    {
        // Build tuple literal: (1, 2, 3, ...)
        var tupleLiteral = string.Join(", ", Enumerable.Range(1, scenario.Arity).Select(i => i.ToString()));

        // Build pattern elements: compatible positions get an int constant,
        // incompatible positions get a string constant (type mismatch with int).
        var patternElements = new List<string>();
        for(var i = 0; i < scenario.Arity; i++)
        {
            patternElements.Add(scenario.IsIncompatible[i] ? $"\"bad{i}\"" : $"{i + 1}");
        }

        var patternLiteral = string.Join(", ", patternElements);

        return $$"""
            class Program {
                public static void Main() {
                    var t = ({{tupleLiteral}});
                    var r = t is ({{patternLiteral}});
                }
            }
            """;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 8: Per-element independent diagnostics
    //
    // For any tuple positional pattern with K elements where J of those elements
    // have type-incompatible subpatterns (J ≥ 1), the type checker SHALL emit at
    // least J separate diagnostics — one for each failing element — rather than
    // stopping at the first error.
    //
    // **Validates: Requirements 8.3, 8.4**
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TuplePositionalPatternPerElementDiagnosticPropertyTests) })]
    public Property PerElementDiagnostics_EmitsAtLeastJDiagnosticsForJIncompatibleElements(
        PerElementDiagnosticScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = CompileAndGetDiagnostics(source);

        // Filter to MIST_TC_0051 (PatternConstantTypeMismatch) diagnostics,
        // which is what the type checker emits for each incompatible constant.
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0051")
            .ToList();

        var j = scenario.IncompatibleCount;

        return (typeMismatchDiags.Count >= j)
            .ToProperty()
            .Label($"Expected at least {j} MIST_TC_0051 diagnostics but got {typeMismatchDiags.Count}. " +
                   $"Scenario: {scenario}. " +
                   $"All diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
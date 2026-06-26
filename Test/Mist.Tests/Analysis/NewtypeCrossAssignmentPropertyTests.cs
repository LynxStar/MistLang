// Feature: newtypes, Property 4: Cross-newtype non-assignability
//
// **Validates: Requirements 2.5**
//
// For any two distinct newtypes that share the same source type, assigning a
// value of one newtype to a variable of the other SHALL produce a type mismatch
// diagnostic error.
//
// Uses the batched pattern: generate all N scenarios upfront, build a single
// Mist program containing all scenarios, compile once via the shared
// CompilerDriver (in-process), and check diagnostics per-scenario.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that cross-newtype assignments (between two
/// newtypes sharing the same source type) produce type mismatch diagnostics.
///
/// Property 4: For any two distinct newtypes that share the same source type,
/// assigning a value of one newtype to a variable of the other SHALL produce
/// a type mismatch diagnostic error.
///
/// **Validates: Requirements 2.5**
/// </summary>
public sealed class NewtypeCrossAssignmentPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Shared infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    /// <summary>
    /// Compiles Mist source in-process (with stdlib loaded for primitive type
    /// resolution) and returns all diagnostics (parse + semantic model + type checker).
    /// </summary>
    private static ImmutableArray<Diagnostic> Compile(string source)
    {
        var (stdlibUnits, stdlibAsts) = StdlibTestHelper.LoadStdlib();

        var userSourceText = new SourceText("test.mist", source);
        var userUnit = new CompilationUnit(userSourceText);

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(userSourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return lexDiag.ToImmutableArray().AddRange(parseDiag.ToImmutableArray());
        }

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(parseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            allAsts,
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        return lexDiag.ToImmutableArray()
            .AddRange(parseDiag.ToImmutableArray())
            .AddRange(buildDiag.ToImmutableArray())
            .AddRange(tcDiag.ToImmutableArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario model
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    /// <summary>
    /// Represents a batched scenario: a complete Mist program containing
    /// multiple pairs of newtypes sharing the same source type, where each
    /// pair has a cross-assignment that should be rejected.
    /// </summary>
    public sealed record CrossAssignmentBatchScenario(
        string Source,
        int ScenarioCount,
        string Description)
    {
        public override string ToString() => Description;
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

    private static readonly string[] NewtypeNamePrefixes =
        ["UserId", "OrderId", "AccountId", "ProductId", "SessionId",
         "TokenId", "RequestId", "EventId", "TaskId", "BatchId"];

    /// <summary>
    /// Generates a batched scenario with N pairs of newtypes sharing the same
    /// source type, each with a cross-assignment that should produce a type
    /// mismatch diagnostic.
    /// </summary>
    public static Arbitrary<CrossAssignmentBatchScenario> ArbitraryCrossAssignmentBatchScenario()
    {
        var gen =
            from scenarioCount in Gen.Choose(3, 8)
            from scenarios in Gen.ArrayOf(GenSingleScenario(), scenarioCount)
            let indexedScenarios = scenarios.Select((s, i) => (s, i)).ToList()
            let newtypeDecls = string.Join("\n", indexedScenarios.Select(x =>
                GenerateNewtypeDecls(x.i, x.s)))
            let crossAssignments = string.Join("\n", indexedScenarios.Select(x =>
                GenerateCrossAssignment(x.i, x.s)))
            let source = $@"{newtypeDecls}

class Program {{
    public static void Main() {{
{crossAssignments}
    }}
}}"
            select new CrossAssignmentBatchScenario(
                source,
                scenarioCount,
                $"Batch of {scenarioCount} cross-newtype assignment scenarios");

        return gen.ToArbitrary();
    }

    // ─── Single scenario components ──────────────────────────────────────

    private sealed record SingleScenario(
        int SourceTypeIndex,
        int NamePrefixIndexA,
        int NamePrefixIndexB);

    private static Gen<SingleScenario> GenSingleScenario()
    {
        return from sourceTypeIdx in Gen.Choose(0, PrimitiveTypes.Length - 1)
               from nameIdxA in Gen.Choose(0, NewtypeNamePrefixes.Length / 2 - 1)
               from nameIdxB in Gen.Choose(NewtypeNamePrefixes.Length / 2, NewtypeNamePrefixes.Length - 1)
               select new SingleScenario(sourceTypeIdx, nameIdxA, nameIdxB);
    }

    // ─── Code generation helpers ─────────────────────────────────────────

    private static string GenerateNewtypeDecls(int index, SingleScenario scenario)
    {
        var sourceType = PrimitiveTypes[scenario.SourceTypeIndex];
        var nameA = $"{NewtypeNamePrefixes[scenario.NamePrefixIndexA]}{index}";
        var nameB = $"{NewtypeNamePrefixes[scenario.NamePrefixIndexB]}{index}";
        return $"type {nameA} = new {sourceType};\ntype {nameB} = new {sourceType};";
    }

    private static string GenerateCrossAssignment(int index, SingleScenario scenario)
    {
        var sourceType = PrimitiveTypes[scenario.SourceTypeIndex];
        var nameA = $"{NewtypeNamePrefixes[scenario.NamePrefixIndexA]}{index}";
        var nameB = $"{NewtypeNamePrefixes[scenario.NamePrefixIndexB]}{index}";

        // Create a value of newtype A via explicit cast, then try to assign it to newtype B
        var defaultValue = sourceType switch
        {
            "int" => "42",
            "string" => "\"hello\"",
            "bool" => "true",
            _ => "0",
        };

        return $"        {nameA} valA{index} = ({nameA}){defaultValue};\n" +
               $"        {nameB} valB{index} = valA{index};";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Cross-newtype non-assignability
    //
    // **Validates: Requirements 2.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any two distinct newtypes that share the same source type, assigning
    /// a value of one newtype to a variable of the other SHALL produce a type
    /// mismatch diagnostic error.
    ///
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NewtypeCrossAssignmentPropertyTests) })]
    public Property CrossNewtypeAssignment_ProducesTypeMismatch(
        CrossAssignmentBatchScenario scenario)
    {
        var diagnostics = Compile(scenario.Source);

        // Filter for type mismatch diagnostics (MIST_TC_0001)
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        // We expect at least one type mismatch per scenario (one cross-assignment each)
        if(typeMismatchDiags.Count < scenario.ScenarioCount)
        {
            var allDiags = diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"  [{d.Id.Value}] {d.Message}");
            return false.ToProperty()
                .Label($"Expected at least {scenario.ScenarioCount} MIST_TC_0001 diagnostics, " +
                       $"got {typeMismatchDiags.Count}")
                .Label($"All error diagnostics:\n{string.Join("\n", allDiags)}")
                .Label($"Source:\n{scenario.Source}");
        }

        return true.ToProperty()
            .Label($"All {scenario.ScenarioCount} cross-newtype assignments correctly rejected");
    }
}
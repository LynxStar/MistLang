// Feature: newtypes, Property 3: Nominal distinctness
//
// **Validates: Requirements 2.1, 2.3, 2.4**
//
// For any newtype and its source type, assigning a value of the source type
// to a variable of the newtype (or vice versa) without an explicit cast
// SHALL produce a type mismatch diagnostic error.
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

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying nominal distinctness of newtypes.
///
/// Property 3: For any newtype and its source type, assigning a value of the
/// source type to a variable of the newtype (or vice versa) without an explicit
/// cast SHALL produce a type mismatch diagnostic error.
///
/// **Validates: Requirements 2.1, 2.3, 2.4**
/// </summary>
public sealed class NewtypeNominalDistinctnessPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Shared infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    /// <summary>
    /// Compiles Mist source in-process and returns all diagnostics
    /// (parse + semantic model + type checker).
    /// </summary>
    private static ImmutableArray<Diagnostic> Compile(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return lexDiag.ToImmutableArray().AddRange(parseDiag.ToImmutableArray());
        }

        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
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
    /// multiple newtype declarations with invalid assignments (source→newtype
    /// and newtype→source without casts).
    /// </summary>
    public sealed record NominalDistinctnessBatchScenario(
        string Source,
        int InvalidAssignmentCount,
        string Description)
    {
        public override string ToString()
        {
            return Description;
        }
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

    private static readonly Dictionary<string, string> DefaultValues = new()
    {
        ["int"] = "42",
        ["string"] = "\"hello\"",
        ["bool"] = "true",
    };

    private enum AssignmentDirection
    {
        SourceToNewtype,
        NewtypeToSource,
    }

    private sealed record SingleScenario(
        int Index,
        string SourceType,
        AssignmentDirection Direction);

    /// <summary>
    /// Generates a batched scenario with N invalid assignments between
    /// newtypes and their source types.
    /// </summary>
    public static Arbitrary<NominalDistinctnessBatchScenario> ArbitraryNominalDistinctnessBatchScenario()
    {
        var gen =
            from scenarioCount in Gen.Choose(3, 10)
            from scenarios in Gen.ArrayOf(GenSingleScenario(), scenarioCount)
            let indexedScenarios = scenarios.Select((s, i) => s with { Index = i }).ToList()
            let newtypeDecls = string.Join("\n", indexedScenarios.Select(GenerateNewtypeDecl))
            let invalidAssignments = string.Join("\n", indexedScenarios.Select(GenerateInvalidAssignment))
            let source = $@"namespace TestNs;

{newtypeDecls}

class Program {{
    public static void Main() {{
{invalidAssignments}
    }}
}}"
            select new NominalDistinctnessBatchScenario(
                source,
                indexedScenarios.Count,
                $"Batch of {indexedScenarios.Count} invalid newtype assignments");

        return gen.ToArbitrary();
    }

    private static Gen<SingleScenario> GenSingleScenario()
    {
        return from sourceType in Gen.Elements(PrimitiveTypes)
               from direction in Gen.Elements(
                   AssignmentDirection.SourceToNewtype,
                   AssignmentDirection.NewtypeToSource)
               select new SingleScenario(0, sourceType, direction);
    }

    // ─── Code generation helpers ─────────────────────────────────────────

    private static string GenerateNewtypeDecl(SingleScenario scenario)
    {
        return $"type Wrapped{scenario.Index} = new {scenario.SourceType};";
    }

    private static string GenerateInvalidAssignment(SingleScenario scenario)
    {
        var newtypeName = $"Wrapped{scenario.Index}";
        var defaultValue = DefaultValues[scenario.SourceType];

        return scenario.Direction switch
        {
            // Source type value assigned to newtype variable (without cast)
            AssignmentDirection.SourceToNewtype =>
                $"        {newtypeName} v{scenario.Index} = {defaultValue};",

            // Newtype value assigned to source type variable (without cast)
            AssignmentDirection.NewtypeToSource =>
                $"        var nt{scenario.Index} = ({newtypeName}){defaultValue};\n" +
                $"        {scenario.SourceType} v{scenario.Index} = nt{scenario.Index};",

            _ => throw new InvalidOperationException(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Nominal distinctness (bidirectional non-assignability)
    //
    // **Validates: Requirements 2.1, 2.3, 2.4**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any newtype and its source type, assigning a value of the source type
    /// to a variable of the newtype (or vice versa) without an explicit cast
    /// SHALL produce a type mismatch diagnostic error.
    ///
    /// **Validates: Requirements 2.1, 2.3, 2.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NewtypeNominalDistinctnessPropertyTests) })]
    public Property NominalDistinctness_InvalidAssignment_ProducesTypeMismatch(
        NominalDistinctnessBatchScenario scenario)
    {
        var diagnostics = Compile(scenario.Source);

        // Filter for type mismatch diagnostics (MIST_TC_0001)
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        // Each invalid assignment scenario should produce exactly one type mismatch
        if(typeMismatchDiags.Count != scenario.InvalidAssignmentCount)
        {
            var allErrors = diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"  [{d.Id.Value}] {d.Message}");
            return false.ToProperty()
                .Label($"Expected {scenario.InvalidAssignmentCount} type mismatch diagnostic(s), " +
                       $"got {typeMismatchDiags.Count}")
                .Label($"All errors:\n{string.Join("\n", allErrors)}")
                .Label($"Source:\n{scenario.Source}");
        }

        return true.ToProperty()
            .Label($"All {scenario.InvalidAssignmentCount} invalid assignments correctly rejected");
    }
}
// Feature: refinement-types, Property 2: Pretty-print round-trip
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Represents a generated refinement type alias source string for round-trip testing.
/// </summary>
public sealed class RefinementTypeRoundTripScenario
{
    public string Source { get; }
    public string Description { get; }

    public RefinementTypeRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests for refinement type alias pretty-print round-trip (idempotence).
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
///
/// For any valid refinement type alias declaration source string, parsing it and
/// pretty-printing the resulting AST, then parsing and pretty-printing again,
/// SHALL produce identical text output (idempotence).
/// </summary>
public class RefinementTypeRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Known type names for generation ────────────────────────────────────

    private static readonly string[] BaseTypes =
        ["int", "byte", "short", "long", "float", "double", "decimal"];

    private static readonly string[] AliasNames =
        ["AdultAge", "Percentage", "Score", "Rating", "Limit", "Bound", "Index", "Count"];

    // ─── Generator ──────────────────────────────────────────────────────────

    public static Arbitrary<RefinementTypeRoundTripScenario> ArbitraryRefinementTypeRoundTripScenario()
    {
        var gen = Gen.OneOf(
            GenSimpleRelational(),
            GenCompoundAnd(),
            GenUnionWithRefinements(),
            GenNoRefinement());

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a simple refinement type alias with a single relational predicate.
    /// Example: type AdultAge = int >= 18;
    /// </summary>
    private static Gen<RefinementTypeRoundTripScenario> GenSimpleRelational()
    {
        return from name in Gen.Elements(AliasNames)
               from baseType in Gen.Elements(BaseTypes)
               from op in Gen.Elements(">=", "<=", ">", "<")
               from value in Gen.Choose(0, 200)
               let source = $"type {name} = {baseType} {op} {value};"
               select new RefinementTypeRoundTripScenario(source, $"simple relational: {source}");
    }

    /// <summary>
    /// Generates a compound refinement type alias with 'and' combinator.
    /// Example: type Percentage = int >= 0 and <= 100;
    /// </summary>
    private static Gen<RefinementTypeRoundTripScenario> GenCompoundAnd()
    {
        return from name in Gen.Elements(AliasNames)
               from baseType in Gen.Elements(BaseTypes)
               from op1 in Gen.Elements(">=", ">")
               from val1 in Gen.Choose(0, 50)
               from op2 in Gen.Elements("<=", "<")
               from val2 in Gen.Choose(51, 200)
               let source = $"type {name} = {baseType} {op1} {val1} and {op2} {val2};"
               select new RefinementTypeRoundTripScenario(source, $"compound and: {source}");
    }

    /// <summary>
    /// Generates a union type alias with refinements on each constituent.
    /// Example: type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;
    /// </summary>
    private static Gen<RefinementTypeRoundTripScenario> GenUnionWithRefinements()
    {
        return from name in Gen.Elements(AliasNames)
               from type1 in Gen.Elements(BaseTypes)
               from op1 in Gen.Elements(">=", ">")
               from val1 in Gen.Choose(0, 30)
               from op2 in Gen.Elements("<=", "<")
               from val2 in Gen.Choose(31, 100)
               from type2 in Gen.Elements(BaseTypes)
               from op3 in Gen.Elements(">=", ">")
               from val3 in Gen.Choose(0, 50)
               from op4 in Gen.Elements("<=", "<")
               from val4 in Gen.Choose(51, 255)
               let source = $"type {name} = {type1} {op1} {val1} and {op2} {val2} | {type2} {op3} {val3} and {op4} {val4};"
               select new RefinementTypeRoundTripScenario(source, $"union with refinements: {source}");
    }

    /// <summary>
    /// Generates a type alias without a refinement predicate (backward compatibility).
    /// Example: type MyInt = int;
    /// </summary>
    private static Gen<RefinementTypeRoundTripScenario> GenNoRefinement()
    {
        return from name in Gen.Elements(AliasNames)
               from baseType in Gen.Elements(BaseTypes)
               let source = $"type {name} = {baseType};"
               select new RefinementTypeRoundTripScenario(source, $"no refinement: {source}");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        }

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    // ─── Property Test ──────────────────────────────────────────────────────

    /// <summary>
    /// Property 2: Pretty-print round-trip (idempotence).
    ///
    /// For any valid refinement type alias declaration source string, parsing and
    /// pretty-printing the result, then parsing and pretty-printing again, SHALL
    /// produce identical output.
    ///
    /// Verifies: print(parse(print(parse(s)))) == print(parse(s))
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefinementTypeRoundTripPropertyTests) })]
    public Property RoundTrip_RefinementTypeAlias_PrintParseIsIdempotent(RefinementTypeRoundTripScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        // Step 3: Parse printed1 → AST2
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST2 → printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify idempotence: printed1 == printed2
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}
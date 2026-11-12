// Feature: refinement-types, Property 2: Pretty-print round-trip
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public sealed class RefinementTypeRoundTripScenario
{
    public string Source { get; }
    public string Description { get; }

    public RefinementTypeRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public class RefinementTypeRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static readonly string[] BaseTypes =
        ["int", "byte", "short", "long", "float", "double", "decimal"];

    private static readonly string[] AliasNames =
        ["AdultAge", "Percentage", "Score", "Rating", "Limit", "Bound", "Index", "Count"];

    public static Arbitrary<RefinementTypeRoundTripScenario> ArbitraryRefinementTypeRoundTripScenario()
    {
        var gen = Gen.OneOf(
            GenSimpleRelational(),
            GenCompoundAnd(),
            GenUnionWithRefinements(),
            GenNoRefinement());

        return gen.ToArbitrary();
    }

    private static Gen<RefinementTypeRoundTripScenario> GenSimpleRelational()
    {
        return from name in Gen.Elements(AliasNames)
               from baseType in Gen.Elements(BaseTypes)
               from op in Gen.Elements(">=", "<=", ">", "<")
               from value in Gen.Choose(0, 200)
               let source = $"type {name} = {baseType} {op} {value};"
               select new RefinementTypeRoundTripScenario(source, $"simple relational: {source}");
    }

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

    private static Gen<RefinementTypeRoundTripScenario> GenNoRefinement()
    {
        return from name in Gen.Elements(AliasNames)
               from baseType in Gen.Elements(BaseTypes)
               let source = $"type {name} = {baseType};"
               select new RefinementTypeRoundTripScenario(source, $"no refinement: {source}");
    }

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefinementTypeRoundTripPropertyTests) })]
    public Property RoundTrip_RefinementTypeAlias_PrintParseIsIdempotent(RefinementTypeRoundTripScenario scenario)
    {
        var ast1 = ParseOrFail(scenario.Source);

        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        var ast2 = ParseOrFail(printed1);

        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}
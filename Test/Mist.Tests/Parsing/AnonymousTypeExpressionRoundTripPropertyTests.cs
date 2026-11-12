// Feature: anonymous-types, Property 1: Anonymous type expression round-trip
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.6, 1.7**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

public sealed class AnonymousTypeRoundTripScenario
{
    public string Source { get; }

    public string Description { get; }

    public AnonymousTypeRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public class AnonymousTypeExpressionRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    public static Arbitrary<AnonymousTypeRoundTripScenario> ArbitraryScenario()
    {
        return AnonymousTypeRoundTripGenerator.GenScenario().ToArbitrary();
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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypeExpressionRoundTripPropertyTests) })]
    public Property RoundTrip_AnonymousTypeExpression_PrintParseIsIdempotent(AnonymousTypeRoundTripScenario scenario)
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

public static class AnonymousTypeRoundTripGenerator
{
    private static readonly string[] PropertyNames =
        ["Name", "Value", "Count", "Size", "Width", "Height", "Timeout", "Label", "Score", "Enabled"];

    private static readonly string[] IdentifierNames =
        ["name", "value", "count", "size", "width", "height", "timeout", "label", "score", "enabled"];

    private static readonly string[] ObjectNames =
        ["obj", "item", "data", "config", "state", "node"];

    private static readonly string[] MemberNames =
        ["Name", "Value", "Id", "Key", "Type", "Status"];

    private static Gen<string> GenIntExpr()
    {
        return Gen.Choose(0, 999).Select(n => n.ToString());
    }

    private static Gen<string> GenBoolExpr()
    {
        return Gen.Elements("true", "false");
    }

    private static Gen<string> GenStringExpr()
    {
        return Gen.Elements("\"hello\"", "\"world\"", "\"test\"", "\"\"");
    }

    private static Gen<string> GenValueExpr()
    {
        return Gen.OneOf(
            GenIntExpr(),
            GenBoolExpr(),
            GenStringExpr());
    }

    private static Gen<string> GenExplicitProperty(int index)
    {
        return from value in GenValueExpr()
               select $"{PropertyNames[index % PropertyNames.Length]} = {value}";
    }

    private static Gen<string> GenIdentifierProjection(int index)
    {
        return Gen.Constant(IdentifierNames[index % IdentifierNames.Length]);
    }

    private static Gen<string> GenMemberAccessProjection()
    {
        return from objIdx in Gen.Choose(0, ObjectNames.Length - 1)
               from memberIdx in Gen.Choose(0, MemberNames.Length - 1)
               select $"{ObjectNames[objIdx]}.{MemberNames[memberIdx]}";
    }

    private static Gen<AnonymousTypeRoundTripScenario> GenExplicitOnly()
    {
        return from count in Gen.Choose(1, 4)
               from properties in GenExplicitProperties(count)
               let newExpr = $"new {{ {properties} }}"
               let source = WrapInProgram(newExpr)
               select new AnonymousTypeRoundTripScenario(source,
                   $"explicit-only({count}): {newExpr}");
    }

    private static Gen<AnonymousTypeRoundTripScenario> GenIdentifierProjectionOnly()
    {
        return from count in Gen.Choose(1, 4)
               from properties in GenIdentifierProjections(count)
               let newExpr = $"new {{ {properties} }}"
               let source = WrapInProgram(newExpr)
               select new AnonymousTypeRoundTripScenario(source,
                   $"projection-identifier({count}): {newExpr}");
    }

    private static Gen<AnonymousTypeRoundTripScenario> GenMemberAccessProjectionOnly()
    {
        return from count in Gen.Choose(1, 3)
               from properties in GenMemberAccessProjections(count)
               let newExpr = $"new {{ {properties} }}"
               let source = WrapInProgram(newExpr)
               select new AnonymousTypeRoundTripScenario(source,
                   $"projection-member-access({count}): {newExpr}");
    }

    private static Gen<AnonymousTypeRoundTripScenario> GenMixed()
    {
        return from explicitCount in Gen.Choose(1, 2)
               from projectionCount in Gen.Choose(1, 2)
               from explicitProps in GenExplicitProperties(explicitCount)
               from projections in GenMixedProjections(projectionCount)
               let properties = $"{explicitProps}, {projections}"
               let newExpr = $"new {{ {properties} }}"
               let source = WrapInProgram(newExpr)
               select new AnonymousTypeRoundTripScenario(source,
                   $"mixed({explicitCount}+{projectionCount}): {newExpr}");
    }

    public static Gen<AnonymousTypeRoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenExplicitOnly(),
            GenIdentifierProjectionOnly(),
            GenMemberAccessProjectionOnly(),
            GenMixed());
    }

    private static Gen<string> GenExplicitProperties(int count)
    {
        var gens = Enumerable.Range(0, count)
            .Select(GenExplicitProperty)
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray())
            .Select(props => string.Join(", ", props));
    }

    private static Gen<string> GenIdentifierProjections(int count)
    {
        var gens = Enumerable.Range(0, count)
            .Select(GenIdentifierProjection)
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray())
            .Select(props => string.Join(", ", props));
    }

    private static Gen<string> GenMemberAccessProjections(int count)
    {
        var gens = Enumerable.Range(0, count)
            .Select(_ => GenMemberAccessProjection())
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray())
            .Select(props => string.Join(", ", props));
    }

    private static Gen<string> GenMixedProjections(int count)
    {
        var gens = Enumerable.Range(0, count)
            .Select(i => i % 2 == 0
                ? GenIdentifierProjection(i + 5) // offset to avoid name collision with explicit props
                : GenMemberAccessProjection())
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray())
            .Select(props => string.Join(", ", props));
    }

    private static string WrapInProgram(string newExpr)
    {
        return $@"class Program {{
    public static void Main() {{
        var x = {newExpr};
    }}
}}";
    }
}
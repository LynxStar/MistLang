// Feature: property-initializers, Property 1: Pretty-printer round trip
// **Validates: Requirements 9.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

public class PropertyInitializerRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    public static Arbitrary<PropertyInitializerSourceScenario> ArbitraryScenario()
    {
        return PropertyInitializerSourceGenerator.GenScenario().ToArbitrary();
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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(PropertyInitializerRoundTripPropertyTests) })]
    public Property RoundTrip_PropertyInitializer_PrintParseIsIdempotent(PropertyInitializerSourceScenario scenario)
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

public sealed class PropertyInitializerSourceScenario
{
    public string Source { get; }

    public string Description { get; }

    public PropertyInitializerSourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public static class PropertyInitializerSourceGenerator
{
    private static readonly string[] TypeNames =
        ["Config", "Point", "Settings", "Options", "Data", "State"];

    private static readonly string[] PropertyNames =
        ["Name", "Value", "Count", "Size", "Width", "Height", "Timeout", "Label", "Score", "Enabled"];

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

    private static Gen<string> GenAdditiveExpr()
    {
        return from left in Gen.Choose(0, 100)
               from right in Gen.Choose(1, 50)
               select $"{left} + {right}";
    }

    private static Gen<string> GenMultiplicativeExpr()
    {
        return from left in Gen.Choose(1, 20)
               from right in Gen.Choose(1, 10)
               select $"{left} * {right}";
    }

    private static Gen<string> GenValueExpr()
    {
        return Gen.OneOf(
            GenIntExpr(),
            GenBoolExpr(),
            GenStringExpr(),
            GenAdditiveExpr(),
            GenMultiplicativeExpr());
    }

    private static Gen<string> GenAssignment(int index)
    {
        return from value in GenValueExpr()
               select $"{PropertyNames[index]} = {value}";
    }

    private static Gen<string[]> GenAssignments(int count)
    {
        if(count == 0)
        {
            return Gen.Constant(Array.Empty<string>());
        }

        // Use distinct property names by picking from the pool sequentially
        var gens = Enumerable.Range(0, count)
            .Select(GenAssignment)
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray());
    }

    private static Gen<string> GenConstructorArgs()
    {
        return Gen.OneOf(
            Gen.Constant(""),
            Gen.Choose(0, 100).Select(n => n.ToString()),
            from a in Gen.Choose(0, 50)
            from b in Gen.Choose(0, 50)
            select $"{a}, {b}",
            from a in Gen.Choose(0, 50)
            from b in Gen.Choose(0, 50)
            from c in Gen.Choose(0, 50)
            select $"{a}, {b}, {c}");
    }

    private static Gen<PropertyInitializerSourceScenario> GenWithArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from args in GenConstructorArgs()
               from assignmentCount in Gen.Choose(1, 5)
               from assignments in GenAssignments(assignmentCount)
               from trailingComma in Gen.Elements(true, false)
               let assignmentList = string.Join(", ", assignments) + (trailingComma ? "," : "")
               let newExpr = $"new {typeName}({args}) {{ {assignmentList} }}"
               let source = WrapInProgram(newExpr)
               select new PropertyInitializerSourceScenario(source,
                   $"with-args({assignmentCount}): new {typeName}({args}) {{ ... }}");
    }

    private static Gen<PropertyInitializerSourceScenario> GenNoArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from assignmentCount in Gen.Choose(1, 5)
               from assignments in GenAssignments(assignmentCount)
               from trailingComma in Gen.Elements(true, false)
               let assignmentList = string.Join(", ", assignments) + (trailingComma ? "," : "")
               let newExpr = $"new {typeName} {{ {assignmentList} }}"
               let source = WrapInProgram(newExpr)
               select new PropertyInitializerSourceScenario(source,
                   $"no-args({assignmentCount}): new {typeName} {{ ... }}");
    }

    private static Gen<PropertyInitializerSourceScenario> GenEmptyBlock()
    {
        return from typeName in Gen.Elements(TypeNames)
               from args in GenConstructorArgs()
               let newExpr = $"new {typeName}({args}) {{  }}"
               let source = WrapInProgram(newExpr)
               select new PropertyInitializerSourceScenario(source,
                   $"empty-block: new {typeName}({args}) {{  }}");
    }

    private static Gen<PropertyInitializerSourceScenario> GenNested()
    {
        return from outerType in Gen.Elements(TypeNames)
               from innerType in Gen.Elements(TypeNames)
               from innerArgs in GenConstructorArgs()
               from innerPropIndex in Gen.Choose(0, 4)
               from innerValue in GenIntExpr()
               from outerPropIndex in Gen.Choose(5, 9)
               let innerExpr = $"new {innerType}({innerArgs}) {{ {PropertyNames[innerPropIndex]} = {innerValue} }}"
               let newExpr = $"new {outerType}() {{ {PropertyNames[outerPropIndex]} = {innerExpr} }}"
               let source = WrapInProgram(newExpr)
               select new PropertyInitializerSourceScenario(source,
                   $"nested: new {outerType}() {{ ... = new {innerType}(...) {{ ... }} }}");
    }

    public static Gen<PropertyInitializerSourceScenario> GenScenario()
    {
        return Gen.OneOf(
            GenWithArgs(),
            GenNoArgs(),
            GenEmptyBlock(),
            GenNested());
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
// Feature: target-typed-new, Property 1: Parsing round-trip
// **Validates: Requirements 2.5**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

public sealed class TargetTypedNewRoundTripScenario
{
    public string Source { get; }

    public string Description { get; }

    public TargetTypedNewRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public class TargetTypedNewRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    public static Arbitrary<TargetTypedNewRoundTripScenario> ArbitraryScenario()
    {
        return TargetTypedNewRoundTripGenerator.GenScenario().ToArbitrary();
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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TargetTypedNewRoundTripPropertyTests) })]
    public Property RoundTrip_TargetTypedNew_PrintParseIsIdempotent(TargetTypedNewRoundTripScenario scenario)
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

public static class TargetTypedNewRoundTripGenerator
{
    private static readonly string[] TypeNames =
        ["Point", "Config", "Settings", "Options", "Data", "State", "Node", "Item"];

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

    private static Gen<string> GenValueExpr()
    {
        return Gen.OneOf(
            GenIntExpr(),
            GenBoolExpr(),
            GenStringExpr());
    }

    private static Gen<string> GenArgList(int count)
    {
        if(count == 0)
        {
            return Gen.Constant("");
        }

        var gens = Enumerable.Range(0, count)
            .Select(_ => GenValueExpr())
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray())
            .Select(args => string.Join(", ", args));
    }

    private static Gen<string> GenAssignment(int index)
    {
        return from value in GenValueExpr()
               select $"{PropertyNames[index]} = {value}";
    }

    private static Gen<string> GenAssignments(int count)
    {
        if(count == 0)
        {
            return Gen.Constant("");
        }

        var gens = Enumerable.Range(0, count)
            .Select(GenAssignment)
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray())
            .Select(assignments => string.Join(", ", assignments));
    }

    private static Gen<TargetTypedNewRoundTripScenario> GenNoArgsNoInitializer()
    {
        return from typeName in Gen.Elements(TypeNames)
               let newExpr = "new()"
               let source = WrapInProgram(typeName, newExpr)
               select new TargetTypedNewRoundTripScenario(source, $"no-args: {typeName} x = new()");
    }

    private static Gen<TargetTypedNewRoundTripScenario> GenWithArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from argCount in Gen.Choose(1, 4)
               from args in GenArgList(argCount)
               let newExpr = $"new({args})"
               let source = WrapInProgram(typeName, newExpr)
               select new TargetTypedNewRoundTripScenario(source,
                   $"with-args({argCount}): {typeName} x = new({args})");
    }

    private static Gen<TargetTypedNewRoundTripScenario> GenNoArgsWithInitializer()
    {
        return from typeName in Gen.Elements(TypeNames)
               from assignmentCount in Gen.Choose(1, 4)
               from assignments in GenAssignments(assignmentCount)
               let newExpr = $"new() {{ {assignments} }}"
               let source = WrapInProgram(typeName, newExpr)
               select new TargetTypedNewRoundTripScenario(source,
                   $"no-args-with-init({assignmentCount}): {typeName} x = new() {{ ... }}");
    }

    private static Gen<TargetTypedNewRoundTripScenario> GenWithArgsAndInitializer()
    {
        return from typeName in Gen.Elements(TypeNames)
               from argCount in Gen.Choose(1, 3)
               from args in GenArgList(argCount)
               from assignmentCount in Gen.Choose(1, 4)
               from assignments in GenAssignments(assignmentCount)
               let newExpr = $"new({args}) {{ {assignments} }}"
               let source = WrapInProgram(typeName, newExpr)
               select new TargetTypedNewRoundTripScenario(source,
                   $"with-args-and-init({argCount},{assignmentCount}): {typeName} x = new({args}) {{ ... }}");
    }

    private static Gen<TargetTypedNewRoundTripScenario> GenNoParensWithInitializer()
    {
        return from typeName in Gen.Elements(TypeNames)
               from assignmentCount in Gen.Choose(1, 4)
               from assignments in GenAssignments(assignmentCount)
               let newExpr = $"new {{ {assignments} }}"
               let source = WrapInProgram(typeName, newExpr)
               select new TargetTypedNewRoundTripScenario(source,
                   $"no-parens-with-init({assignmentCount}): {typeName} x = new {{ ... }}");
    }

    public static Gen<TargetTypedNewRoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenNoArgsNoInitializer(),
            GenWithArgs(),
            GenNoArgsWithInitializer(),
            GenWithArgsAndInitializer(),
            GenNoParensWithInitializer());
    }

    private static string WrapInProgram(string typeName, string newExpr)
    {
        return $@"class Program {{
    public static void Main() {{
        {typeName} x = {newExpr};
    }}
}}";
    }
}
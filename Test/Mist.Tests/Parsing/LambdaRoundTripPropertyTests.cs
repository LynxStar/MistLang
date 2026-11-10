// Feature: first-class-functions, Property 6: Pretty-print round-trip
// **Validates: Requirements 13.7, 13.8**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public class LambdaRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ──────────────────────────────────────────────

    public static Arbitrary<RoundTripScenario> ArbitraryRoundTripScenario()
    {
        return RoundTripGenerator.GenScenario().ToArbitrary();
    }

    // ─── Parse + pretty-print helpers ────────────────────────────────────────

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

    private static string PrintOnce(string source)
    {
        var ast = ParseOrFail(source);
        return MistPrettyPrinter.Print(ast, source);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Property 6: Pretty-Print Round-Trip
    //
    // For any generated program S containing lambdas and func types:
    //   print(parse(print(parse(S)))) == print(parse(S))
    // ═══════════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LambdaRoundTripPropertyTests) })]
    public Property PrettyPrint_RoundTrip_IsIdempotent(RoundTripScenario scenario)
    {
        string printed1;
        try
        {
            printed1 = PrintOnce(scenario.Source);
        }
        catch(Exception ex)
        {
            return false.ToProperty()
                .Label($"First print pass failed:\n{ex.Message}\nSource:\n{scenario.Source}");
        }

        string printed2;
        try
        {
            printed2 = PrintOnce(printed1);
        }
        catch(Exception ex)
        {
            return false.ToProperty()
                .Label($"Second print pass failed (re-parse of printed output):\n{ex.Message}\nPrinted1:\n{printed1}\nOriginal source:\n{scenario.Source}");
        }

        var isIdempotent = printed1 == printed2;

        return isIdempotent.ToProperty()
            .Label($"Pretty-print is NOT idempotent.\n" +
                   $"Description: {scenario.Description}\n" +
                   $"Original source:\n{scenario.Source}\n" +
                   $"Printed1:\n{printed1}\n" +
                   $"Printed2:\n{printed2}");
    }
}

public sealed class RoundTripScenario
{
    public string Source { get; }

    public string Description { get; }

    public RoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public static class RoundTripGenerator
{
    // ─── Building blocks ─────────────────────────────────────────────────────

    private static readonly string[] Identifiers =
        ["a", "b", "c", "f", "g", "x", "y", "z", "foo", "bar"];

    private static readonly string[] SimpleTypes =
        ["int", "string", "bool", "double"];

    private static readonly string[] Modifiers =
        ["", "static ", "async ", "static async "];

    private static readonly string[] ExpressionBodies =
        ["x + 1", "42", "x", "x * y", "true", "\"hi\""];

    private static readonly string[] BlockBodies =
        ["{ return x + 1; }", "{ return 42; }", "{ }"];

    private static readonly string[] GuardExpressions =
        ["n > 0", "n < 100", "n >= 1", "n <= 50", "n == 42", "isValid"];

    private static readonly string[] SwitchResults =
        ["n.ToString()", "\"yes\"", "0", "42"];

    // ─── Lambda generators ───────────────────────────────────────────────────

    private static Gen<string> GenSingleParamExprLambda()
    {
        return from modifier in Gen.Elements(Modifiers)
               from param in Gen.Elements(Identifiers)
               from body in Gen.Elements(ExpressionBodies)
               select $"{modifier}{param} => {body}";
    }

    private static Gen<string> GenParenExprLambda()
    {
        return from modifier in Gen.Elements(Modifiers)
               from paramForm in Gen.OneOf(
                   Gen.Constant("()"),
                   Gen.Elements(Identifiers).Select(n => $"({n})"),
                   from n1 in Gen.Elements(Identifiers)
                   from n2 in Gen.Elements(Identifiers)
                   select $"({n1}, {n2})")
               from body in Gen.Elements(ExpressionBodies)
               select $"{modifier}{paramForm} => {body}";
    }

    private static Gen<string> GenTypedParamExprLambda()
    {
        return from modifier in Gen.Elements(Modifiers)
               from paramForm in Gen.OneOf(
                   from t in Gen.Elements(SimpleTypes)
                   from n in Gen.Elements(Identifiers)
                   select $"({t} {n})",
                   from t1 in Gen.Elements(SimpleTypes)
                   from n1 in Gen.Elements(Identifiers)
                   from t2 in Gen.Elements(SimpleTypes)
                   from n2 in Gen.Elements(Identifiers)
                   select $"({t1} {n1}, {t2} {n2})")
               from body in Gen.Elements(ExpressionBodies)
               select $"{modifier}{paramForm} => {body}";
    }

    private static Gen<string> GenBlockLambda()
    {
        return from modifier in Gen.Elements(Modifiers)
               from paramForm in Gen.OneOf(
                   Gen.Elements(Identifiers).Select(n => $"({n})"),
                   from t in Gen.Elements(SimpleTypes)
                   from n in Gen.Elements(Identifiers)
                   select $"({t} {n})",
                   Gen.Constant("()"))
               from body in Gen.Elements(BlockBodies)
               select $"{modifier}{paramForm} => {body}";
    }

    private static Gen<string> GenNestedLambda()
    {
        return from modifier in Gen.Elements(Modifiers)
               from p1 in Gen.Elements(Identifiers)
               from p2 in Gen.Elements(Identifiers)
               from body in Gen.Elements(ExpressionBodies)
               select $"{modifier}{p1} => {p2} => {body}";
    }

    private static Gen<string> GenAnyLambda()
    {
        return Gen.OneOf(
            GenSingleParamExprLambda(),
            GenParenExprLambda(),
            GenTypedParamExprLambda(),
            GenBlockLambda(),
            GenNestedLambda());
    }

    // ─── func<...> type reference generators ─────────────────────────────────

    private static Gen<string> GenFuncTypeRef()
    {
        return Gen.Choose(1, 4).SelectMany(arity =>
            Gen.ArrayOf(Gen.Elements(SimpleTypes), arity)
                .Select(types => $"func<{string.Join(", ", types)}>"));
    }

    // ─── Program-level scenario generators ───────────────────────────────────

    private static string WrapInMethod(string body)
    {
        return $"namespace TestNs;\n\npublic class Program\n{{\n    public void Run()\n    {{\n        {body}\n    }}\n}}";
    }

    private static Gen<RoundTripScenario> GenAssignmentRhsScenario()
    {
        return from lambda in GenAnyLambda()
               from name in Gen.Elements(Identifiers)
               let body = $"var {name} = {lambda};"
               let source = WrapInMethod(body)
               select new RoundTripScenario(source, $"assignment RHS: var {name} = {lambda}");
    }

    private static Gen<RoundTripScenario> GenFuncTypedAssignmentScenario()
    {
        return from funcType in GenFuncTypeRef()
               from lambda in GenAnyLambda()
               from name in Gen.Elements(Identifiers)
               let body = $"{funcType} {name} = {lambda};"
               let source = WrapInMethod(body)
               select new RoundTripScenario(source, $"func-typed assignment: {funcType} {name} = {lambda}");
    }

    private static Gen<RoundTripScenario> GenCallArgumentScenario()
    {
        return from lambda in GenAnyLambda()
               from fn in Gen.Elements(Identifiers)
               let body = $"{fn}({lambda});"
               let source = WrapInMethod(body)
               select new RoundTripScenario(source, $"call argument: {fn}({lambda})");
    }

    private static Gen<RoundTripScenario> GenReturnScenario()
    {
        return from lambda in GenAnyLambda()
               let body = $"return {lambda};"
               let source = WrapInMethod(body)
               select new RoundTripScenario(source, $"return: return {lambda}");
    }

    private static Gen<RoundTripScenario> GenNestedBodyScenario()
    {
        return from lambda in GenAnyLambda()
               from outer in Gen.Elements(Identifiers)
               let body = $"var h = {outer} => {lambda};"
               let source = WrapInMethod(body)
               select new RoundTripScenario(source, $"nested body: var h = {outer} => {lambda}");
    }

    private static Gen<RoundTripScenario> GenSwitchArmScenario()
    {
        return from guard in Gen.Elements(GuardExpressions)
               from result in Gen.Elements(SwitchResults)
               from defaultResult in Gen.Elements(SwitchResults)
               let body = $"var r = value switch {{ int n when {guard} => {result}, _ => {defaultResult} }};"
               let source = WrapInMethod(body)
               select new RoundTripScenario(source, $"switch arm: when {guard} => {result}");
    }

    private static Gen<RoundTripScenario> GenFuncTypeParamScenario()
    {
        return from funcType in GenFuncTypeRef()
               from paramName in Gen.Elements(Identifiers)
               let source = $"namespace TestNs;\n\npublic class Program\n{{\n    public void Run({funcType} {paramName})\n    {{\n    }}\n}}"
               select new RoundTripScenario(source, $"func type param: {funcType} {paramName}");
    }

    public static Gen<RoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenAssignmentRhsScenario(),
            GenFuncTypedAssignmentScenario(),
            GenCallArgumentScenario(),
            GenReturnScenario(),
            GenNestedBodyScenario(),
            GenSwitchArmScenario(),
            GenFuncTypeParamScenario());
    }
}
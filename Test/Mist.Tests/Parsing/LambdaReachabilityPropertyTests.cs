// Feature: first-class-functions, Property 10: Lambda is reachable from every value position
// **Validates: Requirements 7.1, 7.2, 7.3, 12.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Parsing;

public class LambdaReachabilityPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ──────────────────────────────────────────────

    public static Arbitrary<LambdaReachabilityScenario> ArbitraryLambdaReachabilityScenario()
    {
        return LambdaReachabilityGenerator.GenLambda().ToArbitrary();
    }

    // ─── Parse + navigation helpers ───────────────────────────────────────────

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

    private static T? FindFirst<T>(SyntaxNode root) where T : SyntaxNode
    {
        if(root is T match)
        {
            return match;
        }

        foreach(var child in root.Children())
        {
            if(child is null)
            {
                continue;
            }

            var found = FindFirst<T>(child);
            if(found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// The function return type only makes <c>return L;</c> read naturally; nothing type-checks the body.
    /// </summary>
    private static string WrapMethodBody(string body)
    {
        return "namespace TestNs;\n\npublic class C\n{\n    public func<int, int> M()\n    {\n        "
        + body + "\n    }\n}";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Property 10: Lambda reachable from every value position.
    //
    // For each generated lambda L, four programs are parsed — one per value
    // position — and each is checked for a lambda node in the corresponding slot.
    // ═══════════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LambdaReachabilityPropertyTests) })]
    public Property Lambda_IsReachable_FromEveryValuePosition(LambdaReachabilityScenario scenario)
    {
        var l = scenario.LambdaText;

        var assignFailure = CheckAssignmentRhs(l);
        if(assignFailure is not null)
        {
            return false.ToProperty().Label(assignFailure);
        }

        var argFailure = CheckCallArgument(l);
        if(argFailure is not null)
        {
            return false.ToProperty().Label(argFailure);
        }

        var returnFailure = CheckReturnValue(l);
        if(returnFailure is not null)
        {
            return false.ToProperty().Label(returnFailure);
        }

        var nestedFailure = CheckNestedLambdaBody(l);
        if(nestedFailure is not null)
        {
            return false.ToProperty().Label(nestedFailure);
        }

        return true.ToProperty();
    }

    private static string? CheckAssignmentRhs(string lambdaText)
    {
        var source = WrapMethodBody($"f = {lambdaText};");
        var cu = ParseOrFail(source);

        var stmt = FindFirst<ExpressionStatementNode>(cu);
        if(stmt is null)
        {
            return $"[assignment RHS] no ExpressionStatementNode found.\nLambda: {lambdaText}\nSource:\n{source}";
        }

        if(stmt.Expression is not AssignmentFormExpressionNode assignForm)
        {
            return $"[assignment RHS] expression was {stmt.Expression?.GetType().Name ?? "null"}, expected AssignmentFormExpressionNode.\nLambda: {lambdaText}\nSource:\n{source}";
        }

        if(assignForm.Assignment.Group0?.Value is not LambdaFormExpressionNode)
        {
            return $"[assignment RHS] assignment value was {assignForm.Assignment.Group0?.Value?.GetType().Name ?? "null"}, expected LambdaFormExpressionNode.\nLambda: {lambdaText}\nSource:\n{source}";
        }

        return null;
    }

    private static string? CheckCallArgument(string lambdaText)
    {
        var source = WrapMethodBody($"g({lambdaText});");
        var cu = ParseOrFail(source);

        var arg = FindFirst<ValueArgumentArgumentNode>(cu);
        if(arg is null)
        {
            return $"[call argument] no ValueArgumentArgumentNode found.\nLambda: {lambdaText}\nSource:\n{source}";
        }

        if(arg.Expression is not LambdaFormExpressionNode)
        {
            return $"[call argument] argument expression was {arg.Expression?.GetType().Name ?? "null"}, expected LambdaFormExpressionNode.\nLambda: {lambdaText}\nSource:\n{source}";
        }

        return null;
    }

    private static string? CheckReturnValue(string lambdaText)
    {
        var source = WrapMethodBody($"return {lambdaText};");
        var cu = ParseOrFail(source);

        var ret = FindFirst<ReturnStatementNode>(cu);
        if(ret is null)
        {
            return $"[return value] no ReturnStatementNode found.\nLambda: {lambdaText}\nSource:\n{source}";
        }

        if(ret.Value is not LambdaFormExpressionNode)
        {
            return $"[return value] return value was {ret.Value?.GetType().Name ?? "null"}, expected LambdaFormExpressionNode.\nLambda: {lambdaText}\nSource:\n{source}";
        }

        return null;
    }

    private static string? CheckNestedLambdaBody(string lambdaText)
    {
        var source = WrapMethodBody($"var h = q => {lambdaText};");
        var cu = ParseOrFail(source);

        // The outer lambda is the first lambda encountered top-down.
        var outer = FindFirst<ExpressionLambdaLambdaExpressionNode>(cu);
        if(outer is null)
        {
            return $"[nested lambda body] no outer ExpressionLambdaLambdaExpressionNode found.\nLambda: {lambdaText}\nSource:\n{source}";
        }

        if(outer.Body is not LambdaFormExpressionNode)
        {
            return $"[nested lambda body] outer lambda body was {outer.Body?.GetType().Name ?? "null"}, expected LambdaFormExpressionNode.\nLambda: {lambdaText}\nSource:\n{source}";
        }

        return null;
    }
}

public sealed class LambdaReachabilityScenario
{
    public string LambdaText { get; }

    public LambdaReachabilityScenario(string lambdaText)
    {
        LambdaText = lambdaText;
    }

    public override string ToString()
    {
        return LambdaText;
    }
}

public static class LambdaReachabilityGenerator
{
    private static readonly string[] Modifiers =
        ["", "static ", "async ", "static async "];

    private static readonly string[] ParamLists =
        ["x", "(x)", "(x, y)", "()", "(int x)", "(int x, string y)"];

    // No calls or assignments, so the only lambda and call argument in any wrapping come from the
    // lambda itself.
    private static readonly string[] ExpressionBodies =
        ["x + 1", "42", "x", "\"hi\"", "x * y", "true"];

    private static Gen<string> GenExpressionBodyLambda()
    {
        return from modifier in Gen.Elements(Modifiers)
               from paramList in Gen.Elements(ParamLists)
               from body in Gen.Elements(ExpressionBodies)
               select $"{modifier}{paramList} => {body}";
    }

    private static Gen<string> GenBlockBodyLambda()
    {
        return from modifier in Gen.Elements(Modifiers)
               from paramList in Gen.Elements(ParamLists)
               from body in Gen.Elements(ExpressionBodies)
               from empty in Gen.Elements(true, false)
               let block = empty ? "{ }" : $"{{ return {body}; }}"
               select $"{modifier}{paramList} => {block}";
    }

    public static Gen<LambdaReachabilityScenario> GenLambda()
    {
        return Gen.OneOf(GenExpressionBodyLambda(), GenBlockBodyLambda())
            .Select(text => new LambdaReachabilityScenario(text));
    }
}
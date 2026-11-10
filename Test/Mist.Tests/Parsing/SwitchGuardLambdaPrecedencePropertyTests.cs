// Feature: first-class-functions, Property 8: Switch-arm guard never captures a trailing lambda
// **Validates: Requirements 12.5**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Parsing;

/// <summary>
/// Lambda sits at the lowest precedence, a peer of assignment at the <c>Expression</c> level, and a
/// <c>when</c> guard is bounded to <c>VoidCoalescing</c>, so the guard never reaches the lambda
/// alternative and the arm's arrow (<c>SUCH_THAT</c>) is always left for <c>SwitchArm</c>.
/// </summary>
public class SwitchGuardLambdaPrecedencePropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

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

    private static List<T> FindAll<T>(SyntaxNode root) where T : SyntaxNode
    {
        var results = new List<T>();

        void walk(SyntaxNode n)
        {
            if(n is T match)
            {
                results.Add(match);
            }

            foreach(var child in n.Children())
            {
                if(child is not null)
                {
                    walk(child);
                }
            }
        }

        walk(root);
        return results;
    }

    private static string SpanText(string source, Storm.Runtime.SourceSpan span)
    {
        return source.Substring(span.Start, span.Length);
    }

    private static string NormalizeWs(string s)
    {
        return string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// The scrutinee, guard and result may name undefined symbols: these tests only lex and parse.
    /// </summary>
    private static string Wrap(string arms)
    {
        return "namespace TestNs;\n\npublic class Program\n{\n    public static void Test()\n    {\n        "
        + $"var r = value switch {{ {arms} }};\n        var __use = r;"
        + "\n    }\n}";
    }

    private static SwitchArmNode SingleGuardedArm(CompilationUnitNode cu)
    {
        var guarded = FindAll<SwitchArmNode>(cu).Where(a => a.Guard is not null).ToList();
        _ = Assert.Single(guarded);
        return guarded[0];
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] Identifiers =
        ["predicate", "isValid", "flag", "ready", "enabled", "n", "x", "ok"];

    private static readonly string[] FunctionNames =
        ["isOdd", "check", "matches", "validate"];

    private static readonly string[] MemberNames =
        ["Flag", "Ready", "Valid", "IsActive"];

    private static readonly string[] Results =
        ["\"yes\"", "\"no\"", "\"none\"", "0", "1", "42", "n.ToString()", "result"];

    /// <summary>A bare identifier — the case most likely to be mis-parsed as a single-param lambda.</summary>
    private static Gen<string> GenBareIdentifierGuard()
    {
        return Gen.Elements(Identifiers);
    }

    private static Gen<string> GenComparisonGuard()
    {
        return from id in Gen.Elements(Identifiers)
               from op in Gen.Elements(">", "<", ">=", "<=", "==", "!=")
               from v in Gen.Choose(0, 100)
               select $"{id} {op} {v}";
    }

    private static Gen<string> GenInvocationGuard()
    {
        return from fn in Gen.Elements(FunctionNames)
               from arg in Gen.Elements(Identifiers)
               select $"{fn}({arg})";
    }

    private static Gen<string> GenLogicalGuard()
    {
        return from a in Gen.Elements(Identifiers)
               from op in Gen.Elements("&&", "||")
               from b in Gen.Elements(Identifiers)
               select $"{a} {op} {b}";
    }

    private static Gen<string> GenNegationGuard()
    {
        return from id in Gen.Elements(Identifiers)
               select $"!{id}";
    }

    private static Gen<string> GenMemberAccessGuard()
    {
        return from obj in Gen.Elements(Identifiers)
               from m in Gen.Elements(MemberNames)
               select $"{obj}.{m}";
    }

    /// <summary>A null-coalescing expression — the top of the bounded <c>VoidCoalescing</c> rule.</summary>
    private static Gen<string> GenCoalesceGuard()
    {
        return from a in Gen.Elements(Identifiers)
               from b in Gen.Elements(Identifiers)
               select $"{a} ?? {b}";
    }

    private static Gen<string> GenGuard()
    {
        return Gen.OneOf(
            // Listed twice to bias toward the bare-identifier case.
            GenBareIdentifierGuard(),
            GenBareIdentifierGuard(),
            GenComparisonGuard(),
            GenInvocationGuard(),
            GenLogicalGuard(),
            GenNegationGuard(),
            GenMemberAccessGuard(),
            GenCoalesceGuard());
    }

    public static Gen<SwitchGuardScenario> GenScenario()
    {
        return from guard in GenGuard()
               from result in Gen.Elements(Results)
               from defaultResult in Gen.Elements(Results)
               let arms = $"int n when {guard} => {result}, _ => {defaultResult}"
               let source = Wrap(arms)
               select new SwitchGuardScenario(source, guard, result, $"when {guard} => {result}");
    }

    public static Arbitrary<SwitchGuardScenario> ArbitrarySwitchGuardScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Switch-Arm Guard Never Captures a Trailing Lambda
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SwitchGuardLambdaPrecedencePropertyTests) })]
    public Property GuardAndResultSplitCorrectly_GuardIsNotALambda(SwitchGuardScenario scenario)
    {
        var cu = ParseOrFail(scenario.Source);
        var arm = SingleGuardedArm(cu);

        // Condition is typed VoidCoalescingNode, so non-null is enough.
        var guard = arm.Guard!;
        var conditionIsVoidCoalescing = guard.Condition is not null;

        var noLambdaInGuard = FindFirst<LambdaExpressionNode>(guard) is null;

        var guardText = NormalizeWs(SpanText(scenario.Source, guard.Condition!.Span));
        var guardMatches = guardText == NormalizeWs(scenario.ExpectedGuard);

        var resultText = NormalizeWs(SpanText(scenario.Source, arm.Result.Span));
        var resultMatches = resultText == NormalizeWs(scenario.ExpectedResult);
        var resultIsNotLambda = arm.Result is AssignmentFormExpressionNode;

        var passed = conditionIsVoidCoalescing
            && noLambdaInGuard
            && guardMatches
            && resultMatches
            && resultIsNotLambda;

        return passed.ToProperty().Label(
            $"Scenario: {scenario.Description}\n" +
            $"Source:\n{scenario.Source}\n" +
            $"conditionIsVoidCoalescing={conditionIsVoidCoalescing}\n" +
            $"noLambdaInGuard={noLambdaInGuard}\n" +
            $"guardText='{guardText}' expected='{NormalizeWs(scenario.ExpectedGuard)}' (match={guardMatches})\n" +
            $"resultText='{resultText}' expected='{NormalizeWs(scenario.ExpectedResult)}' (match={resultMatches})\n" +
            $"resultIsNotLambda={resultIsNotLambda}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Explicit regression cases (the defect this property guards against)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Regression_BareIdentifierGuard_DoesNotSwallowTrailingLambda()
    {
        var source = Wrap("int n when predicate => n.ToString(), _ => \"none\"");
        var cu = ParseOrFail(source);
        var arm = SingleGuardedArm(cu);

        _ = Assert.IsType<VoidCoalescingNode>(arm.Guard!.Condition);
        Assert.Null(FindFirst<LambdaExpressionNode>(arm.Guard!));
        Assert.Equal("predicate", NormalizeWs(SpanText(source, arm.Guard!.Condition.Span)));
        Assert.Equal("n.ToString()", NormalizeWs(SpanText(source, arm.Result.Span)));
        _ = Assert.IsType<AssignmentFormExpressionNode>(arm.Result);
    }

    [Fact]
    public void Regression_ComparisonGuard_SplitsCorrectly()
    {
        var source = Wrap("int n when n > 7 => n.ToString(), _ => \"none\"");
        var cu = ParseOrFail(source);
        var arm = SingleGuardedArm(cu);

        _ = Assert.IsType<VoidCoalescingNode>(arm.Guard!.Condition);
        Assert.Null(FindFirst<LambdaExpressionNode>(arm.Guard!));
        Assert.Equal("n > 7", NormalizeWs(SpanText(source, arm.Guard!.Condition.Span)));
        Assert.Equal("n.ToString()", NormalizeWs(SpanText(source, arm.Result.Span)));
    }

    [Fact]
    public void Regression_InvokedPredicateGuard_SplitsCorrectly()
    {
        var source = Wrap("int n when isOdd(n) => n.ToString(), _ => \"none\"");
        var cu = ParseOrFail(source);
        var arm = SingleGuardedArm(cu);

        _ = Assert.IsType<VoidCoalescingNode>(arm.Guard!.Condition);
        Assert.Null(FindFirst<LambdaExpressionNode>(arm.Guard!));
        Assert.Equal("isOdd(n)", NormalizeWs(SpanText(source, arm.Guard!.Condition.Span)));
        Assert.Equal("n.ToString()", NormalizeWs(SpanText(source, arm.Result.Span)));
    }
}

public sealed class SwitchGuardScenario
{
    public string Source { get; }

    public string ExpectedGuard { get; }

    public string ExpectedResult { get; }

    public string Description { get; }

    public SwitchGuardScenario(string source, string expectedGuard, string expectedResult, string description)
    {
        Source = source;
        ExpectedGuard = expectedGuard;
        ExpectedResult = expectedResult;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}
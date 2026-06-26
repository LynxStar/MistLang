// Feature: first-class-functions, Property 8: Switch-arm guard never captures a trailing lambda
// **Validates: Requirements 12.5**

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
/// Property-based tests for <b>Property 8: Switch-Arm Guard Never Captures a Trailing Lambda</b>.
///
/// <para><b>Validates: Requirements 12.5</b></para>
///
/// <para>
/// Because lambda is re-anchored at the lowest precedence (a peer of assignment at the
/// <c>Expression</c> level) and the switch-arm <c>when</c> guard is bounded to the
/// <c>VoidCoalescing</c> rule, the guard can never reach the lambda alternative. The arm's
/// <c>=&gt;</c> (<c>SUCH_THAT</c>) token is therefore always left for the <c>SwitchArm</c> rule
/// to consume rather than being swallowed by a trailing single-parameter lambda.
/// </para>
///
/// <para>
/// For any switch arm of the form <c>pattern when &lt;VoidCoalescing-level guard&gt; =&gt; &lt;result&gt;</c>,
/// the parser MUST:
/// </para>
/// <list type="number">
///   <item>parse the guard condition as the generated guard expression (not as a lambda),</item>
///   <item>leave the arm's <c>=&gt;</c> for the arm so <c>Result</c> is the post-<c>=&gt;</c> expression,</item>
///   <item>never produce a <see cref="LambdaExpressionNode"/> anywhere inside the guard condition
///         (structurally guaranteed because <c>WhenGuardNode.Condition</c> is a
///         <see cref="VoidCoalescingNode"/>, which cannot be a lambda).</item>
/// </list>
///
/// <para>
/// These tests are parse-only and run fully in-process via <see cref="Mist.Lexing.MistLexer"/> and
/// <see cref="Mist.Parsing.MistParser"/> — no <c>dotnet</c> process is spawned.
/// </para>
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

    /// <summary>Walks the syntax tree and returns the first node of type <typeparamref name="T"/>, or null.</summary>
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

    /// <summary>Walks the syntax tree and returns every node of type <typeparamref name="T"/>.</summary>
    private static List<T> FindAll<T>(SyntaxNode root) where T : SyntaxNode
    {
        var results = new List<T>();

        void Walk(SyntaxNode n)
        {
            if(n is T match)
            {
                results.Add(match);
            }

            foreach(var child in n.Children())
            {
                if(child is not null)
                {
                    Walk(child);
                }
            }
        }

        Walk(root);
        return results;
    }

    /// <summary>Extracts the source text covered by a node's span.</summary>
    private static string SpanText(string source, Storm.Runtime.SourceSpan span) =>
        source.Substring(span.Start, span.Length);

    /// <summary>Collapses runs of whitespace to a single space and trims.</summary>
    private static string NormalizeWs(string s) =>
        string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Wraps a switch-expression body in a complete, parseable Mist program. Names referenced by
    /// the scrutinee/guard/result need not be defined — these tests only lex and parse, never
    /// resolve or type-check.
    /// </summary>
    private static string Wrap(string arms) =>
        "namespace TestNs;\n\npublic class Program\n{\n    public static void Test()\n    {\n        "
        + $"var r = value switch {{ {arms} }};\n        var __use = r;"
        + "\n    }\n}";

    /// <summary>
    /// Locates the single guarded arm in a parsed program, asserting there is exactly one.
    /// </summary>
    private static SwitchArmNode SingleGuardedArm(CompilationUnitNode cu)
    {
        var guarded = FindAll<SwitchArmNode>(cu).Where(a => a.Guard is not null).ToList();
        Assert.Single(guarded);
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
    private static Gen<string> GenBareIdentifierGuard() =>
        Gen.Elements(Identifiers);

    /// <summary>A relational/equality comparison (lives in the <c>Relational</c>/<c>Equality</c> tier).</summary>
    private static Gen<string> GenComparisonGuard() =>
        from id in Gen.Elements(Identifiers)
        from op in Gen.Elements(">", "<", ">=", "<=", "==", "!=")
        from v in Gen.Choose(0, 100)
        select $"{id} {op} {v}";

    /// <summary>A method/predicate invocation (lives in the <c>Postfix</c> tier — the idiomatic guard).</summary>
    private static Gen<string> GenInvocationGuard() =>
        from fn in Gen.Elements(FunctionNames)
        from arg in Gen.Elements(Identifiers)
        select $"{fn}({arg})";

    /// <summary>A logical-and/or expression.</summary>
    private static Gen<string> GenLogicalGuard() =>
        from a in Gen.Elements(Identifiers)
        from op in Gen.Elements("&&", "||")
        from b in Gen.Elements(Identifiers)
        select $"{a} {op} {b}";

    /// <summary>A unary negation.</summary>
    private static Gen<string> GenNegationGuard() =>
        from id in Gen.Elements(Identifiers)
        select $"!{id}";

    /// <summary>A member access (lives in the <c>Postfix</c> tier).</summary>
    private static Gen<string> GenMemberAccessGuard() =>
        from obj in Gen.Elements(Identifiers)
        from m in Gen.Elements(MemberNames)
        select $"{obj}.{m}";

    /// <summary>A null-coalescing expression — the top of the bounded <c>VoidCoalescing</c> rule.</summary>
    private static Gen<string> GenCoalesceGuard() =>
        from a in Gen.Elements(Identifiers)
        from b in Gen.Elements(Identifiers)
        select $"{a} ?? {b}";

    private static Gen<string> GenGuard() =>
        Gen.OneOf(
            // Bias toward the bare-identifier case (the regression target) by listing it twice.
            GenBareIdentifierGuard(),
            GenBareIdentifierGuard(),
            GenComparisonGuard(),
            GenInvocationGuard(),
            GenLogicalGuard(),
            GenNegationGuard(),
            GenMemberAccessGuard(),
            GenCoalesceGuard());

    public static Gen<SwitchGuardScenario> GenScenario() =>
        from guard in GenGuard()
        from result in Gen.Elements(Results)
        from defaultResult in Gen.Elements(Results)
        let arms = $"int n when {guard} => {result}, _ => {defaultResult}"
        let source = Wrap(arms)
        select new SwitchGuardScenario(source, guard, result, $"when {guard} => {result}");

    public static Arbitrary<SwitchGuardScenario> ArbitrarySwitchGuardScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Switch-Arm Guard Never Captures a Trailing Lambda
    //
    // **Validates: Requirements 12.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SwitchGuardLambdaPrecedencePropertyTests) })]
    public Property GuardAndResultSplitCorrectly_GuardIsNotALambda(SwitchGuardScenario scenario)
    {
        var cu = ParseOrFail(scenario.Source);
        var arm = SingleGuardedArm(cu);

        // 1. The guard condition is a VoidCoalescing node (structurally cannot be a lambda).
        var conditionIsVoidCoalescing = arm.Guard!.Condition is VoidCoalescingNode;

        // 2. No lambda node appears anywhere inside the guard subtree.
        var noLambdaInGuard = FindFirst<LambdaExpressionNode>(arm.Guard!) is null;

        // 3. The guard condition text matches the generated guard (the guard did not over- or under-read).
        var guardText = NormalizeWs(SpanText(scenario.Source, arm.Guard!.Condition.Span));
        var guardMatches = guardText == NormalizeWs(scenario.ExpectedGuard);

        // 4. The arm's '=>' was claimed by the arm: Result is the post-'=>' expression we generated,
        //    and it is the assignment-form expression (not a lambda).
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
        // value switch { int n when predicate => n.ToString(), _ => "none" }
        // The previously-broken case: with lambda in Primary, `predicate => n.ToString()`
        // was parsed as a single-param lambda *inside* the guard, stranding the arm.
        var source = Wrap("int n when predicate => n.ToString(), _ => \"none\"");
        var cu = ParseOrFail(source);
        var arm = SingleGuardedArm(cu);

        Assert.IsType<VoidCoalescingNode>(arm.Guard!.Condition);
        Assert.Null(FindFirst<LambdaExpressionNode>(arm.Guard!));
        Assert.Equal("predicate", NormalizeWs(SpanText(source, arm.Guard!.Condition.Span)));
        Assert.Equal("n.ToString()", NormalizeWs(SpanText(source, arm.Result.Span)));
        Assert.IsType<AssignmentFormExpressionNode>(arm.Result);
    }

    [Fact]
    public void Regression_ComparisonGuard_SplitsCorrectly()
    {
        var source = Wrap("int n when n > 7 => n.ToString(), _ => \"none\"");
        var cu = ParseOrFail(source);
        var arm = SingleGuardedArm(cu);

        Assert.IsType<VoidCoalescingNode>(arm.Guard!.Condition);
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

        Assert.IsType<VoidCoalescingNode>(arm.Guard!.Condition);
        Assert.Null(FindFirst<LambdaExpressionNode>(arm.Guard!));
        Assert.Equal("isOdd(n)", NormalizeWs(SpanText(source, arm.Guard!.Condition.Span)));
        Assert.Equal("n.ToString()", NormalizeWs(SpanText(source, arm.Result.Span)));
    }
}

/// <summary>
/// A generated switch-arm scenario carrying the full program source plus the expected
/// guard and result fragments for the single guarded arm.
/// </summary>
public sealed class SwitchGuardScenario
{
    /// <summary>The complete, parseable Mist program containing the guarded switch arm.</summary>
    public string Source { get; }

    /// <summary>The expected guard-condition text (what should appear before the arm's <c>=&gt;</c>).</summary>
    public string ExpectedGuard { get; }

    /// <summary>The expected result text (what should appear after the arm's <c>=&gt;</c>).</summary>
    public string ExpectedResult { get; }

    /// <summary>Human-readable description of the scenario.</summary>
    public string Description { get; }

    public SwitchGuardScenario(string source, string expectedGuard, string expectedResult, string description)
    {
        Source = source;
        ExpectedGuard = expectedGuard;
        ExpectedResult = expectedResult;
        Description = description;
    }

    public override string ToString() => Description;
}
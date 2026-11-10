// Feature: first-class-functions, Property 9: Lambda is lowest-precedence and right-associative
// **Validates: Requirements 12.5, 4.1**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public class LambdaPrecedencePropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Generators ──────────────────────────────────────────────────────────

    public static Arbitrary<LambdaAssignmentRhsScenario> ArbitraryLambdaAssignmentRhsScenario()
    {
        return GenAssignmentRhs().ToArbitrary();
    }

    public static Arbitrary<LambdaRightNestScenario> ArbitraryLambdaRightNestScenario()
    {
        return GenRightNest().ToArbitrary();
    }

    private static readonly string[] Identifiers =
        ["a", "b", "c", "f", "g", "x", "y", "z", "foo", "bar"];

    private static readonly string[] BodyExpressions =
        ["1", "42", "x + 1", "a * 2", "x + y"];

    private static readonly string[] Modifiers =
        ["", "static ", "async ", "static async "];

    private static Gen<string> GenSingleLambda()
    {
        var paramForms = new[]
        {
            Gen.Elements(Identifiers),
            Gen.Elements(Identifiers).Select(n => $"({n})"),
            from n1 in Gen.Elements(Identifiers)
            from n2 in Gen.Elements(Identifiers)
            select $"({n1}, {n2})",
            Gen.Constant("()"),
            Gen.Elements(Identifiers).Select(n => $"(int {n})"),
        };

        return from modifier in Gen.Elements(Modifiers)
               from paramForm in Gen.OneOf(paramForms)
               from body in Gen.Elements(BodyExpressions)
               select $"{modifier}{paramForm} => {body}";
    }

    private static Gen<LambdaAssignmentRhsScenario> GenAssignmentRhs()
    {
        return from target in Gen.Elements(Identifiers)
               from lambda in GenSingleLambda()
               let source = $"{target} = {lambda};"
               select new LambdaAssignmentRhsScenario(source);
    }

    private static Gen<LambdaRightNestScenario> GenRightNest()
    {
        return from depth in Gen.Choose(2, 5)
               from body in Gen.Elements(BodyExpressions)
               let parameters = Enumerable.Range(0, depth).Select(i => $"p{i}").ToArray()
               let source = string.Join(" => ", parameters) + " => " + body + ";"
               select new LambdaRightNestScenario(source, depth);
    }

    // ─── Parsing helpers ──────────────────────────────────────────────────────

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
            ?? throw new InvalidOperationException($"Parse result root is not a CompilationUnitNode.\nSource:\n{source}");
    }

    private static ExpressionNode TopLevelExpression(string source)
    {
        var cu = ParseOrFail(source);

        if(cu.Items.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one top-level item, got {cu.Items.Length}.\nSource:\n{source}");
        }

        if(cu.Items[0] is not StatementItemTopLevelItemNode statementItem)
        {
            throw new InvalidOperationException(
                $"Top-level item is not a statement: {cu.Items[0].GetType().Name}.\nSource:\n{source}");
        }

        if(statementItem.Statement is not ExpressionStatementStatementNode expressionStatement)
        {
            throw new InvalidOperationException(
                $"Statement is not an expression statement: {statementItem.Statement.GetType().Name}.\nSource:\n{source}");
        }

        return expressionStatement.ExpressionStatement.Expression;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9a: Lambda binds looser than `=` (lowest precedence).
    //
    // For `f = <lambda>`, the top-level expression is the assignment form whose
    // RHS value is the lambda — `f = (L)` — and is never the lambda form wrapping
    // the assignment — `(f = ...) => ...`.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LambdaPrecedencePropertyTests) })]
    public Property Lambda_OnAssignmentRhs_BindsLooserThanEquals(LambdaAssignmentRhsScenario scenario)
    {
        var top = TopLevelExpression(scenario.Source);

        if(top.AsLambdaOrNull() is not null)
        {
            return false.ToProperty()
                .Label($"Top-level expression parsed as a LAMBDA form (lambda bound tighter than `=`): `(f = ...) => ...`.\nSource:\n{scenario.Source}");
        }

        var assignment = top.AsAssignmentOrNull();
        if(assignment is null)
        {
            return false.ToProperty()
                .Label($"Top-level expression is neither lambda nor assignment form: {top.GetType().Name}.\nSource:\n{scenario.Source}");
        }

        if(!(assignment.Group0?.Operator).HasValue)
        {
            return false.ToProperty()
                .Label($"Top-level expression is an assignment with no operator (not an `=`).\nSource:\n{scenario.Source}");
        }

        var rhsIsLambda = assignment.Group0?.Value.AsLambdaOrNull() is not null;

        return rhsIsLambda.ToProperty()
            .Label($"Assignment RHS is not a lambda form (lambda did not bind as the RHS value): {assignment.Group0?.Value.GetType().Name}.\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9b: Lambda is right-associative.
    //
    // For `p0 => p1 => ... => body`, the outer lambda's body is itself a lambda,
    // nesting rightward exactly `depth` times, with the innermost body being a
    // non-lambda expression: `p0 => (p1 => (... => body))`.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LambdaPrecedencePropertyTests) })]
    public Property Lambda_NestedChain_IsRightAssociative(LambdaRightNestScenario scenario)
    {
        var top = TopLevelExpression(scenario.Source);

        var current = top;
        var peeled = 0;

        while(current.AsLambdaOrNull() is { } lambda)
        {
            if(lambda is not ExpressionLambdaLambdaExpressionNode expressionLambda)
            {
                return false.ToProperty()
                    .Label($"Encountered a non-expression-bodied lambda at depth {peeled}: {lambda.GetType().Name}.\nSource:\n{scenario.Source}");
            }

            current = expressionLambda.Body;
            peeled++;
        }

        var rightNested = peeled == scenario.Depth && current.AsLambdaOrNull() is null;

        return rightNested.ToProperty()
            .Label($"Expected {scenario.Depth} right-nested lambdas, peeled {peeled}; innermost body lambda? {current.AsLambdaOrNull() is not null}.\nSource:\n{scenario.Source}");
    }

    // ─── Canonical regression anchors (the exact examples from design §3) ──────

    [Fact]
    public void Canonical_AssignmentRhs_FEqualsLambda_ParsesAsAssignmentWithLambdaValue()
    {
        var top = TopLevelExpression("f = x => x + 1;");

        Assert.Null(top.AsLambdaOrNull());
        var assignment = top.AsAssignmentOrNull();
        Assert.NotNull(assignment);
        Assert.True((assignment!.Group0?.Operator).HasValue);
        Assert.NotNull(assignment.Group0?.Value.AsLambdaOrNull());
    }

    [Fact]
    public void Canonical_NestedLambda_ParsesRightAssociatively()
    {
        var top = TopLevelExpression("x => y => x + y;");

        var outer = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(top.AsLambdaOrNull());
        var inner = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(outer.Body.AsLambdaOrNull());
        Assert.Null(inner.Body.AsLambdaOrNull());
    }
}

public sealed class LambdaAssignmentRhsScenario
{
    public string Source { get; }

    public LambdaAssignmentRhsScenario(string source)
    {
        Source = source;
    }

    public override string ToString()
    {
        return Source;
    }
}

public sealed class LambdaRightNestScenario
{
    public string Source { get; }

    public int Depth { get; }

    public LambdaRightNestScenario(string source, int depth)
    {
        Source = source;
        Depth = depth;
    }

    public override string ToString()
    {
        return $"(depth={Depth}) {Source}";
    }
}
// Feature: first-class-functions, Property 9: Lambda is lowest-precedence and right-associative
// **Validates: Requirements 12.5, 4.1**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for <b>Property 9: Lambda Is Lowest-Precedence and Right-Associative</b>.
///
/// <para><b>Validates: Requirements 12.5, 4.1</b></para>
///
/// <para>
/// The lambda form sits at the lowest binding precedence — a peer of, but looser than,
/// assignment (design §3, Requirement 12.5). Two structural consequences are asserted:
/// </para>
/// <list type="number">
///   <item>
///     <b>Lowest precedence (looser than <c>=</c>).</b> For any <c>f = &lt;lambda&gt;</c>, the
///     top-level expression is the <i>assignment</i> form whose right-hand value is the lambda
///     (<c>f = (L)</c>), and is <i>never</i> a lambda form wrapping the assignment
///     (<c>(f = …) =&gt; …</c>).
///   </item>
///   <item>
///     <b>Right-associative.</b> For a chain <c>p0 =&gt; p1 =&gt; … =&gt; body</c>, the outer
///     lambda's body is itself a lambda, nesting rightward (<c>p0 =&gt; (p1 =&gt; (… =&gt; body))</c>).
///   </item>
/// </list>
///
/// <para>These tests are parse-only and fully in-process (no <c>dotnet</c> process spawning).</para>
/// </summary>
public class LambdaPrecedencePropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Generators ──────────────────────────────────────────────────────────

    public static Arbitrary<LambdaAssignmentRhsScenario> ArbitraryLambdaAssignmentRhsScenario() =>
        GenAssignmentRhs().ToArbitrary();

    public static Arbitrary<LambdaRightNestScenario> ArbitraryLambdaRightNestScenario() =>
        GenRightNest().ToArbitrary();

    private static readonly string[] Identifiers =
        ["a", "b", "c", "f", "g", "x", "y", "z", "foo", "bar"];

    private static readonly string[] BodyExpressions =
        ["1", "42", "x + 1", "a * 2", "x + y"];

    private static readonly string[] Modifiers =
        ["", "static ", "async ", "static async "];

    /// <summary>
    /// Generates the source text of a single expression-bodied lambda with a random
    /// parameter form (single implicit, parenthesized single/multi, zero, typed) and
    /// an optional <c>static</c>/<c>async</c> modifier prefix.
    /// </summary>
    private static Gen<string> GenSingleLambda()
    {
        var paramForms = new[]
        {
            // single implicit param: x
            Gen.Elements(Identifiers),
            // parenthesized single param: (x)
            Gen.Elements(Identifiers).Select(n => $"({n})"),
            // parenthesized multi param: (x, y)
            from n1 in Gen.Elements(Identifiers)
            from n2 in Gen.Elements(Identifiers)
            select $"({n1}, {n2})",
            // zero params: ()
            Gen.Constant("()"),
            // typed single param: (int x)
            Gen.Elements(Identifiers).Select(n => $"(int {n})"),
        };

        return from modifier in Gen.Elements(Modifiers)
               from paramForm in Gen.OneOf(paramForms)
               from body in Gen.Elements(BodyExpressions)
               select $"{modifier}{paramForm} => {body}";
    }

    /// <summary>
    /// Generates a <c>target = &lt;lambda&gt;;</c> top-level statement, exercising the claim
    /// that lambda binds looser than <c>=</c> (the lambda is the assignment's RHS value).
    /// </summary>
    private static Gen<LambdaAssignmentRhsScenario> GenAssignmentRhs() =>
        from target in Gen.Elements(Identifiers)
        from lambda in GenSingleLambda()
        let source = $"{target} = {lambda};"
        select new LambdaAssignmentRhsScenario(source);

    /// <summary>
    /// Generates a right-nesting chain <c>p0 =&gt; p1 =&gt; … =&gt; body;</c> of depth N ∈ [2, 5]
    /// (each link an expression-bodied, single-implicit-param lambda) as a top-level statement.
    /// </summary>
    private static Gen<LambdaRightNestScenario> GenRightNest() =>
        from depth in Gen.Choose(2, 5)
        from body in Gen.Elements(BodyExpressions)
        let parameters = Enumerable.Range(0, depth).Select(i => $"p{i}").ToArray()
        let source = string.Join(" => ", parameters) + " => " + body + ";"
        select new LambdaRightNestScenario(source, depth);

    // ─── Parsing helpers ──────────────────────────────────────────────────────

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
            ?? throw new InvalidOperationException($"Parse result root is not a CompilationUnitNode.\nSource:\n{source}");
    }

    /// <summary>
    /// Navigates to the single top-level <see cref="ExpressionNode"/> produced by parsing
    /// a one-statement expression-statement program.
    /// </summary>
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
    // **Validates: Requirements 12.5, 4.1**
    //
    // For `f = <lambda>`, the top-level expression is the assignment form whose
    // RHS value is the lambda — `f = (L)` — and is never the lambda form wrapping
    // the assignment — `(f = ...) => ...`.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LambdaPrecedencePropertyTests) })]
    public Property Lambda_OnAssignmentRhs_BindsLooserThanEquals(LambdaAssignmentRhsScenario scenario)
    {
        var top = TopLevelExpression(scenario.Source);

        // The top-level expression must be the ASSIGNMENT form, never the lambda form.
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

        // The assignment must actually carry the `=` operator (it is `f = <rhs>`).
        if(!assignment.Operator.HasValue)
        {
            return false.ToProperty()
                .Label($"Top-level expression is an assignment with no operator (not an `=`).\nSource:\n{scenario.Source}");
        }

        // The assignment's RHS value must be the lambda form — `f = (L)`.
        var rhsIsLambda = assignment.Value.AsLambdaOrNull() is not null;

        return rhsIsLambda.ToProperty()
            .Label($"Assignment RHS is not a lambda form (lambda did not bind as the RHS value): {assignment.Value.GetType().Name}.\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9b: Lambda is right-associative.
    //
    // **Validates: Requirements 12.5, 4.1**
    //
    // For `p0 => p1 => ... => body`, the outer lambda's body is itself a lambda,
    // nesting rightward exactly `depth` times, with the innermost body being a
    // non-lambda expression: `p0 => (p1 => (... => body))`.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LambdaPrecedencePropertyTests) })]
    public Property Lambda_NestedChain_IsRightAssociative(LambdaRightNestScenario scenario)
    {
        var top = TopLevelExpression(scenario.Source);

        // Peel expression-bodied lambdas from the outside in, counting the nesting depth.
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

        // The chain must nest exactly `depth` lambdas (right-associative), and the
        // innermost body must be a non-lambda expression.
        var rightNested = peeled == scenario.Depth && current.AsLambdaOrNull() is null;

        return rightNested.ToProperty()
            .Label($"Expected {scenario.Depth} right-nested lambdas, peeled {peeled}; innermost body lambda? {current.AsLambdaOrNull() is not null}.\nSource:\n{scenario.Source}");
    }

    // ─── Canonical regression anchors (the exact examples from design §3) ──────

    [Fact]
    public void Canonical_AssignmentRhs_FEqualsLambda_ParsesAsAssignmentWithLambdaValue()
    {
        // `f = x => x + 1` must parse as `f = (x => x + 1)`.
        var top = TopLevelExpression("f = x => x + 1;");

        Assert.Null(top.AsLambdaOrNull());
        var assignment = top.AsAssignmentOrNull();
        Assert.NotNull(assignment);
        Assert.True(assignment!.Operator.HasValue);
        Assert.NotNull(assignment.Value.AsLambdaOrNull());
    }

    [Fact]
    public void Canonical_NestedLambda_ParsesRightAssociatively()
    {
        // `x => y => x + y` must parse as `x => (y => (x + y))`.
        var top = TopLevelExpression("x => y => x + y;");

        var outer = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(top.AsLambdaOrNull());
        var inner = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(outer.Body.AsLambdaOrNull());
        // Innermost body is the plain expression `x + y`, not a lambda.
        Assert.Null(inner.Body.AsLambdaOrNull());
    }
}

/// <summary>
/// A generated <c>target = &lt;lambda&gt;;</c> top-level statement for the
/// lambda-lowest-precedence property.
/// </summary>
public sealed class LambdaAssignmentRhsScenario
{
    public string Source { get; }

    public LambdaAssignmentRhsScenario(string source)
    {
        Source = source;
    }

    public override string ToString() => Source;
}

/// <summary>
/// A generated right-nesting lambda chain <c>p0 =&gt; … =&gt; body;</c> of the given
/// <see cref="Depth"/> for the lambda-right-associativity property.
/// </summary>
public sealed class LambdaRightNestScenario
{
    public string Source { get; }

    public int Depth { get; }

    public LambdaRightNestScenario(string source, int depth)
    {
        Source = source;
        Depth = depth;
    }

    public override string ToString() => $"(depth={Depth}) {Source}";
}
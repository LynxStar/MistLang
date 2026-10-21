using System.Text.RegularExpressions;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class RefinementFlowRefusalTests
{
    [Theory]
    [InlineData("getter-over-a-mutable-field-lifts-nothing", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("inequality-does-not-prove-a-positive-local", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("inequality-local-refinement-lost-on-write", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("lifted-getter-violates-refinement", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("lifted-return-violates-refinement", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("lifted-value-copied-into-a-local-violates-refinement", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("local-aliased-by-a-ref-local-unproven", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("local-passed-by-ref-to-a-constructor-unproven", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("local-passed-by-ref-unproven", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("local-passed-to-a-clr-out-parameter-unproven", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("local-read-through-a-ref-local-unproven", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("local-written-by-a-lambda-unproven", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("local-written-by-a-local-function-unproven", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("local-written-by-an-async-block-unproven", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("narrowing-does-not-reach-else-branch", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("narrowing-lost-on-reassignment", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-flow-arithmetic-is-not-a-proof", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-flow-float-rounded-bound-is-not-a-double-bound", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-flow-inclusive-guard-does-not-prove-strict-endpoint", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-flow-insufficient-upper-guard", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-flow-join-includes-a-violating-path", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-flow-negated-lower-order-admits-nan", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-flow-negated-order-admits-nan", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-flow-reassignment-loses-a-guard", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-flow-refined-arithmetic-cannot-reuse-identity", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-flow-refined-cast-cannot-reuse-identity", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-flow-symbolic-bound-is-not-a-proof", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-flow-unseen-write-loses-a-guard", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-inclusive-guard-does-not-prove-positive", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-literal-lost-in-a-closure-write", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-negation-does-not-preserve-a-refinement", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("recursive-return-lifts-nothing", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("regex-literal-lost-after-a-local-write", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("return-the-flow-narrowed-lifts-nothing", "MIST_PA_REFINEMENT_UNPROVEN")]
    public void FlowFactsMustActuallyProveTheRequiredValue(string name, string expected)
    {
        var h = new HandBuiltRefinements();
        var real = name.StartsWith("real", StringComparison.Ordinal);
        IMistType target = real ? h.Refine(h.Double, ComparisonKind.GreaterThan, 0.0, "Positive")
            : h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 18L, "AdultAge");
        var local = HandBuiltRefinements.Local(real ? h.Double : h.Int, "input");
        Expression value = local;
        Expression? guard = null;
        var statements = new List<Statement>();
        var inside = new List<Statement>();
        switch(name)
        {
            case "narrowing-does-not-reach-else-branch":
                statements.Add(new IfStatement(h.Binary(local, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(18L)), HandBuiltModel.Block(),
                    HandBuiltModel.Block(HandBuiltRefinements.Need(local, target))));
                Assert.Equal(expected, Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block([.. statements]))).Id.Value); return;
            case "narrowing-lost-on-reassignment":
                guard = h.Binary(local, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(18L));
                inside.Add(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(local, HandBuiltRefinements.Local(h.Int, "replacement")))); break;
            case "inequality-does-not-prove-a-positive-local":
                target = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
                guard = h.Binary(local, BinaryOperatorKind.NotEqual, h.Literal(0L)); break;
            case "inequality-local-refinement-lost-on-write":
                target = HandBuiltRefinements.RefinePattern(h.Int, new NegatedPattern(HandBuiltModel.Node(), h.Int, h.Compare(h.Int, ComparisonKind.Equal, 0L)), "NonZero");
                guard = h.Binary(local, BinaryOperatorKind.NotEqual, h.Literal(0L));
                inside.Add(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(local, HandBuiltRefinements.Local(h.Int, "replacement")))); break;
            case "local-written-by-a-lambda-unproven":
            case "local-written-by-a-local-function-unproven":
            case "local-written-by-an-async-block-unproven":
            case "real-literal-lost-in-a-closure-write":
                statements.Add(HandBuiltRefinements.Give(local, real ? h.Literal(2.5) : h.Literal(30L)));
                var nested = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(local, real ? h.Literal(-1.0) : h.Literal(1L))));
                if(name.Contains("local-function", StringComparison.Ordinal))
                {
                    statements.Add(new LocalFunctionStatement(new Method("Lower", h.Int, [], new MemberModifiers()) { Body = nested }, []));
                }
                else if(name.Contains("async", StringComparison.Ordinal))
                {
                    statements.Add(HandBuiltRefinements.Evaluate(new AsyncBlockExpression(HandBuiltModel.Node(), nested, h.Int, false)));
                }
                else { statements.Add(HandBuiltRefinements.Evaluate(new LambdaExpression(HandBuiltModel.Node(), [], null, nested, false, h.Int))); }

                statements.Add(HandBuiltRefinements.Evaluate(new InvocationExpression(HandBuiltModel.Node(), h.Literal(0L), [], h.Int, null, []))); break;
            case "local-aliased-by-a-ref-local-unproven":
            case "local-read-through-a-ref-local-unproven":
                var alias = new LocalVariable("alias", h.Int) { IsRef = true };
                var read = HandBuiltRefinements.Read(alias);
                statements.Add(HandBuiltRefinements.Give(local, h.Literal(name.Contains("read-through", StringComparison.Ordinal) ? 5L : 30L)));
                statements.Add(new LocalDeclarationStatement(alias, local));
                statements.Add(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(read, h.Literal(name.Contains("read-through", StringComparison.Ordinal) ? 30L : 1L))));
                if(name.Contains("read-through", StringComparison.Ordinal))
                {
                    statements.Add(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(local, h.Literal(1L)))); value = read;
                }

                break;
            case "local-passed-by-ref-to-a-constructor-unproven":
            case "local-passed-by-ref-unproven":
            case "local-passed-to-a-clr-out-parameter-unproven":
                statements.Add(HandBuiltRefinements.Give(local, h.Literal(30L)));
                var parameter = new Parameter("value", h.Int, new ParameterModifier(false, true, default,
                    name.Contains("clr-out", StringComparison.Ordinal) ? ByRefDirection.Out : ByRefDirection.Ref));
                if(name.Contains("constructor", StringComparison.Ordinal))
                {
                    var resetter = HandBuiltModel.Class("Resetter");
                    var constructor = new Constructor("Resetter", resetter, [parameter], new MemberModifiers());
                    statements.Add(HandBuiltRefinements.Evaluate(new NewExpression(HandBuiltModel.Node(), resetter, [local], constructor, [], [h.Int])));
                }
                else
                {
                    var resetMethod = new Method("Reset", h.Int, [parameter], new MemberModifiers());
                    statements.Add(HandBuiltRefinements.Evaluate(new InvocationExpression(HandBuiltModel.Node(), h.Literal(0L), [local], h.Int, resetMethod, [h.Int])));
                }

                break;
            case "real-inclusive-guard-does-not-prove-positive":
                guard = h.Binary(local, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(0.0)); break;
            case "real-flow-insufficient-upper-guard":
                target = h.Between(h.Double, 0.0, 1.0, "Unit");
                guard = h.Binary(local, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(0.0)); break;
            case "real-flow-inclusive-guard-does-not-prove-strict-endpoint":
                target = HandBuiltRefinements.RefinePattern(h.Double, new ConjunctionPattern(HandBuiltModel.Node(), h.Double,
                    [h.Compare(h.Double, ComparisonKind.GreaterThan, 0.0), h.Compare(h.Double, ComparisonKind.LessThan, 1.0)]), "StrictUnit");
                guard = h.And(h.Binary(local, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(0.0)), h.Binary(local, BinaryOperatorKind.LessThanOrEqual, h.Literal(1.0))); break;
            case "real-flow-negated-lower-order-admits-nan":
                guard = h.Not(h.Binary(local, BinaryOperatorKind.LessThanOrEqual, h.Literal(0.0))); break;
            case "real-flow-negated-order-admits-nan":
                target = h.Refine(h.Double, ComparisonKind.LessThanOrEqual, 0.0, "NonPositive");
                guard = h.Not(h.Binary(local, BinaryOperatorKind.GreaterThan, h.Literal(0.0))); break;
            case "real-flow-symbolic-bound-is-not-a-proof":
                guard = h.Binary(local, BinaryOperatorKind.GreaterThan, HandBuiltRefinements.Local(h.Double, "limit")); break;
            case "real-flow-float-rounded-bound-is-not-a-double-bound":
                local = HandBuiltRefinements.Local(h.Built.Type("Single"), "input"); value = local;
                target = h.Refine(h.Double, ComparisonKind.LessThanOrEqual, 0.1, "Tenth");
                guard = h.Binary(local, BinaryOperatorKind.LessThanOrEqual, h.Literal(0.1f)); break;
            case "real-flow-reassignment-loses-a-guard":
            case "real-flow-unseen-write-loses-a-guard":
                guard = h.Binary(local, BinaryOperatorKind.GreaterThan, h.Literal(0.0));
                var assignment = HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(local, HandBuiltRefinements.Local(h.Double, "other")));
                if(name.Contains("unseen", StringComparison.Ordinal))
                {
                    inside.Add(HandBuiltRefinements.Evaluate(new LambdaExpression(HandBuiltModel.Node(), [], null, HandBuiltModel.Block(assignment), false, h.Int)));
                }
                else { inside.Add(assignment); }

                break;
            case "real-flow-arithmetic-is-not-a-proof":
                guard = h.Binary(local, BinaryOperatorKind.GreaterThan, h.Literal(0.0));
                var sum = HandBuiltRefinements.Local(h.Double, "sum");
                inside.Add(HandBuiltRefinements.Give(sum, h.Binary(local, BinaryOperatorKind.Add, h.Literal(1.0), h.Double))); value = sum; break;
            case "real-flow-join-includes-a-violating-path":
                target = h.Between(h.Double, 0.0, 1.0, "Unit");
                statements.Add(HandBuiltRefinements.Give(local, h.Literal(0.25)));
                statements.Add(HandBuiltRefinements.If(HandBuiltRefinements.Local(h.Bool, "flag"), HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(local, h.Literal(1.5))))); break;
            case "real-flow-refined-arithmetic-cannot-reuse-identity":
            case "real-flow-refined-cast-cannot-reuse-identity":
                var first = HandBuiltRefinements.Local(target, "first");
                value = h.Binary(first, BinaryOperatorKind.Subtract, first, target);
                if(name.Contains("cast", StringComparison.Ordinal))
                {
                    statements.Add(HandBuiltRefinements.Evaluate(new CastExpression(HandBuiltModel.Node(), value, target, [], target)));
                    Assert.Equal(expected, Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block([.. statements]))).Id.Value); return;
                }

                break;
            case "real-negation-does-not-preserve-a-refinement":
                var positive = HandBuiltRefinements.Local(target, "first");
                statements.Add(HandBuiltRefinements.Give(positive, h.Literal(2.5)));
                value = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, positive, target); break;
            case "regex-literal-lost-after-a-local-write":
                target = h.Regex("[a-z]+", "Letters");
                var text = HandBuiltRefinements.Local(h.String, "text");
                statements.Add(HandBuiltRefinements.Give(text, h.Literal("abc")));
                statements.Add(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(text, h.Literal("123")))); value = text; break;
            case "getter-over-a-mutable-field-lifts-nothing":
            case "lifted-getter-violates-refinement":
            case "lifted-value-copied-into-a-local-violates-refinement":
                var owner = HandBuiltModel.Class("Ledger");
                var field = new Field("count", h.Int, default, new MemberModifiers()) { Initializer = h.Literal(20L) };
                var property = new Property("Count", h.Int, new MemberModifiers())
                {
                    Getter = new PropertyAccessor
                    {
                        Body = HandBuiltModel.Block(new ReturnStatement(
                        name == "getter-over-a-mutable-field-lifts-nothing" ? new FieldReferenceExpression(HandBuiltModel.Node(), field) : h.Literal(9L), h.Int))
                    },
                };
                value = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(owner), "Count", h.Int, member: new PropertyResult(property, owner, []));
                if(name.Contains("copied", StringComparison.Ordinal))
                {
                    statements.Add(HandBuiltRefinements.Give(local, value)); value = local;
                }

                break;
            case "lifted-return-violates-refinement":
            case "return-the-flow-narrowed-lifts-nothing":
            case "recursive-return-lifts-nothing":
                var body = HandBuiltModel.Block();
                var method = new Method("Make", h.Int, [], new MemberModifiers()) { Body = body };
                var call = new InvocationExpression(HandBuiltModel.Node(), h.Literal(0L), [], h.Int, method, []);
                body.Statements = name == "lifted-return-violates-refinement"
                    ? [new ReturnStatement(h.Literal(7L), h.Int)]
                    : name.StartsWith("recursive", StringComparison.Ordinal)
                    ? [HandBuiltRefinements.If(HandBuiltRefinements.Local(h.Bool), new ReturnStatement(h.Literal(20L), h.Int)), new ReturnStatement(call, h.Int)]
                    : [HandBuiltRefinements.Give(local, h.Literal(20L)), new ReturnStatement(local, h.Int)];

                value = call; break;
            default: throw new InvalidOperationException(name);
        }

        inside.Add(HandBuiltRefinements.Need(value, target));
        if(guard is null) { statements.AddRange(inside); }
        else { statements.Add(HandBuiltRefinements.If(guard, [.. inside])); }

        Assert.Equal(expected, Assert.Single(h.Built.Analyze(new RefinementAnalyzer(Regex.InfiniteMatchTimeout), HandBuiltModel.Block([.. statements]))).Id.Value);
    }

    [Fact]
    public void ALiteralWhoseMatchExceedsTheAnalyzersBudget_IsUnprovenRatherThanViolated()
    {
        var h = new HandBuiltRefinements();
        var text = HandBuiltRefinements.Local(h.String, "text");
        var body = HandBuiltModel.Block(
            HandBuiltRefinements.Give(text, h.Literal(new string('a', 64))), HandBuiltRefinements.Need(text, h.Regex("(a+)+b", "Catastrophic")));

        Assert.Equal("MIST_PA_REFINEMENT_UNPROVEN", Assert.Single(h.Built.Analyze(new RefinementAnalyzer(TimeSpan.FromMilliseconds(1)), body)).Id.Value);
    }
}
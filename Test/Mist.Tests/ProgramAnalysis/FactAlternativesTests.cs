using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class FactAlternativesTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(1)]
    public void DemoKeepsThePairingUntilTheCapMergesPaths(int cap)
    {
        var h = new HandBuiltRefinements();
        var flag = HandBuiltRefinements.Local(h.Bool, "flag");
        var a = HandBuiltRefinements.Local(h.Int, "a");
        var b = HandBuiltRefinements.Local(h.Int, "b");
        var limit = new Parameter("b", h.Int);
        var required = h.Refine(h.Int, ComparisonKind.LessThan, HandBuiltRefinements.Read(new LocalVariable("b", h.Int) { Parameter = limit }), null);
        var needs = new Method("Needs", h.Int, [new Parameter("a", required), limit], new MemberModifiers { IsReadonly = true });
        var call = new InvocationExpression(HandBuiltModel.Node(), h.Literal(0L), [a, b], h.Int, needs, [required, h.Int]);
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(a.Local, null), new LocalDeclarationStatement(b.Local, null), new IfStatement(flag,
            HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(a, h.Literal(1L))), HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(b, h.Literal(2L)))),
            HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(a, h.Literal(100L))), HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(b, h.Literal(200L))))),
            HandBuiltRefinements.Evaluate(call));

        var findings = h.Built.Analyze(new RefinementAnalyzer(cap), body);

        if(cap == 16)
        {
            Assert.Empty(findings);
        }
        else
        {
            var refusal = Assert.Single(findings);
            Assert.Equal("MIST0277", refusal.Number.Value);
            Assert.Contains("after merging paths", refusal.Message);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FillNeedsTheBoundBetweenNAndTheItemsLength(bool constrained)
    {
        var h = new HandBuiltRefinements();
        var items = HandBuiltRefinements.Local(h.Sequence("Span"), "items");
        var parameterType = constrained ? h.Refine(h.Int, ComparisonKind.LessThanOrEqual, h.Length(items), null) : h.Int;
        var n = HandBuiltRefinements.Read(new LocalVariable("n", parameterType) { Parameter = new Parameter("n", parameterType) });
        var i = HandBuiltRefinements.Local(h.Int, "i");
        var loop = new WhileStatement(h.Binary(i, BinaryOperatorKind.LessThan, n), HandBuiltModel.Block(
            HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(h.At(items, i), i)),
            HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(i, h.Binary(i, BinaryOperatorKind.Add, h.Literal(1L), h.Int)))));

        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(i, h.Literal(0L)), loop);
        var findings = h.Built.Analyze(new RefinementAnalyzer(), body);

        if(constrained)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal("MIST0275", Assert.Single(findings).Number.Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrConditionsReachTheProofAsSeparateAlternatives(bool safe)
    {
        var h = new HandBuiltRefinements();
        var a = HandBuiltRefinements.Local(h.Int, "a");
        var b = HandBuiltRefinements.Local(h.Int, "b");
        var first = h.And(h.Binary(a, BinaryOperatorKind.Equal, h.Literal(1L)), h.Binary(b, BinaryOperatorKind.Equal, h.Literal(2L)));
        var second = h.And(h.Binary(a, BinaryOperatorKind.Equal, h.Literal(100L)), h.Binary(b, BinaryOperatorKind.Equal, h.Literal(safe ? 200L : 2L)));
        var required = h.Refine(h.Int, ComparisonKind.LessThan, b);
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(h.Binary(first, BinaryOperatorKind.LogicalOr, second), HandBuiltRefinements.Need(a, required)));

        var findings = h.Built.Analyze(new RefinementAnalyzer(), body);

        if(safe)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal("MIST0272", Assert.Single(findings).Number.Value);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABooleanLocalSelectsItsInitializerAlternative(bool holds)
    {
        var h = new HandBuiltRefinements();
        var i = HandBuiltRefinements.Local(h.Int, "i");
        var n = HandBuiltRefinements.Local(h.Int, "n");
        var ok = HandBuiltRefinements.Local(h.Bool, "ok");
        var required = h.Refine(h.Int, holds ? ComparisonKind.LessThan : ComparisonKind.GreaterThanOrEqual, n);
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(ok, h.Binary(i, BinaryOperatorKind.LessThan, n)),
            HandBuiltRefinements.If(holds ? ok : h.Not(ok), HandBuiltRefinements.Need(i, required)));

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Fact]
    public void WideningMakesALaterIterationReachAnInitiallyImpossibleBranch()
    {
        var h = new HandBuiltRefinements();
        var i = HandBuiltRefinements.Local(h.Int, "i");
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(i, h.Literal(0L)),
            new WhileStatement(h.Binary(i, BinaryOperatorKind.LessThan, h.Literal(100L)), HandBuiltModel.Block(
                HandBuiltRefinements.If(h.Binary(i, BinaryOperatorKind.GreaterThan, h.Literal(10L)),
                    HandBuiltRefinements.Need(i, h.Refine(h.Int, ComparisonKind.LessThanOrEqual, 10L))),
                HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(i, h.Binary(i, BinaryOperatorKind.Add, h.Literal(1L), h.Int))))));

        Assert.Equal("MIST0272", Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), body)).Number.Value);
    }
}
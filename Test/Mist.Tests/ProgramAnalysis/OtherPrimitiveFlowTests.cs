using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class OtherPrimitiveFlowTests
{
    [Theory]
    [InlineData(0.0, false, true)]
    [InlineData(-1.0, false, false)]
    [InlineData(0.0, true, true)]
    [InlineData(-1.0, true, false)]
    public void ADoubleRefinedAboveABoundMeetsANonNegativeParameter(double bound, bool negatedTwice, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var x = Parameter(h.Refine(h.Double, ComparisonKind.GreaterThan, bound), "x");
        Expression value = negatedTwice ? Negated(h, Negated(h, x)) : x;
        var body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(h.Call(value, h.Refine(h.Double, ComparisonKind.GreaterThanOrEqual, 0.0))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HalfOfABoundedDoubleIsRecordedAndUsed(bool bounded)
    {
        var h = new HandBuiltRefinements();
        var x = Parameter(bounded ? h.Between(h.Double, 1.0, 1000.0) : h.Refine(h.Double, ComparisonKind.GreaterThanOrEqual, 1.0), "x");
        var half = HandBuiltRefinements.Local(h.Double, "half");
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(half, h.Binary(x, BinaryOperatorKind.Divide, h.Literal(2.0), h.Double)),
            HandBuiltRefinements.Evaluate(h.Call(half, h.Refine(h.Double, ComparisonKind.LessThanOrEqual, 500.0))));

        AssertAdmitted(bounded, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData("^[a-z]+$", true)]
    [InlineData("^[a-z]*$", false)]
    public void AStringNarrowedByARegexMeetsARegexParameter(string tested, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var s = Parameter(h.String, "s");
        var test = new IsPatternExpression(HandBuiltModel.Node(), s, new RegexPattern(HandBuiltModel.Node(), h.String, tested), h.Bool);
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(test, HandBuiltRefinements.Evaluate(h.Call(s, h.Regex("^[a-z]+$")))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("ABC", false)]
    public void AStringEqualToALiteralMeetsARegexParameterTheLiteralMatches(string literal, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var s = Parameter(h.String, "s");
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(h.Binary(s, BinaryOperatorKind.Equal, h.Literal(literal)),
            HandBuiltRefinements.Evaluate(h.Call(s, h.Regex("^[a-z]+$")))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body), refused: "MIST0271");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NotOkSelectsTheAlternativeWhereTheComparisonFailed(bool below, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var i = HandBuiltRefinements.Local(h.Int, "i");
        var n = HandBuiltRefinements.Local(h.Int, "n");
        var ok = HandBuiltRefinements.Local(h.Bool, "ok");
        var required = h.Refine(h.Int, below ? ComparisonKind.LessThan : ComparisonKind.GreaterThanOrEqual, n);
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(ok, h.Binary(i, BinaryOperatorKind.LessThan, n)),
            HandBuiltRefinements.If(h.Not(ok), HandBuiltRefinements.Need(i, required)));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData("and", true)]
    [InlineData("or", false)]
    [InlineData("not-or", true)]
    [InlineData("not-and", false)]
    public void AndOrAndNotOverBooleanLocalsFollowTheirPaths(string shape, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var i = HandBuiltRefinements.Local(h.Int, "i");
        var j = HandBuiltRefinements.Local(h.Int, "j");
        var n = HandBuiltRefinements.Local(h.Int, "n");
        var ok = HandBuiltRefinements.Local(h.Bool, "ok");
        var fine = HandBuiltRefinements.Local(h.Bool, "fine");
        var below = h.Refine(h.Int, ComparisonKind.LessThan, n);
        var atLeast = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, n);
        var (condition, required) = shape switch
        {
            "and" => (h.And(ok, fine), below),
            "or" => (h.Binary(ok, BinaryOperatorKind.LogicalOr, fine), below),
            "not-or" => (h.Not(h.Binary(ok, BinaryOperatorKind.LogicalOr, fine)), atLeast),
            _ => (h.Not(h.And(ok, fine)), atLeast),
        };
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(ok, h.Binary(i, BinaryOperatorKind.LessThan, n)),
            HandBuiltRefinements.Give(fine, h.Binary(j, BinaryOperatorKind.LessThan, n)),
            HandBuiltRefinements.If(condition, HandBuiltRefinements.Need(j, required)));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    private static LocalReferenceExpression Parameter(IMistType type, string name)
    {
        return HandBuiltRefinements.Read(new LocalVariable(name, type) { Parameter = new Parameter(name, type) });
    }

    private static UnaryExpression Negated(HandBuiltRefinements h, Expression operand)
    {
        return new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, operand, h.Double);
    }

    private static void AssertAdmitted(bool admitted, IReadOnlyList<Diagnostic> findings, string refused = "MIST0272")
    {
        if(admitted)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal(refused, Assert.Single(findings).Number.Value);
        }
    }
}
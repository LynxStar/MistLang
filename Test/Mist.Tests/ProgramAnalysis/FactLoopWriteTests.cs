using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class FactLoopWriteTests
{
    [Theory]
    [InlineData("while")]
    [InlineData("for")]
    [InlineData("short-circuit")]
    [InlineData("pattern")]
    [InlineData("iterator")]
    public void WritesAcrossTheRepeatingRegionCannotRestoreTheLoopEntryValue(string shape)
    {
        var h = new HandBuiltRefinements();
        var i = HandBuiltRefinements.Local(h.Int, "i");
        var step = HandBuiltRefinements.Assign(i, h.Binary(i, BinaryOperatorKind.Add, h.Literal(1L), h.Int));
        var condition = h.Binary(step, BinaryOperatorKind.LessThan, h.Literal(100L));
        var body = HandBuiltModel.Block(HandBuiltRefinements.Need(i, h.Between(h.Int, 0L, 5L)));
        Statement loop = shape switch
        {
            "while" => new WhileStatement(condition, body),
            "for" => new ForStatement(null, condition, null, body),
            "short-circuit" => new WhileStatement(h.And(h.Binary(i, BinaryOperatorKind.LessThan, h.Literal(100L)), condition), body),
            "pattern" => new WhileStatement(new IsPatternExpression(HandBuiltModel.Node(), h.Literal(0L),
                new ConjunctionPattern(HandBuiltModel.Node(), h.Int,
                    [h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, 0L), h.Compare(h.Int, ComparisonKind.LessThan, step)]), h.Bool), body),
            "iterator" => new ForStatement(null, h.Binary(i, BinaryOperatorKind.LessThan, h.Literal(100L)),
                h.Call(h.And(h.Literal(true), condition), h.Bool), body),
            _ => throw new InvalidOperationException(shape),
        };

        var program = HandBuiltModel.Block(HandBuiltRefinements.Give(i, h.Literal(0L)), loop);

        Assert.Equal("MIST0272", Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), program)).Number.Value);
    }
}
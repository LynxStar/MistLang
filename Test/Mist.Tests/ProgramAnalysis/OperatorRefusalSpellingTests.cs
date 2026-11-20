using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class OperatorRefusalSpellingTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData(nameof(BinaryOperatorKind.Subtract), "-")]
    [InlineData(nameof(BinaryOperatorKind.Multiply), "*")]
    [InlineData(nameof(BinaryOperatorKind.Divide), "/")]
    [InlineData(nameof(BinaryOperatorKind.Modulo), "%")]
    [InlineData(nameof(BinaryOperatorKind.Equal), "==")]
    [InlineData(nameof(BinaryOperatorKind.NotEqual), "!=")]
    [InlineData(nameof(BinaryOperatorKind.LessThan), "<")]
    [InlineData(nameof(BinaryOperatorKind.GreaterThan), ">")]
    [InlineData(nameof(BinaryOperatorKind.LessThanOrEqual), "<=")]
    [InlineData(nameof(BinaryOperatorKind.GreaterThanOrEqual), ">=")]
    [InlineData(nameof(BinaryOperatorKind.LogicalAnd), "&&")]
    [InlineData(nameof(BinaryOperatorKind.LogicalOr), "||")]
    public void ABinaryRefusal_NamesTheOperatorAsSourceWritesIt(string kind, string written)
    {
        var binary = new BinaryExpression(
            HandBuiltModel.Node(37), HandBuiltModel.Literal(HandBuiltModel.Class("Left")), Enum.Parse<BinaryOperatorKind>(kind),
            HandBuiltModel.Literal(HandBuiltModel.Class("Right")), ErrorType.Instance);

        var finding = Assert.Single(model.Analyze(new OperatorAnalyzer(), binary));

        Assert.Equal("MIST0200", finding.Number.Value);
        Assert.Equal($"Operator '{written}' cannot be applied to operands of type 'Left' and 'Right'.", finding.Message);
    }

    [Theory]
    [InlineData(nameof(UnaryOperatorKind.Negate), "-")]
    [InlineData(nameof(UnaryOperatorKind.LogicalNot), "!")]
    public void AUnaryRefusal_NamesTheOperatorAsSourceWritesIt(string kind, string written)
    {
        var unary = new UnaryExpression(
            HandBuiltModel.Node(37), Enum.Parse<UnaryOperatorKind>(kind), HandBuiltModel.Literal(HandBuiltModel.Class("Shape")), ErrorType.Instance);

        var finding = Assert.Single(model.Analyze(new OperatorAnalyzer(), unary));

        Assert.Equal("MIST0200", finding.Number.Value);
        Assert.Equal(new NameValue(written), finding.Arguments[0]);
    }
}
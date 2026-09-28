using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class UnaryNegationRuleTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Byte")]
    [InlineData("UInt16")]
    [InlineData("UInt32")]
    [InlineData("UInt64")]
    [InlineData("Char")]
    public void AnUnsignedTypeOrChar_CannotBeNegated(string type)
    {
        var operand = new HandBuiltModel().Type(type);

        Assert.Equal("MIST_PA_OPERATOR_NOT_APPLICABLE", OperatorDecision.Unary(UnaryOperatorKind.Negate, operand)?.Id.Value);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Int32")]
    [InlineData("Double")]
    public void ASignedTypeWhoseDeclarationIsNotLoaded_IsStillNegatable(string type)
    {
        Assert.Null(OperatorDecision.Unary(UnaryOperatorKind.Negate, new HandBuiltModel().Type(type)));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ADeclaredNegation_AnswersForItsType()
    {
        var integer = new HandBuiltModel().Type("Int32");
        integer.OperatorsList.Add(new Operator(OperatorKind.Minus, integer, [new Parameter("a", integer)], new MemberModifiers { IsStatic = true }));

        Assert.Null(OperatorDecision.Unary(UnaryOperatorKind.Negate, integer));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AnOperandThatAlreadyFailed_IsNotRefusedAgain()
    {
        Assert.Null(OperatorDecision.Unary(UnaryOperatorKind.Negate, ErrorType.Instance));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ANegationTheBinderCouldNotType_IsReportedAgainstItsOperand()
    {
        var fragment = new DeclarationFragments();
        var unsigned = fragment.Built.Type("UInt32");
        fragment.Root = new UnaryExpression(
            HandBuiltModel.Node(30), UnaryOperatorKind.Negate, HandBuiltModel.Literal(unsigned, 5U, 31), ErrorType.Instance);

        var finding = Assert.Single(fragment.Run(new OperatorAnalyzer()));

        Assert.Equal("MIST_PA_OPERATOR_NOT_APPLICABLE", finding.Id.Value);
    }
}
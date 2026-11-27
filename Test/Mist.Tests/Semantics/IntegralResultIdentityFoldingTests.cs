using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class IntegralResultIdentityFoldingTests
{
    [Fact]
    public void Fold_PreservesThePlainIntegralOperandOfResultIdentity()
    {
        const int value = 37;
        var operand = HandBuiltModel.Literal(new HandBuiltModel().Type("Int32"), value);
        var identity = new ResultIdentityExpression(HandBuiltModel.Node(), operand);

        Assert.Equal((Int128)value, IntegralConstant.Fold(identity));
    }
}
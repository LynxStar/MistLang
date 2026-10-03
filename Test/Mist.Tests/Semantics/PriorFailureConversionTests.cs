using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class PriorFailureConversionTests
{
    [Theory]
    [InlineData("reported-expression", true)]
    [InlineData("reported-local", true)]
    [InlineData("unfinished-expression", false)]
    [InlineData("unfinished-local", false)]
    public void AConversionPreservesOnlyPositiveOperandFailure(string source, bool expected)
    {
        var node = HandBuiltModel.Node();
        Expression operand = source switch
        {
            "reported-expression" => new ErrorExpression(node),
            "unfinished-expression" => new UnresolvedExpression(node),
            _ => new LocalReferenceExpression(node, new LocalVariable("value", ErrorType.Instance)
            {
                IsFailed = source == "reported-local",
            }),
        };
        var conversion = new ImplicitConversionExpression(node, operand, new HandBuiltModel().Type("Int32"), []);

        Assert.Equal(expected, PriorFailure.Of(conversion));
    }
}
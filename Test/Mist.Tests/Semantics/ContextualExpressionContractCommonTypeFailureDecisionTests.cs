using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ContextualExpressionContractCommonTypeFailureDecisionTests
{
    [Theory]
    [InlineData("array", true)]
    [InlineData("array", false)]
    [InlineData("switch-result", true)]
    [InlineData("switch-result", false)]
    [InlineData("switch-scrutinee", true)]
    [InlineData("switch-scrutinee", false)]
    [InlineData("switch-guard", true)]
    [InlineData("switch-guard", false)]
    public void CommonTypedExpressionsPropagateOnlyPositiveChildFailure(string site, bool reported)
    {
        var model = new HandBuiltModel();
        var node = HandBuiltModel.Node();
        Expression input = reported ? new ErrorExpression(node) : new UnresolvedExpression(node);
        var sound = HandBuiltModel.Literal(model.Type("Int32"));
        var arm = new SwitchArm(node, new CatchAllPattern(node, model.Type("Int32"), binding: null),
            site == "switch-guard" ? input : null, site == "switch-result" ? input : sound);
        Expression bound = site == "array"
            ? new ArrayLiteralExpression(node, model.Type("Int32"), [input, sound], new ConstructedType(model.Type("Array<,>"), [model.Type("Int32")]))
            : new SwitchExpression(node, site == "switch-scrutinee" ? input : sound, [arm], model.Type("Int32"));

        Assert.Equal(reported, PriorFailure.Of(bound));
    }
}
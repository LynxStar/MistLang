using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ContextualExpressionContractAssignmentFailureTests
{
    [Theory]
    [InlineData("numeric")]
    [InlineData("declared-conversion")]
    public void OrdinaryAssignmentResultFollowsStorageAfterConversion(string shape)
    {
        var built = new HandBuiltModel();
        var storageType = shape == "numeric" ? built.Type("Int64") : HandBuiltModel.Class("Parent");
        var convertedType = shape == "numeric" ? built.Type("Int32") : HandBuiltModel.Class("Derived", storageType);
        var storage = HandBuiltModel.Literal(storageType);
        var value = HandBuiltModel.Literal(convertedType);

        Assert.Same(storageType, ExpressionBinder.AssignmentResultType(storage, value, isCoalescing: false));
    }

    [Fact]
    public void CoalescingAssignmentKeepsItsPresentValueConvention()
    {
        var built = new HandBuiltModel();
        var text = built.Type("String");
        var optional = built.Interner.GetOrCreateUnionType([text, built.Type("Void")]);

        Assert.Same(text, ExpressionBinder.AssignmentResultType(HandBuiltModel.Literal(optional), HandBuiltModel.Literal(text), isCoalescing: true));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AssignmentRetainsReportedTargetAndValueFailure(bool targetFailed, bool valueFailed)
    {
        var built = new HandBuiltModel();
        var integer = built.Type("Int64");
        Expression target = targetFailed ? new ErrorExpression(HandBuiltModel.Node()) : HandBuiltModel.Literal(integer);
        Expression value = valueFailed ? new ErrorExpression(HandBuiltModel.Node()) : HandBuiltModel.Literal(built.Type("Int32"));

        Assert.True(PriorFailure.Of(new AssignmentExpression(HandBuiltModel.Node(), target, value, integer)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnfinishedAssignmentInputsDoNotPretendTheyWereReported(bool targetUnfinished)
    {
        var built = new HandBuiltModel();
        var integer = built.Type("Int64");
        Expression target = targetUnfinished ? new UnresolvedExpression(HandBuiltModel.Node()) : HandBuiltModel.Literal(integer);
        Expression value = targetUnfinished ? HandBuiltModel.Literal(ErrorType.Instance) : new UnresolvedExpression(HandBuiltModel.Node());

        Assert.False(PriorFailure.Of(new AssignmentExpression(HandBuiltModel.Node(), target, value, integer)));
    }
}
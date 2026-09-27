using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 1: decide the source and clone argument conversions directly on hand-built types.
[Trait("Tier", "1")]
public sealed class NewtypeCloneAssignabilityTests
{
    [Theory]
    [InlineData("newtype-clone-passes-itself-as-its-source", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    public void CloneReceiverCannotStandInForItsSource(string refusal, string diagnosticId)
    {
        var built = new HandBuiltModel();
        var lion = HandBuiltModel.Class("Lion");
        var puma = HandBuiltModel.Class("Puma");
        puma.CloneSource = lion;
        puma.CloneSourceType = lion;
        var parameter = new Parameter("lion", lion);
        var decision = Assignability.Judge(new ThisExpression(HandBuiltModel.Node(40), puma), parameter.Type, built.Interner);
        _ = Assert.NotNull(decision);
        Assert.True(decision.Value.Id.Value == diagnosticId, refusal);
        Assert.Null(Assignability.Judge(new ThisExpression(HandBuiltModel.Node(40), lion), parameter.Type, built.Interner));
    }
}
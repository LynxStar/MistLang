using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class CoalescingAssignedTypeDecisionTests
{
    private readonly HandBuiltRefinements h = new();

    [Theory]
    [InlineData(true, false, "refined")]
    [InlineData(false, false, "optional")]
    [InlineData(true, true, "refined")]
    [InlineData(false, true, "refined")]
    public void AValueIsJudgedAgainstTheTargetOrForACoalescingAssignmentItsNonVoidPortion(bool coalescing, bool bare, string expected)
    {
        var small = h.Between(h.Int, 11L, 19L, "Small");
        var optional = h.Built.Interner.GetOrCreateUnionType([small, h.Built.Type("Void")]);
        var target = HandBuiltRefinements.Local(bare ? small : optional);
        var assignment = new AssignmentExpression(HandBuiltModel.Node(), target, h.Literal(15L), target.Type, coalescing);

        var judged = StorageLocations.AssignedType(assignment, h.Built.Interner);

        Assert.Equal(expected == "refined" ? small : optional, judged);
    }
}
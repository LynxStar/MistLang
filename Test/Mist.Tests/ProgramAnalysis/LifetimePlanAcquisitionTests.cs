using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class LifetimePlanAcquisitionTests
{
    private readonly LifetimeFragments f = new();

    [Fact]
    public void AnUninitializedLocalKeepsItsFirstOwnedAssignmentAfterReacquisition()
    {
        var local = f.Local("resource");
        var declaration = Declare(local, null);
        var firstAssignment = AssignAt(local, 10);
        var laterAssignment = AssignAt(local, 30);
        var plan = new LifetimePlan(new Dictionary<LocalVariable, LocalDeclarationStatement> { [local] = declaration });

        plan.Mark(firstAssignment, local, owned: true);
        plan.Mark(new LocalReferenceExpression(HandBuiltModel.Node(20), local), local, owned: false);
        plan.Mark(laterAssignment, local, owned: true);

        Assert.Same(firstAssignment.Node, plan.AcquiredAt(declaration));
    }

    [Fact]
    public void AnInitializedLocalKeepsItsInitializerAfterReacquisition()
    {
        var local = f.Local("resource");
        var initializer = f.New(1);
        var declaration = Declare(local, initializer);
        var plan = new LifetimePlan(new Dictionary<LocalVariable, LocalDeclarationStatement> { [local] = declaration });

        plan.Mark(new LocalReferenceExpression(HandBuiltModel.Node(20), local), local, owned: false);
        plan.Mark(AssignAt(local, 30), local, owned: true);

        Assert.Same(initializer.Node, plan.AcquiredAt(declaration));
    }

    private AssignmentExpression AssignAt(LocalVariable local, int offset)
    {
        return new AssignmentExpression(HandBuiltModel.Node(offset), Ref(local), f.New(offset), local.Type);
    }
}
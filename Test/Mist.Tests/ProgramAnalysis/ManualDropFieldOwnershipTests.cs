using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ManualDropFieldOwnershipTests
{
    [Fact]
    public void AManualDropOwnsOnlyTheLinearInstanceField()
    {
        var fragments = new LifetimeFragments();
        var holder = HandBuiltModel.Class("Holder");
        holder.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear };
        var resource = new Field("Resource", fragments.Token, OwnershipKind.Unrestricted, default)
        {
            DeclaringType = holder,
            Initializer = fragments.New(1),
        };
        var staticResource = new Field("StaticResource", fragments.Token, OwnershipKind.Unrestricted, new MemberModifiers { IsStatic = true })
        {
            DeclaringType = holder,
            Initializer = fragments.New(2),
        };
        var count = new Field("Count", fragments.Int32, OwnershipKind.Unrestricted, default)
        {
            DeclaringType = holder,
            Initializer = HandBuiltModel.Literal(fragments.Int32, 3),
        };
        holder.FieldsList.AddRange([resource, staticResource, count]);
        var body = HandBuiltModel.Block();
        var drop = new Method("Drop", fragments.Void, [], default)
        {
            DeclaringType = holder,
            Body = body,
            DeclarationNode = HandBuiltModel.Node(),
        };
        holder.MethodsList.Add(drop);

        var plan = LocalLifetimes.Plan(body, drop, holder);

        Assert.Equal(LifetimeRelease.AtExit, plan.ReleaseOf(resource));
        Assert.False(plan.Covers(staticResource));
        Assert.False(plan.Covers(count));
    }
}
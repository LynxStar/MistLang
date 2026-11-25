using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// A use that names a withheld CLR member carries the withholding reason as a frame (diagnostics Requirement 14.7),
// so the reasons gathered must come from every type member lookup would have searched for the name: an abstract base
// lookup enters, a type parameter's constraints, a newtype's source. TypeMemberLookup.EnqueueBases is the oracle.
[Trait("Tier", "1")]
public sealed class ProjectedWithholdingFrameWalkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInterfaceReasonIsGatheredUnlessAProjectedClassNeverSearchesItsProjectedInterface(bool projectedOwner)
    {
        var surface = Interface("Surface");
        surface.Assembly = MistAssembly.Ambient;
        var frame = Withheld(surface, "Hidden");
        var owner = HandBuiltModel.Class("Owner");
        owner.Assembly = projectedOwner ? MistAssembly.Ambient : MistAssembly.Synthetic;
        owner.BaseAbstractTypes.Add(surface);

        var frames = ProjectedWithholding.Frames(owner, "Hidden");

        if(projectedOwner)
        {
            Assert.Empty(frames);
            return;
        }

        Assert.Same(frame, Assert.Single(frames));
    }

    [Fact]
    public void AConstrainedTypeParameterGathersTheReasonsOfEveryTypeConstraint()
    {
        var generic = HandBuiltModel.Class("Labelled");
        generic.TypeParametersList.Add(new TypeParameter("E", 0, generic.Scope, generic));
        var first = Withheld(generic, "Hidden");
        var shape = Interface("Shape");
        var second = Withheld(shape, "Hidden");
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        parameter.ConstraintsList.Add(new TypeConstraint(new ConstructedType(generic, [HandBuiltModel.Class("Text")])));
        parameter.ConstraintsList.Add(new TypeConstraint(shape));

        var frames = ProjectedWithholding.Frames(parameter, "Hidden");

        Assert.Equal(new Frame[] { first, second }, frames);
    }

    [Fact]
    public void ANewtypeGathersTheReasonsOfItsSource()
    {
        var source = HandBuiltModel.Class("Surface");
        var frame = Withheld(source, "Hidden");
        var newtype = HandBuiltModel.Newtype("Meters", source);

        var frames = ProjectedWithholding.Frames(newtype, "Hidden");

        Assert.Same(frame, Assert.Single(frames));
    }

    [Fact]
    public void AnInterfaceReachedByTwoPathsContributesItsReasonOnce()
    {
        var root = Interface("Root");
        var frame = Withheld(root, "Hidden");
        var left = Interface("Left");
        left.BaseAbstractTypes.Add(root);
        var right = Interface("Right");
        right.BaseAbstractTypes.Add(root);
        var owner = HandBuiltModel.Class("Owner");
        owner.BaseAbstractTypes.AddRange([left, right]);

        var frames = ProjectedWithholding.Frames(owner, "Hidden");

        Assert.Same(frame, Assert.Single(frames));
    }

    private static AbstractType Interface(string name)
    {
        return new AbstractType(name, "Probe", new Scope(null), null, null);
    }

    private static WithholdingFrame Withheld(MemberType owner, string name)
    {
        var frame = new WithholdingFrame(new SymbolValue($"{owner.Name}.{name}()", name), new EnumValue("pointer"));
        owner.WithheldMembers.Add(frame);
        return frame;
    }
}
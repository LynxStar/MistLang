using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures.DisposalProjection;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AmbientDisposalProjectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupFlagsFollowCompletedHeritageInEitherProjectionOrder(bool reverse)
    {
        var types = new[]
        {
            typeof(Leaf), typeof(Middle<>), typeof(Resource<>), typeof(IGenericResource<>),
            typeof(IResource), typeof(IDisposable), typeof(ExplicitResource), typeof(Plain),
        };
        var context = new ConstraintProjectionTestContext(reverse ? types.Reverse().ToArray() : types);

        context.Project();

        foreach(var clrType in new[] { typeof(Leaf), typeof(Middle<>), typeof(Resource<>) })
        {
            var projected = Assert.IsType<ClassType>(context.Type(clrType));
            Assert.True(projected.HasDrop);
            Assert.True(projected.IsDisposableDrop);
        }

        var leaf = Assert.IsType<ClassType>(context.Type(typeof(Leaf)));
        Assert.Empty(leaf.BaseAbstractTypes);
        Assert.Same(context.Type(typeof(Middle<>)), Assert.IsType<ConstructedType>(leaf.BaseType).Definition);
        Assert.Equal([context.Type(typeof(IDisposable))], context.Type(typeof(IResource)).BaseAbstractTypes);
        Assert.Equal([context.Type(typeof(IResource))], context.Type(typeof(IGenericResource<>)).BaseAbstractTypes);
        var explicitResource = Assert.IsType<ClassType>(context.Type(typeof(ExplicitResource)));
        Assert.True(explicitResource.HasDrop);
        Assert.False(explicitResource.IsDisposableDrop);
        Assert.False(Assert.IsType<ClassType>(context.Type(typeof(Plain))).HasDrop);
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }
}
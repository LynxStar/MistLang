using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class PureFunctionReferenceTests
{
    [Fact]
    public void PureOverAReferenceThatAlreadyFailedAddsNoFindingOfItsOwn()
    {
        var f = new BindingFragments();

        var applied = PureFunctionReference.Apply(ErrorType.Instance, HandBuiltModel.Node(12), f.Builder);

        Assert.Same(ErrorType.Instance, applied);
        Assert.Empty(f.Diagnostics.Admitted);
    }
}
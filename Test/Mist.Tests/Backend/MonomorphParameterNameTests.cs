using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class MonomorphParameterNameTests
{
    [Fact]
    public void PackElementsAvoidFixedParametersOnBothSides()
    {
        var first = Parameter("T1");
        var last = Parameter("T3");
        var pack = Parameter("Ts", true);

        var specialization = Monomorph.For([first, pack, last], [2])!;

        Assert.Equal(["T1", "T2", "T4", "T3"], specialization.Parameters([first, pack, last]).Select(p => p.Name));
        Assert.Same(first, specialization.Parameters([first, pack, last])[0]);
        Assert.Same(last, specialization.Parameters([first, pack, last])[^1]);
        Assert.Equal([0, 1], specialization.Expansion(pack).Select(p => p.Ordinal));
    }

    [Fact]
    public void NestedPackAvoidsOuterFixedAndAllocatedNames()
    {
        var fixedParameter = Parameter("T1");
        var outer = Parameter("Ts", true);
        var inner = Parameter("Us", true);
        using var scope = Monomorph.For([fixedParameter, outer], [2])!.Enter();

        var specialization = Monomorph.For([inner], [2])!;

        Assert.Equal(["T4", "T5"], specialization.Expansion(inner).Select(p => p.Name));
        Assert.Equal([2, 3], specialization.Expansion(inner).Select(p => p.Ordinal));
    }

    private static TypeParameter Parameter(string name, bool pack = false)
    {
        return new TypeParameter(name, 0, new Scope(null), null)
        {
            PackKind = pack ? TypePackKind.Unbracketed : TypePackKind.None,
        };
    }
}
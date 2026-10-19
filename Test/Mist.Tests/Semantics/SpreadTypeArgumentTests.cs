using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class SpreadTypeArgumentTests
{
    [Theory]
    [InlineData(-1, 1, 1, 0, false)]
    [InlineData(0, 1, 1, 0, true)]
    [InlineData(1, 3, 4, 0, false)]
    [InlineData(1, 3, 4, 1, true)]
    [InlineData(1, 3, 4, 2, true)]
    [InlineData(1, 3, 4, 3, false)]
    [InlineData(1, 3, 2, 1, false)]
    public void OnlyTheAbsorbingRegionAcceptsASymbolicSpread(
        int packIndex, int parameterCount, int argumentCount, int spreadIndex, bool expected)
    {
        var parameters = Enumerable.Range(0, parameterCount).Select(index =>
            new TypeParameter($"T{index}", index, new Scope(null), null)
            {
                PackKind = index == packIndex ? TypePackKind.Unbracketed : TypePackKind.None,
            }).ToList();

        Assert.Equal(expected, PackBinding.AcceptsSpread(parameters, argumentCount, spreadIndex));
    }
}
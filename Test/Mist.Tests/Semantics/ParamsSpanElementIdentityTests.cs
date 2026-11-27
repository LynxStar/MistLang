using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ParamsSpanElementIdentityTests
{
    [Theory]
    [InlineData("Int32", -1)]
    [InlineData("Int64", 0)]
    public void ReadOnlySpanOfInt32_IsPreferredToAnArrayOnlyWithIdenticalElements(string arrayElementName, int expected)
    {
        var built = new HandBuiltModel();
        var integer = built.Type(WellKnownTypes.Int32.Name);
        var length = new UnknownValueType(integer);
        var span = new ConstructedType(built.Type(WellKnownTypes.ReadOnlySpan.Name), [integer, length]);
        var array = new ConstructedType(built.Type(WellKnownTypes.Array.Name), [built.Type(arrayElementName), length]);

        Assert.Equal(expected, ParamsCollection.Compare(span, array));
        Assert.Equal(-expected, ParamsCollection.Compare(array, span));
    }
}
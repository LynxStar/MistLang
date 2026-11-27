using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class OptionalMultiConstituentProjectionTests
{
    [Fact]
    public void NonVoidPortionPreservesBothPresentConstituents()
    {
        var interner = new TypeInterner();
        var integer = new StructType("Int32", "Standard", null, null, null);
        var text = new ClassType("String", "Standard", null, null, null);
        var absence = new StructType("Void", "Standard", null, null, null);
        var input = interner.GetOrCreateUnionType([integer, text, absence]);

        var projected = Assert.IsAssignableFrom<IUnionType>(OptionalLifting.NonVoidPortion(input, interner));

        Assert.Equal(2, projected.Constituents.Count);
        Assert.Contains(projected.Constituents, constituent => ReferenceEquals(constituent, integer));
        Assert.Contains(projected.Constituents, constituent => ReferenceEquals(constituent, text));
        Assert.DoesNotContain(projected.Constituents, constituent => constituent.FullyQualifiedName == absence.FullyQualifiedName);
    }
}
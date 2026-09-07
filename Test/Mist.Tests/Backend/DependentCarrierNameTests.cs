using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Tier 1: the C# names a dependent value is carried under.
/// <para>
/// A COLLISION HERE IS TWO TYPES SILENTLY BECOMING ONE, which is why this is asserted rather than
/// eyeballed. The carrier struct is what distinguishes <c>FixedBuffer&lt;5&gt;</c> from
/// <c>FixedBuffer&lt;6&gt;</c> once emitted, so two values sharing a name would make the two
/// constructions the same C# type and nothing downstream would notice -- Roslyn sees one
/// well-formed program either way.
/// </para>
/// </summary>
public sealed class DependentCarrierNameTests
{
    private static MistType StandardType(string name)
    {
        return new ClassType(name, "Standard", new Scope(null), declaringType: null, declarationNode: null);
    }

    private static string? Carrier(string simpleName, object value)
    {
        return DependentCarrierNames.Carrier(new LiftedType(StandardType(simpleName), value));
    }

    [Theory]
    [InlineData("Int32", 5L, "Const_Int_5")]
    [InlineData("Int32", -1L, "Const_Int_Neg1")]
    [InlineData("Int64", 9L, "Const_Long_9")]
    [InlineData("Byte", 255L, "Const_Byte_255")]
    public void IntegralValue_IsNamedByItsDigits(string domain, long value, string expected)
    {
        Assert.Equal(expected, Carrier(domain, value));
    }

    [Fact]
    public void BooleanValues_AreNamedApart()
    {
        Assert.Equal("Const_Bool_True", Carrier("Boolean", true));
        Assert.Equal("Const_Bool_False", Carrier("Boolean", false));
    }

    [Fact]
    public void CharsDifferingOnlyInCase_DoNotCollide()
    {
        // The spec's readable form names both of these Const_Char_X. Hex always is what keeps them
        // two carriers, and two carriers is what keeps them two types.
        Assert.NotEqual(Carrier("Char", 'x'), Carrier("Char", 'X'));
        Assert.Equal("Const_Char_0x0078", Carrier("Char", 'x'));
    }

    [Fact]
    public void DomainThatCannotCarryIdentity_HasNoCarrier()
    {
        Assert.Null(Carrier("String", 1L));
    }

    [Fact]
    public void WitnessInterface_IsNamedFromTheDomain()
    {
        Assert.Equal(
            "IConstInt", DependentCarrierNames.Witness(StandardType("Int32")));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData('x', "'\\u0078'")]
    public void Literal_IsWhatCSharpReadsBack(object value, string expected)
    {
        Assert.Equal(expected, DependentCarrierNames.Literal(value));
    }
}
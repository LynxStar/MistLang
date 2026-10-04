using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Tier 1: the C# names a dependent value is carried under, and how a parameter reads it back.
/// <para>
/// A COLLISION HERE IS TWO TYPES SILENTLY BECOMING ONE, which is why this is asserted rather than
/// eyeballed. The carrier struct is what distinguishes <c>FixedBuffer&lt;5&gt;</c> from
/// <c>FixedBuffer&lt;6&gt;</c> once emitted, so two values sharing a name would make the two
/// constructions the same C# type and nothing downstream would notice -- Roslyn sees one
/// well-formed program either way. The converse matters as much: one value must have ONE carrier
/// whatever slot it fills, or a long slot reached through an int parameter names a different type
/// than the same long slot closed directly.
/// </para>
/// </summary>
[Trait("Tier", "1")]
public sealed class DependentCarrierNameTests
{
    private static MistType StandardType(string name)
    {
        return new ClassType(name, "Standard", new Scope(null), declaringType: null, declarationNode: null);
    }

    private static string? Carrier(string domain, object value)
    {
        return DependentCarrierNames.Carrier(StandardType(domain), new LiftedType(StandardType("Int32"), value));
    }

    [Theory]
    [InlineData(5L, "Const_5")]
    [InlineData(-1L, "Const_Neg1")]
    [InlineData(long.MinValue, "Const_Neg9223372036854775808")]
    [InlineData(ulong.MaxValue, "Const_18446744073709551615")]
    public void IntegralValue_IsNamedByItsDigits(object value, string expected)
    {
        Assert.Equal(expected, DependentCarrierNames.Carrier(value));
    }

    [Theory]
    [InlineData("Int32")]
    [InlineData("Int64")]
    [InlineData("Byte")]
    [InlineData("UInt64")]
    public void OneValue_HasOneCarrierWhateverIntegralSlotItFills(string domain)
    {
        Assert.Equal("Const_9", Carrier(domain, 9L));
    }

    [Fact]
    public void BooleanValues_AreNamedApartFromIntegers()
    {
        Assert.Equal("Const_True", DependentCarrierNames.Carrier(true));
        Assert.Equal("Const_False", DependentCarrierNames.Carrier(false));
        Assert.NotEqual(DependentCarrierNames.Carrier(1L), DependentCarrierNames.Carrier(true));
    }

    [Fact]
    public void CharsDifferingOnlyInCase_DoNotCollide()
    {
        // The spec's readable form names both of these Const_Char_X. Hex always is what keeps them
        // two carriers, and two carriers is what keeps them two types.
        Assert.NotEqual(DependentCarrierNames.Carrier('x'), DependentCarrierNames.Carrier('X'));
        Assert.Equal("Const_Char_0x0078", DependentCarrierNames.Carrier('x'));
        Assert.NotEqual(DependentCarrierNames.Carrier(120L), DependentCarrierNames.Carrier('x'));
    }

    [Fact]
    public void NewlineAndBackslash_HaveDistinctCarriers()
    {
        Assert.Equal("Const_Char_0x000a", DependentCarrierNames.Carrier('\n'));
        Assert.Equal("Const_Char_0x005c", DependentCarrierNames.Carrier('\\'));
    }

    [Theory]
    [InlineData("String", 1L)]
    [InlineData("Int32", true)]
    [InlineData("Boolean", 1L)]
    [InlineData("Char", 65L)]
    [InlineData("Int32", 'A')]
    public void ValueOfAnotherKind_HasNoCarrierInTheSlot(string domain, object value)
    {
        Assert.Null(Carrier(domain, value));
    }

    [Theory]
    [InlineData("Int32", "IConstIntegral")]
    [InlineData("UInt64", "IConstIntegral")]
    [InlineData("SByte", "IConstIntegral")]
    [InlineData("Boolean", "IConstBool")]
    [InlineData("Char", "IConstChar")]
    public void WitnessInterface_IsNamedFromTheDomainsKind(string domain, string expected)
    {
        Assert.Equal(expected, DependentCarrierNames.Witness(StandardType(domain)));
    }

    [Fact]
    public void WitnessOfARefinedDomain_IsItsPrimitivesKind()
    {
        Assert.Equal("IConstIntegral", DependentCarrierNames.Witness(HandBuiltModel.Refined(StandardType("Int32"), "Positive")));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData('x', "'\\u0078'")]
    [InlineData(ulong.MaxValue, "18446744073709551615")]
    [InlineData(long.MinValue, "-9223372036854775808")]
    public void Literal_IsWhatCSharpReadsBack(object value, string expected)
    {
        Assert.Equal(expected, DependentCarrierNames.Literal(value));
    }

    [Theory]
    [InlineData("Int32", "((int)TN.Value)")]
    [InlineData("UInt64", "((ulong)TN.Value)")]
    [InlineData("Byte", "((byte)TN.Value)")]
    [InlineData("Boolean", "TN.Value")]
    [InlineData("Char", "TN.Value")]
    public void ValueRead_ConvertsTheWitnessToTheReadersOwnDomain(string domain, string expected)
    {
        Assert.Equal(expected, DependentCarrierNames.ValueRead(Dependent("N", StandardType(domain))));
    }

    [Fact]
    public void ValueRead_OfAnOrdinaryParameter_IsNotAValue()
    {
        Assert.Null(DependentCarrierNames.ValueRead(new TypeParameter("T", 0, new Scope(null), null)));
    }

    [Fact]
    public void ValueProperty_DelegatesToTheWitnessInTheDeclaredDomain()
    {
        var parameter = Dependent("Size", StandardType("Int64"));
        parameter.ValueProperty = new Property("Size", parameter.Domain!, default);

        Assert.Equal("public static long Size => ((long)TSize.Value);", DependentCarrierNames.ValueProperty(parameter));
    }

    [Fact]
    public void ValueProperty_IsAbsentForAParameterProjectingNone()
    {
        Assert.Null(DependentCarrierNames.ValueProperty(Dependent("Size", StandardType("Int32"))));
    }

    private static TypeParameter Dependent(string name, MistType domain)
    {
        return new TypeParameter(name, 0, new Scope(null), null) { IsDependent = true, Domain = domain };
    }
}
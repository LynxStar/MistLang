using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class EnumValueCountingTests
{
    private readonly BindingFragments fragments = new();

    [Fact]
    public void CountingPastLongMaxValue_StaysExactInAULongEnum()
    {
        var unsigned = Primitive("UInt64");
        var codes = Enum(unsigned, ("Start", HandBuiltModel.Literal(unsigned, 9223372036854775807UL)), ("Next", null));

        EnumMemberBinder.BindAll(codes, fragments.Builder);

        AssertValue(9223372036854775808UL, codes, "Next");
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AValueAfterArithmetic_CountsOnFromWhatTheArithmeticIs()
    {
        var integer = Primitive("Int32");
        var sum = new BinaryExpression(
            HandBuiltModel.Node(), HandBuiltModel.Literal(integer, 1), BinaryOperatorKind.Add, HandBuiltModel.Literal(integer, 1), integer);
        var codes = Enum(integer, ("A", sum), ("B", null));

        EnumMemberBinder.BindAll(codes, fragments.Builder);

        AssertValue(3, codes, "B");
    }

    [Fact]
    public void ANameOnlyByteEnum_HoldsByteValues()
    {
        var octet = Primitive("Byte");
        var levels = Enum(octet, ("Low", null), ("High", null));

        EnumMemberBinder.BindAll(levels, fragments.Builder);

        AssertValue((byte)0, levels, "Low");
        AssertValue((byte)1, levels, "High");
    }

    [Fact]
    public void CountingPastTheBase_IsRefusedAtTheVariantThatWouldLeaveIt()
    {
        var octet = Primitive("Byte");
        var levels = Enum(octet, ("Top", HandBuiltModel.Literal(Primitive("Int32"), 255)), ("Over", null));

        EnumMemberBinder.BindAll(levels, fragments.Builder);

        Assert.Null(levels.MembersList[1].Value);
        Assert.Equal("MIST_PM_ENUM_VALUE_OUT_OF_RANGE", Assert.Single(fragments.Diagnostics.ToImmutableArray()).Id.Value);
    }

    private IMistType Primitive(string name)
    {
        return fragments.Built.Type(name);
    }

    private static EnumType Enum(IMistType underlying, params (string Name, Expression? Value)[] members)
    {
        var enumType = new EnumType("Codes", "Probe", null, null, BindingFragments.At<EnumDeclarationNode>())
        {
            UnderlyingType = underlying,
        };

        foreach(var (name, value) in members)
        {
            enumType.MembersList.Add(new EnumMember(name, isDefault: false, value, BindingFragments.At<EnumMemberNode>()));
        }

        return enumType;
    }

    private static void AssertValue(object expected, EnumType enumType, string member)
    {
        var literal = Assert.IsType<LiteralExpression>(enumType.MembersList.Single(variant => variant.Name == member).Value);

        Assert.Equal(expected, literal.Value);
        Assert.Same(enumType.UnderlyingType, literal.Type);
    }
}
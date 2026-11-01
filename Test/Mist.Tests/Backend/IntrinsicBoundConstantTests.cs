using Mist.ProgramModel;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class IntrinsicBoundConstantTests
{
    [Theory]
    [InlineData("Int32", "MinValue", -2147483648L)]
    [InlineData("Int64", "MinValue", long.MinValue)]
    [InlineData("Int32", "MaxValue", 2147483647L)]
    public void DeclaredBoundsAreAvailableToConstantArithmetic(string name, string member, long expected)
    {
        var built = new HandBuiltModel();
        var owner = built.Type(name);
        owner.Modifiers = new MemberModifiers { IsIntrinsic = true };
        var field = new Field(member, owner, OwnershipKind.Unrestricted,
            new MemberModifiers { IsIntrinsic = true, IsStatic = true, IsReadonly = true })
        { DeclaringType = owner };
        var constant = IntrinsicBounds.Of(field, HandBuiltModel.Node());
        Assert.NotNull(constant);
        Assert.Same(owner, constant.Type);
        Assert.Equal((Int128)expected, IntegralConstant.Of(constant.Value));
    }

    [Theory]
    [InlineData(false, true, "MaxValue")]
    [InlineData(true, false, "MaxValue")]
    [InlineData(true, true, "Other")]
    public void OrdinaryStorageAndOtherMembersAreNotConstants(bool intrinsic, bool readOnly, string name)
    {
        var built = new HandBuiltModel();
        var owner = built.Type("Int32");
        owner.Modifiers = new MemberModifiers { IsIntrinsic = true };
        var field = new Field(name, owner, OwnershipKind.Unrestricted,
            new MemberModifiers { IsIntrinsic = intrinsic, IsStatic = true, IsReadonly = readOnly })
        { DeclaringType = owner };
        Assert.Null(IntrinsicBounds.Of(field, HandBuiltModel.Node()));
    }
}
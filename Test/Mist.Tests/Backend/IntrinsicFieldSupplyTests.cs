using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class IntrinsicFieldSupplyTests
{
    [Theory]
    [InlineData("Int32", "MinValue")]
    [InlineData("Int64", "MinValue")]
    [InlineData("UInt64", "MaxValue")]
    [InlineData("Decimal", "MaxValue")]
    [InlineData("Char", "MinValue")]
    public void BoundsAreSuppliedByTypedClrFields(string name, string member)
    {
        var built = new HandBuiltModel();
        var owner = built.Type(name);
        var field = Bound(owner, owner, member);
        Assert.Equal(IntrinsicSupply.Kind.Metadata, IntrinsicSupply.Classify(field, Type.GetType));
    }

    [Theory]
    [InlineData("Missing", "Int32", true)]
    [InlineData("MaxValue", "Int64", true)]
    [InlineData("MaxValue", "Int32", false)]
    public void ANameWithTheWrongFieldContractIsNotSupplied(string name, string type, bool isStatic)
    {
        var built = new HandBuiltModel();
        var field = new Field(name, built.Type(type), OwnershipKind.Unrestricted,
            new MemberModifiers { IsIntrinsic = true, IsStatic = isStatic, IsReadonly = true })
        { DeclaringType = built.Type("Int32") };
        Assert.Equal(IntrinsicSupply.Kind.Missing, IntrinsicSupply.Classify(field, Type.GetType));
    }

    [Fact]
    public void APropertyCannotSupplyAField()
    {
        var built = new HandBuiltModel();
        var field = Bound(built.Type("Type"), built.Type("String"), "Name");
        Assert.Equal(IntrinsicSupply.Kind.Missing, IntrinsicSupply.Classify(field, Type.GetType));
    }

    private static Field Bound(ConcreteType owner, ConcreteType type, string name)
    {
        return new Field(name, type, OwnershipKind.Unrestricted,
            new MemberModifiers { IsIntrinsic = true, IsStatic = true, IsReadonly = true })
        { DeclaringType = owner };
    }
}
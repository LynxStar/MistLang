using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

public sealed class InheritedStampabilityTests
{
    [Fact]
    public void InheritedReferenceStorageIsNotStampable()
    {
        var ancestor = Struct("Ancestor");
        AddField(ancestor, "owner", new ClassType("Owner", "Probe", null, null, null));
        var derived = Struct("Derived", ancestor);

        var reason = Stampability.WhyNotStampable(derived);

        Assert.NotNull(reason);
        Assert.Contains("field 'owner'", reason, StringComparison.Ordinal);
        Assert.False(Stampability.IsStampable(derived));
    }

    [Fact]
    public void InheritedNestedReferenceStorageIsNotStampable()
    {
        var nested = Struct("Nested");
        AddField(nested, "owner", new ClassType("Owner", "Probe", null, null, null));
        var ancestor = Struct("Ancestor");
        AddField(ancestor, "payload", nested);
        var middle = Struct("Middle", ancestor);
        var derived = Struct("Derived", middle);

        var reason = Stampability.WhyNotStampable(derived);

        Assert.NotNull(reason);
        Assert.Contains("field 'payload'", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedBaseStorageUsesItsClosedTypeArgument(bool constructedDerived)
    {
        var ancestor = Struct("Ancestor<>");
        var parameter = new TypeParameter("T", 0, ancestor.Scope, ancestor);
        ancestor.TypeParametersList.Add(parameter);
        AddField(ancestor, "value", parameter);
        var derived = Struct(constructedDerived ? "Derived<>" : "Derived", new ConstructedType(ancestor, [Primitive()]));
        if(constructedDerived)
        {
            derived.TypeParametersList.Add(new TypeParameter("TMarker", 0, derived.Scope, derived));
        }

        IMistType allocationElement = constructedDerived ? new ConstructedType(derived, [Primitive()]) : derived;

        Assert.Null(Stampability.WhyNotStampable(allocationElement));
        Assert.True(Stampability.IsStampable(allocationElement));
    }

    [Fact]
    public void InheritedPrimitiveStorageRemainsStampableAcrossSeveralBases()
    {
        var ancestor = Struct("Ancestor");
        AddField(ancestor, "first", Primitive());
        var middle = Struct("Middle", ancestor);
        AddField(middle, "second", Primitive());
        var derived = Struct("Derived", middle);
        AddField(derived, "third", Primitive());

        Assert.Null(Stampability.WhyNotStampable(derived));
        Assert.True(Stampability.IsStampable(derived));
    }

    [Fact]
    public void StaticReferenceStorageDoesNotAffectInheritedStampability()
    {
        var ancestor = Struct("Ancestor");
        AddField(ancestor, "shared", new ClassType("Owner", "Probe", null, null, null), isStatic: true);
        var derived = Struct("Derived", ancestor);

        Assert.True(Stampability.IsStampable(derived));
    }

    [Fact]
    public void InheritanceCyclesAreConservativelyNonStampable()
    {
        var first = Struct("First");
        var second = Struct("Second", first);
        first.BaseType = second;

        Assert.Equal("it contains itself", Stampability.WhyNotStampable(first));
        Assert.False(Stampability.IsStampable(second));
    }

    [Fact]
    public void AFieldCycleThroughAnInheritedStructIsNonStampable()
    {
        var ancestor = Struct("Ancestor");
        var derived = Struct("Derived", ancestor);
        AddField(ancestor, "next", derived);

        var reason = Stampability.WhyNotStampable(derived);

        Assert.NotNull(reason);
        Assert.Contains("field 'next'", reason, StringComparison.Ordinal);
    }

    private static StructType Struct(string name, MistType? baseType = null)
    {
        return new StructType(name, "Probe", null, null, null) { BaseType = baseType };
    }

    private static StructType Primitive()
    {
        return new StructType("Int32", "Standard", null, null, null)
        {
            Modifiers = new MemberModifiers { IsIntrinsic = true },
        };
    }

    private static void AddField(StructType type, string name, IMistType fieldType, bool isStatic = false)
    {
        type.FieldsList.Add(new Field(name, fieldType, OwnershipKind.Unrestricted, new MemberModifiers { IsStatic = isStatic })
        {
            DeclaringType = type,
        });
    }
}
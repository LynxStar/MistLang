using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ImportedStorageStampabilityTests
{
    [Fact]
    public void CompleteHiddenPrimitiveStorageQualifiesWithoutChangingVisibleFields()
    {
        var type = Imported(new StorageLayout(true, [new StorageField("hidden", Primitive())]));

        Assert.True(Stampability.IsStampable(type));
        Assert.Empty(type.FieldsList);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("<Value>k__BackingField")]
    public void HiddenReferenceStorageRefusesTheStruct(string name)
    {
        var type = Imported(new StorageLayout(true, [new StorageField(name, Reference())]));

        Assert.Contains(name, Stampability.WhyNotStampable(type), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownLayoutCannotBeProvedFromVisiblePrimitiveFields(bool explicitLayout)
    {
        var field = new Field("Visible", Primitive(), OwnershipKind.Unrestricted, new MemberModifiers());
        var type = Imported(explicitLayout ? new StorageLayout(false, [new StorageField(field.Name, field.Type, field)]) : null);
        type.FieldsList.Add(field);

        Assert.Contains("unknown", Stampability.WhyNotStampable(type), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyImportedStorageNeedsCompleteness(bool complete)
    {
        Assert.Equal(complete, Stampability.IsStampable(Imported(new StorageLayout(complete, []))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenGenericFieldUsesTheClosedArgument(bool reference)
    {
        var type = Imported(null);
        var parameter = new TypeParameter("T", 0, type.Scope, type);
        type.TypeParametersList.Add(parameter);
        type.ImportedStorage = new StorageLayout(true, [new StorageField("hidden", parameter)]);
        var closed = new ConstructedType(type, [reference ? Reference() : Primitive()]);

        Assert.Equal(!reference, Stampability.IsStampable(closed));
        Assert.Same(parameter, Assert.Single(type.ImportedStorage.Fields).Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenNestedFieldUsesTheEnclosingArgument(bool reference)
    {
        var outer = Imported(null);
        var parameter = new TypeParameter("T", 0, outer.Scope, outer);
        outer.TypeParametersList.Add(parameter);
        var inner = new StructType("Inner", "Probe", outer.Scope, outer, null)
        {
            ClrMetadataName = "Probe.Outer+Inner",
            ImportedStorage = new StorageLayout(true, [new StorageField("hidden", parameter)]),
        };
        var closed = new ConstructedType(inner, [], new ConstructedType(outer, [reference ? Reference() : Primitive()]));

        Assert.Equal(!reference, Stampability.IsStampable(closed));
    }

    [Fact]
    public void NestedUnknownLayoutCannotHideBehindACompleteOuterLayout()
    {
        var inner = Imported(new StorageLayout(false, []));
        var outer = Imported(new StorageLayout(true, [new StorageField("inner", inner)]));

        Assert.False(Stampability.IsStampable(outer));
    }

    [Fact]
    public void ClrValueTypeHeritageDoesNotBecomeReferenceStorage()
    {
        var type = Imported(new StorageLayout(true, [new StorageField("hidden", Primitive())]));
        type.BaseType = new ClassType("ValueType", "System", null, null, null);

        Assert.True(Stampability.IsStampable(type));
        Assert.NotNull(type.BaseType);
    }

    [Fact]
    public void ActualBaseStorageStillParticipatesInTheProof()
    {
        var type = Imported(new StorageLayout(true, [], Reference()));

        Assert.False(Stampability.IsStampable(type));
    }

    [Fact]
    public void ImportedRecursiveStorageTerminatesConservatively()
    {
        var type = Imported(null);
        type.ImportedStorage = new StorageLayout(true, [new StorageField("self", type)]);

        Assert.False(Stampability.IsStampable(type));
    }

    [Fact]
    public void FiniteNestedImportedWrappersRemainStampable()
    {
        var wrapper = Imported(null);
        var parameter = new TypeParameter("T", 0, wrapper.Scope, wrapper);
        wrapper.TypeParametersList.Add(parameter);
        wrapper.ImportedStorage = new StorageLayout(true, [new StorageField("hidden", parameter)]);
        var empty = Imported(new StorageLayout(true, []));
        var nested = new ConstructedType(wrapper, [new ConstructedType(wrapper, [empty])]);

        Assert.True(Stampability.IsStampable(nested));
    }

    private static StructType Imported(StorageLayout? layout)
    {
        return new StructType("Imported", "Probe", null, null, null)
        {
            ClrMetadataName = "Probe.Imported",
            ImportedStorage = layout,
        };
    }

    private static StructType Primitive()
    {
        return new StructType("Int32", "Standard", null, null, null)
        {
            Modifiers = new MemberModifiers { IsIntrinsic = true },
        };
    }

    private static ClassType Reference()
    {
        return new ClassType("Owner", "Probe", null, null, null);
    }
}
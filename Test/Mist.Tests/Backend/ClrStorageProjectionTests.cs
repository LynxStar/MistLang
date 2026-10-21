using System.Reflection;
using System.Runtime.CompilerServices;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ClrStorageProjectionTests
{
    [Fact]
    public void ShellHasUnknownStorageBeforeProjection()
    {
        var shell = Assert.IsType<StructType>(TypeProjector.CreateShell(typeof(PrimitiveStorage), typeof(PrimitiveStorage).Namespace!));

        Assert.False(shell.ImportedStorage!.IsComplete);
        Assert.Empty(shell.ImportedStorage.Fields);
    }

    [Fact]
    public void InstanceStorageIncludesHiddenAndBackingFieldsWithoutChangingVisibility()
    {
        var (shell, _) = Project(typeof(PrimitiveStorage));
        var storage = shell.ImportedStorage!;

        Assert.True(storage.IsComplete);
        Assert.Equal(["<Automatic>k__BackingField", "Visible", "hidden", "internalValue"], storage.Fields.Select(field => field.Name).Order(StringComparer.Ordinal));
        Assert.All(storage.Fields, field => Assert.Equal("Standard.Int32", field.Type.FullyQualifiedName));
        Assert.Equal([nameof(PrimitiveStorage.Visible), nameof(PrimitiveStorage.Static), nameof(PrimitiveStorage.Constant)], shell.FieldsList.Select(field => field.Name));
        Assert.Same(shell.FieldsList[0], storage.Fields.Single(field => field.Name == nameof(PrimitiveStorage.Visible)).Field);
        Assert.All(storage.Fields.Where(field => field.Name != nameof(PrimitiveStorage.Visible)), field => Assert.Null(field.Field));
        Assert.Equal("System.ValueType", shell.BaseType!.FullyQualifiedName);
        Assert.Null(storage.BaseType);
    }

    [Theory]
    [InlineData(typeof(HiddenReferenceStorage), "hidden")]
    [InlineData(typeof(BackingReferenceStorage), "<Text>k__BackingField")]
    public void ReferenceStorageKeepsItsPhysicalTypeWithoutInboundOptionalWrapping(Type fixture, string fieldName)
    {
        var (shell, _) = Project(fixture);

        Assert.True(shell.ImportedStorage!.IsComplete);
        var hidden = shell.ImportedStorage.Fields.Single(field => field.Name == fieldName);
        Assert.Equal("Standard.String", Assert.IsType<ClassType>(hidden.Type).FullyQualifiedName);
        Assert.Null(hidden.Field);
        Assert.DoesNotContain(shell.FieldsList, field => field.Name == fieldName);
    }

    [Fact]
    public void GenericStorageKeepsDefinitionParametersAndVisibleFieldIdentity()
    {
        var (shell, _) = Project(typeof(GenericStorage<>));

        Assert.True(shell.ImportedStorage!.IsComplete);
        var parameter = Assert.Single(shell.TypeParametersList);
        Assert.All(shell.ImportedStorage.Fields, field => Assert.Same(parameter, field.Type));
        Assert.Same(Assert.Single(shell.FieldsList), shell.ImportedStorage.Fields.Single(field => field.Name == nameof(GenericStorage<int>.Visible)).Field);
    }

    [Theory]
    [InlineData(typeof(PrivateHelperStorage), "Standard.Int32")]
    [InlineData(typeof(PrivateReferenceHelperStorage), "Standard.String")]
    public void PrivateNestedHelpersHaveCompleteStorageWithoutBecomingLookupTypes(Type fixture, string fieldType)
    {
        var (shell, resolver) = Project(fixture);

        Assert.True(shell.ImportedStorage!.IsComplete);
        var helper = Assert.IsType<StructType>(Assert.Single(shell.ImportedStorage.Fields).Type);
        Assert.True(helper.ImportedStorage!.IsComplete);
        Assert.Equal(fieldType, Assert.Single(helper.ImportedStorage.Fields).Type.FullyQualifiedName);
        Assert.Empty(shell.NestedTypes);
        Assert.Empty(shell.FieldsList);
        _ = Assert.IsType<ErrorType>(resolver.Resolve(fixture.GetNestedType("Helper", BindingFlags.NonPublic)!, shell.Scope));
    }

    [Fact]
    public void PrivateNestedGenericStoragePreservesEnclosingAndFieldParameters()
    {
        var (shell, _) = Project(typeof(PrivateGenericHelperStorage<>));

        var helper = Assert.IsType<ConstructedType>(Assert.Single(shell.ImportedStorage!.Fields).Type);
        Assert.Same(shell, helper.Enclosing!.Definition);
        Assert.Same(Assert.Single(shell.TypeParametersList), Assert.Single(helper.Enclosing.TypeArguments));
        Assert.Same(Assert.Single(shell.TypeParametersList), Assert.Single(helper.TypeArguments));
        var definition = Assert.IsType<StructType>(helper.Definition);
        Assert.True(definition.ImportedStorage!.IsComplete);
        Assert.Same(Assert.Single(definition.TypeParametersList), Assert.Single(definition.ImportedStorage.Fields).Type);
        Assert.Empty(shell.NestedTypes);
    }

    [Theory]
    [InlineData(typeof(PointerStorage))]
    [InlineData(typeof(ByRefStorage))]
    public void UnsupportedStorageCannotClaimCompleteness(Type fixture)
    {
        var (shell, _) = Project(fixture);

        Assert.False(shell.ImportedStorage!.IsComplete);
        _ = Assert.IsType<ErrorType>(Assert.Single(shell.ImportedStorage.Fields).Type);
    }

    [Theory]
    [InlineData(typeof(InlineStorage))]
    [InlineData(typeof(FixedStorage))]
    public void RepeatedPhysicalStorageKeepsAnOpaqueRemainder(Type fixture)
    {
        var (shell, _) = Project(fixture);

        Assert.False(shell.ImportedStorage!.IsComplete);
        _ = Assert.Single(shell.ImportedStorage.Fields);
    }

    [Fact]
    public void ReferenceAssemblyStorageRetainsKnownFieldsAndAnOpaqueRemainder()
    {
        var (shell, resolver) = Project(typeof(PrimitiveStorage));

        StorageProjector.Project(shell, new ReferenceAssemblyType(typeof(PrimitiveStorage)), resolver);

        Assert.False(shell.ImportedStorage!.IsComplete);
        Assert.Equal(4, shell.ImportedStorage.Fields.Count);
        Assert.Same(shell.FieldsList[0], shell.ImportedStorage.Fields.Single(field => field.Name == nameof(PrimitiveStorage.Visible)).Field);
    }

    [Fact]
    public void UnreadableMetadataCannotBecomeAnEmptyCompleteLayout()
    {
        var (shell, resolver) = Project(typeof(PrimitiveStorage));

        StorageProjector.Project(shell, new UnreadableStorageType(typeof(PrimitiveStorage)), resolver);

        Assert.False(shell.ImportedStorage!.IsComplete);
        Assert.Empty(shell.ImportedStorage.Fields);
    }

    private static (StructType Shell, AmbientTypeResolver Resolver) Project(Type fixture)
    {
        var shell = Assert.IsType<StructType>(TypeProjector.CreateShell(fixture, fixture.Namespace!));
        var resolver = new AmbientTypeResolver(
            new Dictionary<string, DeclaredType> { [AmbientTypeResolver.BuildFQN(fixture)] = shell },
            new Dictionary<string, IMistType>
            {
                ["System.Int32"] = new StructType("Int32", "Standard", null, null, null),
                ["System.String"] = new ClassType("String", "Standard", null, null, null),
                ["System.Void"] = new StructType("Void", "Standard", null, null, null),
                ["System.ValueType"] = new ClassType("ValueType", "System", null, null, null),
            },
            new TypeInterner(), new DiagnosticBag());
        Assert.True(TypeParameterProjector.Initialize(shell, fixture));
        BaseTypeProjector.Project(shell, fixture, resolver);
        FieldProjector.Project(shell, fixture, resolver);
        return (shell, resolver);
    }

    private sealed class ReferenceAssemblyType(Type type) : TypeDelegator(type)
    {
        public override Assembly Assembly => new ReferenceAssembly();

        public override IList<CustomAttributeData> GetCustomAttributesData()
        {
            return typeImpl.GetCustomAttributesData();
        }
    }

    private sealed class ReferenceAssembly : Assembly
    {
        public override IList<CustomAttributeData> GetCustomAttributesData()
        {
            return [new ReferenceAssemblyMarker()];
        }
    }

    private sealed class ReferenceAssemblyMarker : CustomAttributeData
    {
        public override Type AttributeType => typeof(ReferenceAssemblyAttribute);
    }

    private sealed class UnreadableStorageType(Type type) : TypeDelegator(type)
    {
        public override IList<CustomAttributeData> GetCustomAttributesData()
        {
            return typeImpl.GetCustomAttributesData();
        }

        public override FieldInfo[] GetFields(BindingFlags bindingAttr)
        {
            throw new TypeLoadException("Storage metadata is unavailable.");
        }
    }
}
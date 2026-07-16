using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures;
using ParameterModifier = Mist.ProgramModel.Model.ParameterModifier;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

public sealed class AmbientTypeProviderV2AttributeProjectionTests(
    AmbientTypeProviderV2AttributeProjectionFixture fixture)
    : IClassFixture<AmbientTypeProviderV2AttributeProjectionFixture>
{
    private const string MarkerAttributeName = "Mist.Tests.InteropFixtures.ProjectionMarkerAttribute";
    private const string InAttributeName = "System.Runtime.InteropServices.InAttribute";
    private const string OutAttributeName = "System.Runtime.InteropServices.OutAttribute";
    private const string IsReadOnlyAttributeName = "System.Runtime.CompilerServices.IsReadOnlyAttribute";

    [Fact]
    public void CustomAttribute_PreservesPositionalAndNamedArguments()
    {
        var attribute = GetMarker(fixture.TargetType);

        // Positional arguments preserve string, Type, enum, and array values.
        Assert.Equal(4, attribute.PositionalArguments.Count);
        Assert.Equal("type", attribute.PositionalArguments[0]);
        Assert.Equal("System.String", Assert.IsAssignableFrom<Type>(attribute.PositionalArguments[1]).FullName);
        Assert.Equal(1, Convert.ToInt32(attribute.PositionalArguments[2]));
        var values = Assert.IsType<object?[]>(attribute.PositionalArguments[3]);
        Assert.Equal(new object?[] { 2, 4 }, values);

        // Named arguments cover both a property (Order) and a field (Category).
        Assert.Contains(attribute.NamedArguments, argument =>
            argument.Name == "Order" && Convert.ToInt32(argument.Value) == 1);
        Assert.Contains(attribute.NamedArguments, argument =>
            argument.Name == "Category" && (string?)argument.Value == "root");
    }

    [Fact]
    public void CustomAttributes_ProjectAcrossAllSupportedMemberDestinations()
    {
        var target = fixture.TargetType;
        var destinations = new (IHasAttributes Target, string Label)[]
        {
            (target, "type"),
            (target.FieldsList.Single(field => field.Name == nameof(AttributeProjectionTarget.Value)), "field"),
            (target.PropertiesList.Single(property => property.Name == nameof(AttributeProjectionTarget.Number)), "property"),
            (target.ConstructorsList.Single(), "constructor"),
            (target.MethodsList.Single(method => method.Name == nameof(AttributeProjectionTarget.Transform)), "method"),
            (target.IndexersList.Single(), "indexer"),
            (target.OperatorsList.Single(), "operator"),
            (target.CastOperatorsList.Single(), "cast"),
        };

        foreach(var destination in destinations)
        {
            var attribute = GetMarker(destination.Target);
            Assert.Equal(destination.Label, attribute.PositionalArguments[0]);
        }
    }

    [Fact]
    public void CustomAttributes_ProjectAcrossAllSupportedParameterDestinations()
    {
        var target = fixture.TargetType;
        var parameters = new (Parameter Parameter, string Label)[]
        {
            (target.ConstructorsList.Single().ParametersList.Single(), "constructor-parameter"),
            (target.MethodsList.Single(method => method.Name == nameof(AttributeProjectionTarget.Transform)).ParametersList.Single(), "method-parameter"),
            (target.IndexersList.Single().ParametersList.Single(), "indexer-parameter"),
            (target.OperatorsList.Single().ParametersList[0], "operator-left-parameter"),
            (target.OperatorsList.Single().ParametersList[1], "operator-right-parameter"),
            (target.CastOperatorsList.Single().SourceParameter, "cast-parameter"),
        };

        foreach(var parameter in parameters)
        {
            var attribute = GetMarker(parameter.Parameter);
            Assert.Equal(parameter.Label, Assert.Single(attribute.PositionalArguments));
        }
    }

    [Fact]
    public void ByRefParameters_RecordModifierAndOnlyStoreOutAttribute()
    {
        var target = fixture.TargetType;
        var inParameter = MethodParameter(target, nameof(AttributeProjectionTarget.ReadIn));
        var outParameter = MethodParameter(target, nameof(AttributeProjectionTarget.WriteOut));
        var refParameter = MethodParameter(target, nameof(AttributeProjectionTarget.Mutate));
        var valueParameter = MethodParameter(target, nameof(AttributeProjectionTarget.Transform));

        // 'in' => readonly + ref. The convention lives entirely in the modifier; no attribute is stored.
        Assert.Equal(
            new ParameterModifier(IsReadonly: true, IsRef: true, OwnershipKind.Unrestricted),
            inParameter.Modifier);
        Assert.DoesNotContain(inParameter.AttributesList, attribute =>
            attribute.AttributeType.FullyQualifiedName is InAttributeName or OutAttributeName or IsReadOnlyAttributeName);

        // 'out' => mutable ref. Only 'out' is stored as an attribute, since the modifier cannot distinguish it from plain 'ref'.
        Assert.Equal(
            new ParameterModifier(IsReadonly: false, IsRef: true, OwnershipKind.Unrestricted),
            outParameter.Modifier);
        Assert.Contains(outParameter.AttributesList, attribute =>
            attribute.AttributeType.FullyQualifiedName == OutAttributeName);

        // Plain 'ref' => mutable ref, no calling-convention attribute.
        Assert.Equal(
            new ParameterModifier(IsReadonly: false, IsRef: true, OwnershipKind.Unrestricted),
            refParameter.Modifier);
        Assert.DoesNotContain(refParameter.AttributesList, attribute =>
            attribute.AttributeType.FullyQualifiedName is InAttributeName or OutAttributeName or IsReadOnlyAttributeName);

        // By value => neither ref nor readonly. CLR never yields an ownership tier.
        Assert.Equal(
            new ParameterModifier(IsReadonly: false, IsRef: false, OwnershipKind.Unrestricted),
            valueParameter.Modifier);
    }

    [Fact]
    public void ParamArrayAttribute_ProjectsFromClrParameterMetadata()
    {
        var clrParameter = typeof(AttributeProjectionTarget)
            .GetMethod(nameof(AttributeProjectionTarget.CountParams))!
            .GetParameters()
            .Single();
        var clrAttributeType = typeof(ParamArrayAttribute);
        var shell = TypeProjector.CreateShell(clrAttributeType, clrAttributeType.Namespace!);
        var shells = new Dictionary<string, MistType>
        {
            [clrAttributeType.FullName!] = shell,
        };
        var resolver = new AmbientTypeResolver(
            shells,
            new Dictionary<string, IMistType>(),
            new TypeInterner(),
            new DiagnosticBag());
        var projectedAttributes = new List<AttributeData>();

        AttributeProjector.Project(clrParameter, projectedAttributes, new Scope(null), resolver);

        var attribute = Assert.Single(projectedAttributes);
        Assert.Equal("System.ParamArrayAttribute", attribute.AttributeType.FullyQualifiedName);
        Assert.Empty(attribute.PositionalArguments);
        Assert.Empty(attribute.NamedArguments);
    }

    [Fact]
    public void ReadonlyStruct_UsesModifierWithoutCompilerInternalTypeAttribute()
    {
        var target = fixture.ReadonlyStructType;

        Assert.True(target.IsReadonly);
        Assert.DoesNotContain(target.AttributesList, attribute =>
            attribute.AttributeType.FullyQualifiedName == IsReadOnlyAttributeName);
    }

    private static Parameter MethodParameter(ClassType target, string methodName)
    {
        return target.MethodsList.Single(method => method.Name == methodName).ParametersList.Single();
    }

    private static AttributeData GetMarker(IHasAttributes target)
    {
        return Assert.Single(target.Attributes, attribute =>
            attribute.AttributeType.FullyQualifiedName == MarkerAttributeName);
    }
}

public sealed class AmbientTypeProviderV2AttributeProjectionFixture : IDisposable
{
    private readonly MetadataLoadContext metadataContext;
    private readonly AmbientTypeProviderV2 provider;
    private readonly ProgramModelClass programModel;

    public AmbientTypeProviderV2AttributeProjectionFixture()
    {
        programModel = new ProgramModelClass();
        RegisterStandardTypes(programModel);

        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var assemblyPaths = Directory.GetFiles(runtimeDirectory, "*.dll")
            .Append(typeof(AttributeProjectionTarget).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
        var resolver = new PathAssemblyResolver(assemblyPaths);
        metadataContext = new MetadataLoadContext(resolver);
        var loadedAssemblies = AmbientTypeProviderV2Factory.LoadAssemblies(
            assemblyPaths,
            metadataContext,
            new DiagnosticBag());

        provider = new AmbientTypeProviderV2(loadedAssemblies, null, new DiagnosticBag());
        provider.BuildAndRegister(programModel);
    }

    internal ClassType TargetType => (ClassType)programModel.TypesByFQN["Mist.Tests.InteropFixtures.AttributeProjectionTarget"];

    internal StructType ReadonlyStructType => (StructType)programModel.TypesByFQN["Mist.Tests.InteropFixtures.ReadonlyAttributeProjectionTarget"];

    public void Dispose()
    {
        provider.Dispose();
        metadataContext.Dispose();
    }

    private static void RegisterStandardTypes(ProgramModelClass programModel)
    {
        _ = programModel.GetOrAddNamespace("Standard");
        var scope = new Scope(null);
        var standardTypes = new[]
        {
            "Int32", "Int64", "Int16", "Byte", "SByte",
            "UInt32", "UInt64", "UInt16", "Single", "Double", "Decimal",
            "String", "Boolean", "Char", "Void",
            "Task", "Task<>", "ValueTask", "ValueTask<>"
        };

        foreach(var name in standardTypes)
        {
            _ = programModel.RegisterType(new ClassType(name, "Standard", scope, null, null!));
        }

        _ = programModel.RegisterType(new AbstractType("IEnumerable<>", "Standard", scope, null, null));
        _ = programModel.RegisterType(new AbstractType("IEnumerator<>", "Standard", scope, null, null));
        _ = programModel.RegisterType(new ClassType("Array<,>", "Standard", scope, null, null!));

        for(var arity = 1; arity <= 17; arity++)
        {
            var commas = arity > 1 ? new string(',', arity - 1) : "";
            _ = programModel.RegisterType(new ClassType($"Func<{commas}>", "Standard", scope, null, null!));
        }

        _ = programModel.RegisterType(new ClassType("Action", "Standard", scope, null, null!));
        for(var arity = 1; arity <= 16; arity++)
        {
            var commas = arity > 1 ? new string(',', arity - 1) : "";
            _ = programModel.RegisterType(new ClassType($"Action<{commas}>", "Standard", scope, null, null!));
        }
    }
}
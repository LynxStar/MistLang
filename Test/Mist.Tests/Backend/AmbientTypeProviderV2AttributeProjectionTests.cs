using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AmbientTypeProviderV2AttributeProjectionTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    private const string MarkerAttributeName = "Mist.Tests.InteropFixtures.ProjectionMarkerAttribute";
    private const string InAttributeName = "System.Runtime.InteropServices.InAttribute";
    private const string OutAttributeName = "System.Runtime.InteropServices.OutAttribute";
    private const string IsReadOnlyAttributeName = "System.Runtime.CompilerServices.IsReadOnlyAttribute";

    [Fact]
    public void CustomAttribute_PreservesPositionalAndNamedArguments()
    {
        var attribute = GetMarker(Target);

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
        var target = Target;
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
        var target = Target;
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
    public void ByRefParameters_RecordTheirDirectionInTheModifier()
    {
        var target = Target;
        var inParameter = MethodParameter(target, nameof(AttributeProjectionTarget.ReadIn));
        var outParameter = MethodParameter(target, nameof(AttributeProjectionTarget.WriteOut));
        var refParameter = MethodParameter(target, nameof(AttributeProjectionTarget.Mutate));
        var valueParameter = MethodParameter(target, nameof(AttributeProjectionTarget.Transform));

        // 'in' => readonly + ref. The convention lives entirely in the modifier.
        Assert.Equal(
            new ParameterModifier(IsReadonly: true, IsRef: true, OwnershipKind.Unrestricted, ByRefDirection.In),
            inParameter.Modifier);

        // 'out' => mutable ref.
        Assert.Equal(
            new ParameterModifier(IsReadonly: false, IsRef: true, OwnershipKind.Unrestricted, ByRefDirection.Out),
            outParameter.Modifier);

        // THE TWO ROWS THAT DEFEAT A FLAG-ONLY READING. Both set the metadata In flag exactly as a
        // real 'in' does, and both are mutable refs requiring 'ref' at a call site. Only a readonly
        // MARKER separates them from the genuine article, which is why the direction is decided by
        // the marker rather than by the flag.
        var markedIn = MethodParameter(target, nameof(AttributeProjectionTarget.MutateMarkedIn));
        Assert.Equal(
            new ParameterModifier(IsReadonly: false, IsRef: true, OwnershipKind.Unrestricted, ByRefDirection.Ref),
            markedIn.Modifier);

        var markedInOut = MethodParameter(target, nameof(AttributeProjectionTarget.MutateMarkedInOut));
        Assert.Equal(
            new ParameterModifier(IsReadonly: false, IsRef: true, OwnershipKind.Unrestricted, ByRefDirection.Ref),
            markedInOut.Modifier);

        // 'ref readonly' carries RequiresLocation rather than IsReadOnly, and is readonly all the same.
        var refReadonly = MethodParameter(target, nameof(AttributeProjectionTarget.ReadRefReadonly));
        Assert.Equal(
            new ParameterModifier(IsReadonly: true, IsRef: true, OwnershipKind.Unrestricted, ByRefDirection.In),
            refReadonly.Modifier);

        // Plain 'ref' => mutable ref.
        Assert.Equal(
            new ParameterModifier(IsReadonly: false, IsRef: true, OwnershipKind.Unrestricted, ByRefDirection.Ref),
            refParameter.Modifier);

        // By value => neither ref nor readonly. CLR never yields an ownership tier.
        Assert.Equal(
            new ParameterModifier(IsReadonly: false, IsRef: false, OwnershipKind.Unrestricted),
            valueParameter.Modifier);
    }

    /// <summary>
    /// Only <c>out</c> is stored as an attribute, since the modifier cannot distinguish it from a plain
    /// <c>ref</c>; in/ref/readonly are the modifier's alone. Judged with shells of the three attribute
    /// types, which live only in the core library and so project from no fixture.
    /// </summary>
    [Theory]
    [InlineData(nameof(AttributeProjectionTarget.ReadIn), false)]
    [InlineData(nameof(AttributeProjectionTarget.WriteOut), true)]
    [InlineData(nameof(AttributeProjectionTarget.Mutate), false)]
    [InlineData(nameof(AttributeProjectionTarget.ReadRefReadonly), false)]
    [InlineData(nameof(AttributeProjectionTarget.Transform), false)]
    public void OnlyOutIsStoredAsAnAttribute(string methodName, bool storesOut)
    {
        var clrParameter = typeof(AttributeProjectionTarget).GetMethod(methodName)!.GetParameters().Single();
        var shells = new Dictionary<string, DeclaredType>();
        foreach(var attributeType in new[] { typeof(InAttribute), typeof(OutAttribute), typeof(IsReadOnlyAttribute) })
        {
            shells[attributeType.FullName!] = TypeProjector.CreateShell(attributeType, attributeType.Namespace!);
        }

        var resolver = new AmbientTypeResolver(shells, new Dictionary<string, IMistType>(), new TypeInterner(), new DiagnosticBag());
        var projectedAttributes = new List<AttributeData>();

        AttributeProjector.Project(clrParameter, projectedAttributes, new Scope(null), resolver);

        var conventions = projectedAttributes
            .Select(attribute => attribute.AttributeType.FullyQualifiedName)
            .Where(name => name is InAttributeName or OutAttributeName or IsReadOnlyAttributeName);
        Assert.Equal(storesOut ? [OutAttributeName] : [], conventions);
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
        var shells = new Dictionary<string, DeclaredType>
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
        var target = Assert.IsType<StructType>(
            fixture.Model.TypesByFQN[$"Mist.Tests.InteropFixtures.{nameof(ReadonlyAttributeProjectionTarget)}"]);

        Assert.True(target.IsReadonly);
        Assert.DoesNotContain(target.AttributesList, attribute =>
            attribute.AttributeType.FullyQualifiedName == IsReadOnlyAttributeName);
    }

    private ClassType Target => fixture.Type(nameof(AttributeProjectionTarget));

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
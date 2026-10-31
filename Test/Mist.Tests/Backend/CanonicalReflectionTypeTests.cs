using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

public sealed class CanonicalReflectionTypeTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Int32")]
    [InlineData("Type")]
    public void ClrIdentityIsMappedToItsStandardDeclaration(string name)
    {
        Assert.Equal(("Standard", name), WellKnownTypeMap.Entries[("System", name)]);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(false)]
    [InlineData(true)]
    public void ClrTypeReferencesPreferTheCanonicalDeclarationOverAProjectedShell(bool inbound)
    {
        var canonical = new HandBuiltModel().Type("Type");
        var resolver = Resolver(canonical);
        var scope = new Scope(null);

        var resolved = inbound
            ? resolver.ResolveInbound(typeof(Type), scope, declaredNonNull: true)
            : resolver.Resolve(typeof(Type), scope);

        Assert.Same(canonical, resolved);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void NullableClrTypeResultsRetainTheCanonicalConstituent()
    {
        var canonical = new HandBuiltModel().Type("Type");

        var result = Resolver(canonical).ResolveInbound(typeof(Type), new Scope(null));

        var optional = Assert.IsType<UnionType>(result);
        Assert.Contains(canonical, optional.Constituents);
        Assert.Contains(optional.Constituents, type => type.FullyQualifiedName == "Standard.Void");
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ClrTypeArraysCarryCanonicalElements()
    {
        var canonical = new HandBuiltModel().Type("Type");

        var result = Resolver(canonical).Resolve(typeof(Type[]), new Scope(null));

        Assert.Same(canonical, Assert.IsType<ConstructedType>(result).TypeArguments[0]);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void CanonicalDescriptorValuesResolveTheirDeclaredReflectionMethods()
    {
        var canonical = new HandBuiltModel().Type("Type");
        var methods = new Method("GetMethods", ErrorType.Instance, [], DeclarationFragments.Public)
        {
            DeclaringType = canonical,
        };
        canonical.MethodsList.Add(methods);

        var result = TypeMemberLookup.Find(canonical, new LookupRequest("GetMethods", MemberVisibility.InstanceOnly, null));

        Assert.Same(methods, Assert.Single(Assert.IsType<MethodGroupResult>(result).Methods));
    }

    [Fact]
    [Trait("Tier", "4")]
    public void AmbientMapToProjectionUsesOneIdentityForQualifiedNamesAndBothSignatureDirections()
    {
        var model = new ProgramModel.Model.ProgramModel();
        AmbientProviderTestHelper.RegisterStandardTypes(model);
        var canonical = model.TypesByFQN["Standard.Type"];
        using var provider = AmbientProviderTestHelper.ProviderOver(
            new DiagnosticBag(), typeof(ProjectionMarkerAttribute).Assembly.Location);

        provider.BuildAndRegister(model);

        Assert.Same(canonical, Assert.IsType<TypeResult>(model.LookupQualified("System", "Type")).Type);
        Assert.False(model.TypesByFQN.ContainsKey("System.Type"));
        var attribute = Assert.IsType<ClassType>(model.TypesByFQN[typeof(ProjectionMarkerAttribute).FullName!]);
        var constructor = Assert.Single(attribute.ConstructorsList, constructor => constructor.ParametersList.Count == 4);
        Assert.Same(canonical, constructor.ParametersList[1].Type);
        var property = Assert.Single(attribute.PropertiesList, property => property.Name == "TargetType");
        Assert.Contains(canonical, Assert.IsType<UnionType>(property.Type).Constituents);
        Assert.Equal(MistAssemblyKind.Standard, canonical.Assembly.Kind);
    }

    private static AmbientTypeResolver Resolver(ClassType canonical)
    {
        var built = new HandBuiltModel();
        return new AmbientTypeResolver(
            new Dictionary<string, DeclaredType> { ["System.Type"] = new ClassType("Type", "System", null, null, null) },
            new Dictionary<string, IMistType>
            {
                ["System.Type"] = canonical,
                ["System.Void"] = built.Type("Void"),
                ["System.Int32"] = built.Type("Int32"),
                ["System.Array"] = built.Type("Array<,>"),
            }, built.Interner, new DiagnosticBag());
    }
}
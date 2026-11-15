using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures;
using Mist.Tests.InteropFixtures.ConstraintProjection;

namespace Mist.Tests.Backend;

// What BuildAndRegister leaves in the model, read back through the provider's own lookup: the loaded
// assemblies name the CLR type and ProjectionOf answers its projection.
[Trait("Tier", "2")]
public sealed class AmbientTypeProviderV2IntegrationProjectionTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    private const string Fixtures = "Mist.Tests.InteropFixtures";
    private const string Constraints = "Mist.Tests.InteropFixtures.ConstraintProjection";

    [Theory]
    [InlineData($"{Fixtures}.{nameof(SimpleClass)}", $"{Fixtures}.{nameof(SimpleClass)}")]
    [InlineData($"{Constraints}.{nameof(Methods)}", $"{Constraints}.{nameof(Methods)}")]
    [InlineData($"{Fixtures}.Bag`1", $"{Fixtures}.Bag<>")]
    public void TheProvidersProjection_IsTheInstanceRegisteredUnderItsMistName(string clrName, string mistName)
    {
        var projected = fixture.Projected(clrName);

        Assert.NotNull(projected);
        Assert.Same(fixture.Model.TypesByFQN[mistName], projected);
        Assert.Equal(mistName, projected!.FullyQualifiedName);
    }

    [Theory]
    [InlineData(Fixtures, nameof(SimpleClass))]
    [InlineData(Fixtures, nameof(SimpleStruct))]
    [InlineData(Fixtures, nameof(ISimpleInterface))]
    [InlineData(Constraints, nameof(Methods))]
    public void AProjectedType_IsRegisteredInItsClrNamespace(string clrNamespace, string typeName)
    {
        var projected = Assert.IsAssignableFrom<DeclaredType>(fixture.Projected($"{clrNamespace}.{typeName}"));

        Assert.Equal(clrNamespace, projected.NamespaceName);
        Assert.Contains(clrNamespace, fixture.Model.Namespaces.Keys);
    }

    [Theory]
    [InlineData($"{Fixtures}.{nameof(SimpleClass)}")]
    [InlineData($"{Fixtures}.{nameof(SimpleStruct)}")]
    [InlineData($"{Fixtures}.{nameof(ISimpleInterface)}")]
    [InlineData($"{Fixtures}.{nameof(SimpleEnum)}")]
    [InlineData($"{Fixtures}.Bag`1")]
    [InlineData($"{Fixtures}.{nameof(HasNullableMembers)}")]
    [InlineData($"{Fixtures}.{nameof(TupleSignatures)}")]
    [InlineData($"{Constraints}.{nameof(Methods)}")]
    public void AProjectedType_IsAttributedToTheAmbientAssembly(string clrName)
    {
        var projected = fixture.Projected(clrName);

        Assert.NotNull(projected);
        Assert.Equal(MistAssemblyKind.Ambient, projected!.Assembly.Kind);
    }

    // An alias answers the existing Standard instance, which keeps its standard library attribution rather
    // than being re-attributed as an ambient projection -- asked for directly or met in a signature.
    [Fact]
    public void AnAliasedType_KeepsTheStandardInstanceAndAssembly()
    {
        var text = fixture.Model.TypesByFQN["Standard.String"];
        var simpleClass = fixture.Type(nameof(SimpleClass));
        var name = simpleClass.PropertiesList.Single(property => property.Name == nameof(SimpleClass.Name));

        Assert.Same(text, fixture.Provider.ProjectionOf(typeof(string)));
        Assert.Same(text, name.Type);
        Assert.Equal(MistAssemblyKind.Standard, text.Assembly.Kind);
    }

    [Fact]
    public void ATypeNoLoadedAssemblyDeclares_HasNoProjection()
    {
        Assert.Null(fixture.Projected($"{Fixtures}.NoSuchType"));
        Assert.Null(fixture.Provider.ProjectionOf(typeof(Uri)));
    }
}
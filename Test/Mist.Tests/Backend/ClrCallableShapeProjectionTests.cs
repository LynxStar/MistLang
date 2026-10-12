using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// The callable shapes the real ambient provider reads off the runtime's metadata, and what selection and
// lookup make of them. Decisions over hand-built shapes are NativeCallFormTests and NativeCallSelectionTests.
[Trait("Tier", "3")]
public sealed class ClrCallableShapeProjectionTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    [Fact]
    public void AnOptionalClrParameter_ProjectsWithItsNativeDefault()
    {
        var serialize = Methods("System.Text.Json.JsonSerializer", "Serialize").Single(method =>
            method.TypeParametersList.Count == 1
            && method.ParametersList is [{ Type: TypeParameter }, { Name: "options" }]);

        Assert.Equal([false, true], serialize.ParametersList.Select(parameter => parameter.HasNativeDefault));
    }

    [Fact]
    public void AParamsArrayAndAParamsSpan_ProjectFlattened()
    {
        var combine = Methods("System.IO.Path", "Combine");

        Assert.Equal(2, combine.Count(method => method.ParametersList is [{ IsFlattened: true }]));
        Assert.All(combine.Where(method => method.ParametersList.Count > 1), method => Assert.DoesNotContain(method.ParametersList, parameter => parameter.IsFlattened));
    }

    [Theory]
    [InlineData(2, "String")]
    [InlineData(5, "ReadOnlySpan")]
    public void AProjectedParamsCall_SelectsTheOverloadCSharpSelects(int count, string expected)
    {
        var text = fixture.Model.TypesByFQN["Standard.String"];
        var arguments = Enumerable.Range(0, count).Select(_ => (Expression)HandBuiltModel.Literal(text)).ToList();

        var choice = OverloadResolution.Select(Methods("System.IO.Path", "Combine"), arguments, null, null, fixture.Model.TypeInterner);

        Assert.StartsWith(expected, Assert.IsType<Method>(choice.Selected).ParametersList[0].Type.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOmittedOptionalClrArgument_StillSelectsItsOverload()
    {
        var integer = fixture.Model.TypesByFQN["Standard.Int32"];

        var choice = OverloadResolution.Select(
            Methods("System.Text.Json.JsonSerializer", "Serialize"), [HandBuiltModel.Literal(integer)], null, [integer], fixture.Model.TypeInterner);

        Assert.Equal("options", Assert.IsType<Method>(choice.Selected).ParametersList[1].Name);
    }

    [Fact]
    public void AClrClass_ReachesNoIndexerItsInterfacesDeclareExplicitly()
    {
        var dictionary = fixture.Model.TypeInterner.GetOrCreateConstructedType(
            fixture.Model.TypesByFQN["System.Collections.Generic.Dictionary<,>"],
            [fixture.Model.TypesByFQN["Standard.String"], fixture.Model.TypesByFQN["Standard.Int32"]]);

        var indexer = Assert.Single(Indexability.Indexers(dictionary));

        _ = Assert.IsType<TypeParameter>(Assert.Single(indexer.ParametersList).Type);
    }

    [Fact]
    public void AClrClass_ReachesNoMethodItsInterfacesDeclareExplicitly()
    {
        var list = (MistType)fixture.Model.TypesByFQN["System.Collections.Generic.List<>"];

        var adds = Assert.IsType<MethodGroupResult>(TypeMemberLookup.Find(list, LookupRequest.Member("Add", MemberVisibility.InstanceOnly, null)));

        Assert.All(adds.Methods, add => Assert.Same(list, add.DeclaringType));
    }

    [Fact]
    public void AStandardIntrinsicAndTheClrTypeItEmitsAs_AreOneType()
    {
        Assert.True(ConversionRelation.IsIdentity(
            fixture.Model.TypesByFQN["Standard.List<>"], fixture.Model.TypesByFQN["System.Collections.Generic.List<>"]));
    }

    private List<Method> Methods(string type, string name)
    {
        return [.. ((MemberType)fixture.Model.TypesByFQN[type]).MethodsList.Where(method => method.Name == name && method.Modifiers.IsStatic)];
    }
}
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AmbientCallableProjectionTests(AmbientTypeProviderV2LinqInteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2LinqInteropFixture>
{
    [Fact]
    public void EnumerableSelectorAndPredicateKeepTheirOpenTypeParametersAndDistinctArities()
    {
        var enumerable = fixture.AmbientType("System.Linq.Enumerable");
        var selectors = enumerable.MethodsList.Where(method => method.Name == "Select").ToList();
        var predicates = enumerable.MethodsList.Where(method => method.Name == "Where").ToList();

        Assert.Equal(2, selectors.Count);
        Assert.Equal(2, predicates.Count);
        foreach(var selector in selectors)
        {
            var callback = Assert.IsType<FunctionType>(selector.ParametersList[1].Type);
            Assert.Same(selector.TypeParametersList[0], callback.ParameterTypes[0]);
            Assert.Same(selector.TypeParametersList[1], callback.ReturnType);
        }

        foreach(var predicate in predicates)
        {
            var callback = Assert.IsType<FunctionType>(predicate.ParametersList[1].Type);
            Assert.Same(predicate.TypeParametersList[0], callback.ParameterTypes[0]);
            Assert.Equal("Standard.Boolean", callback.ReturnType.FullyQualifiedName);
        }

        Assert.Equal([1, 2], selectors.Select(method => Assert.IsType<FunctionType>(method.ParametersList[1].Type).ParameterTypes.Count).Order());
        Assert.Equal([1, 2], predicates.Select(method => Assert.IsType<FunctionType>(method.ParametersList[1].Type).ParameterTypes.Count).Order());
    }

    [Fact]
    public void AProjectedGenericDelegateCarriesItsNamedTupleSignatureOnInvoke()
    {
        var definition = fixture.Type("EchoTuple<>");
        var parameter = Assert.Single(definition.TypeParametersList);

        var invoke = definition.MethodsList.Single(method => method.Name == "Invoke");

        Assert.Same(parameter, invoke.ReturnType);
        Assert.Equal(3, invoke.ParametersList.Count);
        Assert.Same(parameter, invoke.ParametersList[0].Type);
        Assert.Same(parameter, invoke.ParametersList[1].Type);
        var nested = Assert.IsType<NamedTupleType>(invoke.ParametersList[2].Type);
        Assert.Equal(["Value", "Edge"], nested.ElementNames);
        Assert.Same(parameter, nested.ElementTypes[0]);
        var edge = Assert.IsType<NamedTupleType>(nested.ElementTypes[1]);
        Assert.Equal(["Fixed", null], edge.ElementNames);
        Assert.Equal("Standard.Int32", edge.ElementTypes[1].FullyQualifiedName);
    }
}
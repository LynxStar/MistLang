using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class AmbientCallableProjectionTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
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
    public void AProjectedGenericFixtureDelegateInvokesItsClosedNamedTupleSignature()
    {
        var f = new BindingFragments();
        var definition = fixture.Type("EchoTuple<>");
        var integer = fixture.Model.TypesByFQN["Standard.Int32"];
        var tuple = new NamedTupleType(new TupleType([integer, integer]), ["X", "Y"]);
        var callable = fixture.Model.TypeInterner.GetOrCreateConstructedType(definition, [tuple]);
        var shape = Assert.IsType<FunctionType>(DelegateShape.Of(callable));
        _ = f.Local("callback", callable);
        for(var i = 0; i < shape.ParameterTypes.Count; i++)
        {
            _ = f.Local($"input{i}", shape.ParameterTypes[i]);
        }

        var suffix = BindingFragments.At<InvocationSuffixPostfixSuffixNode>();
        suffix.Arguments = BindingFragments.Arguments([.. Enumerable.Range(0, shape.ParameterTypes.Count)
            .Select(i => BindingFragments.Expression(BindingFragments.Identifier($"input{i}")))]);

        var call = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("callback"), suffix));

        Assert.Null(call.Method);
        Assert.Same(definition.MethodsList.Single(method => method.Name == "Invoke"), call.InvokeMethod);
        Assert.Same(callable, call.Receiver.Type);
        Assert.Same(tuple, call.Type);
        Assert.Same(tuple, call.ParameterTypes![0]);
        Assert.Same(tuple, call.ParameterTypes[1]);
        var nested = Assert.IsType<NamedTupleType>(call.ParameterTypes[2]);
        Assert.Equal(["Value", "Edge"], nested.ElementNames);
        Assert.Same(tuple, nested.ElementTypes[0]);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }
}
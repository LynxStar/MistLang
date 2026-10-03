using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "3")]
public sealed class ProjectedCallableOverloadSelectionTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    [Theory]
    [InlineData("Select", false)]
    [InlineData("Select", true)]
    [InlineData("Where", false)]
    public void LinqProjectedExtensionSelectsTheClosedUnarySignature(string name, bool explicitResult)
    {
        var f = new BindingFragments();
        var model = fixture.Model;
        var integer = model.TypesByFQN["Standard.Int32"];
        var blocks = model.Namespaces["System.Linq"].Extensions.Values.SelectMany(blocks => blocks)
            .Where(block => block.ResolvedReceiverType is ConstructedType receiver
                && receiver.Definition.FullyQualifiedName == "Standard.IEnumerable<>"
                && block.MethodsList.Any(method => method.Name == name && method.ParametersList[0].Type is FunctionType))
            .ToList();
        var methods = blocks.SelectMany(block => block.MethodsList.Where(method => method.Name == name)
            .Select(method => ExtensionSpecialization.Close(method, block, [integer], model.TypeInterner)))
            .ToList();
        Assert.Equal(2, methods.Count);
        var body = name == "Select"
            ? BindingFragments.Expression(BindingFragments.Identifier("x"))
            : BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode());
        var lambda = new UnboundLambdaExpression(BindingFragments.Lambda(body));

        var choice = OverloadResolution.Select(methods, new Expression[] { lambda }, null,
            explicitResult ? [integer] : null, model.TypeInterner, lambdas: new LambdaTrials(f.Binding, f.Builder));

        var selected = Assert.IsType<Method>(choice.Selected);
        var callback = Assert.IsType<FunctionType>(choice.Substitution.Apply(Assert.Single(selected.ParametersList).Type, model.TypeInterner));
        Assert.Same(integer, Assert.Single(callback.ParameterTypes));
        Assert.Equal(name == "Select" ? "Standard.Int32" : "Standard.Boolean", callback.ReturnType.FullyQualifiedName);
        var result = Assert.IsType<ConstructedType>(choice.Substitution.Apply(selected.ReturnType, model.TypeInterner));
        Assert.Same(integer, Assert.Single(result.TypeArguments));
        if(name == "Select")
        {
            Assert.Equal(1, Assert.Single(selected.TypeParametersList).Ordinal);
            Assert.Same(integer, choice.Substitution.Apply(Assert.Single(selected.TypeParametersList), model.TypeInterner));
        }

        var settled = Assert.IsType<LambdaExpression>(LambdaTrials.BindAgainst(lambda, callback, f.Binding, f.Builder));
        Assert.Same(integer, Assert.Single(settled.Parameters).Type);
        Assert.Equal(callback.ReturnType.FullyQualifiedName, Assert.IsType<FunctionType>(settled.Type).ReturnType.FullyQualifiedName);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }
}
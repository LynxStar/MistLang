using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ConstraintClosureForwardingTests
{
    private readonly HandBuiltModel model = new();
    private readonly ConstraintClosureRegistry registry = new();
    private readonly ClassType factory = new("Factory<>", "Probe", new Scope(null), null, null);

    public ConstraintClosureForwardingTests()
    {
        var product = new TypeParameter("T", 0, factory.Scope, factory);
        product.ConstraintsList.Add(new ConstructorConstraint([model.Type("Int32")]));
        factory.TypeParametersList.Add(product);
    }

    [Fact]
    public void AConcreteArgumentClosesTheParameterizedConstructorConstraint()
    {
        var widget = HandBuiltModel.Class("Widget");

        registry.RecordType(factory, [widget], HandBuiltModel.Node(40));

        var closure = Assert.Single(registry.Closures);
        Assert.Same(widget, closure.TypeArgument);
        Assert.Equal([model.Type("Int32")], closure.Closed);
    }

    [Fact]
    public void AForwardedTypeParameterClosesNothingButIsKeptAsAnOpenClosing()
    {
        var wrapper = HandBuiltModel.Class("Wrapper");
        IMistType forwarded = new TypeParameter("U", 0, wrapper.Scope, wrapper);

        registry.RecordType(factory, [forwarded], HandBuiltModel.Node(40));

        Assert.Empty(registry.Closures);
        Assert.Empty(registry.Closings);
        Assert.Same(forwarded, Assert.Single(registry.OpenClosings).Argument);
    }
}
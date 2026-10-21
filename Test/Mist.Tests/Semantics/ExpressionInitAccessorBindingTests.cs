using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ExpressionInitAccessorBindingTests
{
    private readonly BindingFragments fragments = new();

    public ExpressionInitAccessorBindingTests()
    {
        fragments.Register(Int);
    }

    private IMistType Int => fragments.Built.Type("Int32");

    [Fact]
    public void AnExpressionBodiedInitAccessorIsAnInitOnlySetterThatKeepsItsExpression()
    {
        var body = BindingFragments.Expression(BindingFragments.Identifier("value"));
        var host = Build(body);

        var property = Assert.Single(host.PropertiesList);
        var setter = Assert.IsType<PropertyAccessor>(property.Setter);
        Assert.True(setter.IsInit);
        Assert.False(setter.IsAuto);
        Assert.Null(setter.Body);
        Assert.Same(body, Assert.IsType<UnresolvedExpression>(setter.ExpressionBody).Node);
        Assert.True(Assert.IsType<PropertyAccessor>(property.Getter).IsAuto);
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void TheInitExpressionReadsTheImplicitValueAtThePropertyType()
    {
        var host = Build(BindingFragments.Expression(BindingFragments.Identifier("value")));
        var property = Assert.Single(host.PropertiesList);

        BodyBinder.BindAccessors(property.Getter, property.Setter, property.Type, [], isStatic: false, property, host.Scope, fragments.Builder);

        var read = Assert.IsType<LocalReferenceExpression>(property.Setter!.ExpressionBody);
        Assert.Equal("value", read.Local.Name);
        Assert.Same(Int, read.Local.Type);
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    private ClassType Build(ExpressionNode initBody)
    {
        var initAccessor = BindingFragments.At<ExpressionInitAccessorAccessorDeclarationNode>(40);
        initAccessor.Expression = initBody;
        var declaration = BindingFragments.At<BlockPropertyPropertyDeclarationNode>();
        declaration.Type = BindingFragments.TypeName("Int32");
        declaration.Name = BindingFragments.Token("Count");
        declaration.Accessors = [BindingFragments.At<AutoGetAccessorAccessorDeclarationNode>(35), initAccessor];

        var host = new ClassType("Holder", "Probe", fragments.Types, null, null);
        new PropertyBuilder(fragments.Builder, fragments.Builder.Resolver, new AccessorBuilder(fragments.Builder)).Resolve(declaration, host);
        return host;
    }
}
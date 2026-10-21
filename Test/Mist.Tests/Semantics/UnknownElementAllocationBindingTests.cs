using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class UnknownElementAllocationBindingTests
{
    [Fact]
    public void AnUnknownElementTypeAllocatesAnArrayOfTheTopType()
    {
        var fragments = new BindingFragments();
        var int32 = fragments.Built.Type("Int32");
        fragments.Register(int32);
        var array = fragments.Built.Type("Array<,>");
        array.TypeParametersList.Add(new TypeParameter("TValue", 0, array.Scope, array));
        array.TypeParametersList.Add(new TypeParameter("Length", 1, array.Scope, array) { IsDependent = true, Domain = int32 });
        fragments.Register(array);
        var size = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        size.IntegerLiteral = BindingFragments.Token("3");
        var node = BindingFragments.At<ArrayCreationExpressionPrimaryNode>();
        node.Type = new UnknownElementTypeRefElementTypeRefNode();
        node.Size = BindingFragments.Expression(size);

        var allocation = Assert.IsType<ArrayCreationExpression>(ExpressionBinder.BindPrimary(node, fragments.Binding, fragments.Builder));

        Assert.Same(UnknownType.Instance, allocation.ElementType);
        var constructed = Assert.IsType<ConstructedType>(allocation.Type);
        Assert.Same(array, constructed.Definition);
        Assert.Same(UnknownType.Instance, constructed.TypeArguments[0]);
    }
}
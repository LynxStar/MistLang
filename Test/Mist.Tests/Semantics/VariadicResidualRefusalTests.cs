using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class VariadicResidualRefusalTests
{
    [Fact]
    public void SpreadSuffixOnValueParameterIsRefusedAtItsDeclaration()
    {
        var f = Fixture();
        var node = BindingFragments.At<ParameterNode>(20);
        node.Name = BindingFragments.Token("args");
        node.Type = Spread();

        var parameter = Assert.Single(new ParameterBuilder(f.Builder.Resolver, f.Builder.Attributes, f.Diagnostics)
            .Resolve(new ParameterListNode { Parameter = node }, f.Types));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_SPREAD_VALUE_PARAMETER", diagnostic.Id.Value);
        Assert.Contains("params", diagnostic.Message);
        Assert.Equal(21, diagnostic.Location.Start.Column);
        Assert.True(parameter.IsFailed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TypeArgumentSpreadNeedsAnAbsorbingSlot(bool acceptsPack, bool construction)
    {
        var f = Fixture();
        var family = HandBuiltModel.Class("Family<>");
        family.TypeParametersList.Add(new TypeParameter("T", 0, family.Scope, family)
        {
            PackKind = acceptsPack ? TypePackKind.Unbracketed : TypePackKind.None,
            Domain = f.Built.Type("Type"),
        });
        f.Register(family);
        var syntax = BindingFragments.At<NamedSingleTypeRefSingleTypeRefNode>();
        syntax.Name = BindingFragments.Name("Family");
        syntax.TypeArgs = new TypeArgumentListNode
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = Spread() },
        };

        if(construction)
        {
            var creation = BindingFragments.At<ObjectCreationExpressionPrimaryNode>();
            creation.Type = syntax.Name;
            creation.TypeArgs = syntax.TypeArgs;
            _ = f.Bind(creation);
        }
        else
        {
            _ = f.Builder.Resolver.Resolve(new TypeReferenceNode { First = syntax }, f.Types);
        }

        if(acceptsPack)
        {
            Assert.Empty(f.Diagnostics.ToImmutableArray());
        }
        else
        {
            var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
            Assert.Equal("MIST_PM_SPREAD_NON_PACK_ARGUMENT", diagnostic.Id.Value);
            Assert.Equal(41, diagnostic.Location.Start.Column);
        }
    }

    private static BindingFragments Fixture()
    {
        var f = new BindingFragments();
        f.Register(new TypeParameter("Ts", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed });
        return f;
    }

    private static TypeReferenceNode Spread()
    {
        var spread = BindingFragments.At<SpreadSingleTypeRefSingleTypeRefNode>(40);
        spread.Name = BindingFragments.Name("Ts");
        return new TypeReferenceNode { First = spread };
    }
}
using Mist.Generated;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class BareVariadicConstructionBindingTests
{
    [Fact]
    public void NewOnAPackOnlyFamilyWithNoArgumentListConstructsItsZeroWideClosing()
    {
        var f = new BindingFragments();
        var tup = HandBuiltModel.Class("Tup<>");
        var domain = HandBuiltModel.Class("TypeArray");
        tup.TypeParametersList.Add(new TypeParameter("TArgs", 0, tup.Scope, tup) { PackKind = TypePackKind.Unbracketed, Domain = domain });
        f.Register(tup);
        var creation = BindingFragments.At<ObjectCreationExpressionPrimaryNode>();
        creation.Type = BindingFragments.Name("Tup");

        var bound = Assert.IsType<NewExpression>(f.Bind(creation));

        var zeroWide = f.Built.Interner.GetOrCreateConstructedType(tup, [f.Built.Interner.LiftPack(domain, new TypePackValue([]))]);
        Assert.Same(zeroWide, bound.Type);
        Assert.Empty(bound.Arguments);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }
}
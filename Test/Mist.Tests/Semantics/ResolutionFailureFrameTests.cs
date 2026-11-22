using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ResolutionFailureFrameTests
{
    [Fact]
    public void AFailureInANestedResolutionFailsTheEnclosingOneButNotItsCleanSibling()
    {
        var f = new BindingFragments();
        var alias = Deferred(f);
        TypeResolution failed = default;
        TypeResolution clean = default;
        f.Builder.Resolver.AliasCompleter = completing =>
        {
            failed = f.Builder.Resolver.ResolveWithFailure(BindingFragments.TypeName("Missing"), f.Types);
            clean = f.Builder.Resolver.ResolveWithFailure(BindingFragments.TypeName("Int32"), f.Types);
            completing.AliasedType = f.Built.Type("Int32");
        };

        var outer = f.Builder.Resolver.ResolveWithFailure(BindingFragments.TypeName("Deferred"), f.Types);

        Assert.True(failed.IsFailed);
        Assert.False(clean.IsFailed);
        Assert.True(outer.IsFailed);
        Assert.False(alias.IsFailed);
        Assert.Same(f.Built.Type("Int32"), outer.Type);
        Assert.Equal("MIST0298", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Theory]
    [InlineData("Missing", true)]
    [InlineData("Int32", false)]
    public void AFinishedNestedResolutionHandsLaterFailuresBackToTheEnclosingOne(string later, bool expected)
    {
        var f = new BindingFragments();
        _ = Deferred(f);
        TypeResolution nested = default;
        f.Builder.Resolver.AliasCompleter = completing =>
        {
            nested = f.Builder.Resolver.ResolveWithFailure(BindingFragments.TypeName("Int32"), f.Types);
            completing.AliasedType = f.Built.Type("Int32");
        };
        var function = new TypeReferenceNode
        {
            First = new FuncSingleTypeRefSingleTypeRefNode { TypeArgs = BindingFragments.TypeArguments("Deferred", later) },
        };

        var outer = f.Builder.Resolver.ResolveWithFailure(function, f.Types);

        Assert.False(nested.IsFailed);
        Assert.Equal(expected, outer.IsFailed);
        Assert.Equal(expected ? 1 : 0, f.Diagnostics.ToImmutableArray().Length);
    }

    private static TypeAlias Deferred(BindingFragments f)
    {
        var alias = new TypeAlias("Deferred", "Probe", f.Types, null, BindingFragments.At<TypeAliasDeclarationNode>());
        f.Register(alias);
        return alias;
    }
}
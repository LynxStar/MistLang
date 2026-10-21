using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class OwnedConstraintResolutionTests
{
    [Fact]
    public void TheOwnedConstraintKeywordResolvesToTheOwnedKeywordConstraint()
    {
        var fragments = new BindingFragments();
        var resolved = new List<TypeParameterConstraint>();

        new ConstraintResolver(fragments.Builder, fragments.Builder.Resolver).ResolveConstraintList(
            new ConstraintListNode { Constraint = new OwnedConstraintConstraintNode(), Constraints = [] }, resolved, fragments.Scope);

        var constraint = Assert.IsType<KeywordConstraint>(Assert.Single(resolved));
        Assert.Equal(TypeParameterConstraintKind.Owned, constraint.Kind);
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }
}
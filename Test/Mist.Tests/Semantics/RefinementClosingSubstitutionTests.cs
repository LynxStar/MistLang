using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 1: TypeSubstitution.Apply over a refinement whose bound names the dependent parameter N.
[Trait("Tier", "1")]
public sealed class RefinementClosingSubstitutionTests
{
    private readonly HandBuiltRefinements h = new();
    private readonly TypeParameter size = new("N", 0, new Scope(null), null) { IsDependent = true };
    private readonly TypeParameter element = new("T", 1, new Scope(null), null);

    [Fact]
    public void ANamedDependentTheSubstitutionBinds_IsCarriedBesideTheSharedPredicate()
    {
        var declared = BelowSize();
        var three = new LiftedType(h.Int, 3L);

        var closed = Assert.IsType<RefinedType>(TypeSubstitution.ForParameters([size], [three]).Apply(declared, interner: null));

        Assert.NotSame(declared, closed);
        Assert.Same(declared, closed.Declaration);
        Assert.Same(declared.Predicate, closed.Predicate);
        Assert.Same(three, closed.Closing[size]);
        Assert.False(closed.IsSubstituted);
    }

    [Fact]
    public void ASubstitutionBindingNothingThePredicateNames_KeepsTheRefinementItself()
    {
        var declared = BelowSize();

        Assert.Same(declared, TypeSubstitution.ForParameters([element], [h.String]).Apply(declared, interner: null));
    }

    [Fact]
    public void ARefinementClosedTwice_ComposesTheClosings()
    {
        var forwarded = new TypeParameter("K", 0, new Scope(null), null) { IsDependent = true };
        var three = new LiftedType(h.Int, 3L);
        var once = (RefinedType)TypeSubstitution.ForParameters([size], [forwarded]).Apply(BelowSize(), interner: null);

        var twice = Assert.IsType<RefinedType>(TypeSubstitution.ForParameters([forwarded], [three]).Apply(once, interner: null));

        Assert.Same(three, twice.Closing[size]);
    }

    [Fact]
    public void ASubstitutedBase_StillCarriesTheClosing()
    {
        var declared = HandBuiltRefinements.RefinePattern(element, h.Compare(element, ComparisonKind.LessThan, new TypeReferenceExpression(HandBuiltModel.Node(), size)));
        var three = new LiftedType(h.Int, 3L);

        var closed = Assert.IsType<RefinedType>(TypeSubstitution.ForParameters([size, element], [three, h.Int]).Apply(declared, interner: null));

        Assert.Same(h.Int, closed.BaseType);
        Assert.Same(three, closed.Closing[size]);
        Assert.True(closed.IsSubstituted);
    }

    private RefinedType BelowSize()
    {
        return HandBuiltRefinements.RefinePattern(h.Int, h.Compare(h.Int, ComparisonKind.LessThan, new TypeReferenceExpression(HandBuiltModel.Node(), size)), name: null);
    }
}
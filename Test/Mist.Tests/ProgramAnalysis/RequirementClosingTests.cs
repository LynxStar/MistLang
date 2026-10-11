using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class RequirementClosingTests
{
    private readonly HandBuiltRefinements h = new();
    private readonly TypeParameter size = new("N", 0, new Scope(null), null) { IsDependent = true };

    [Fact]
    public void ADependentBoundClosedWithAValue_IsThatInterval()
    {
        var required = Requirement.OfType(Closed(new LiftedType(h.Int, 3L)))!;

        Assert.Empty(required.Bounds);
        Assert.True(required.Values.IsSubsetOf(Admitted.Between(0, 2)));
        Assert.True(Admitted.Between(0, 2).IsSubsetOf(required.Values));
    }

    [Fact]
    public void ADependentBoundClosedWithAnotherParameter_NamesThatParameterUnderItsWrittenName()
    {
        var forwarded = new TypeParameter("K", 0, new Scope(null), null) { IsDependent = true };

        var bound = Assert.Single(Requirement.OfType(Closed(forwarded))!.Bounds);

        Assert.Same(forwarded, bound.Named);
        Assert.Equal("N", bound.Name);
    }

    [Fact]
    public void ADependentBoundClosedWithNoValue_StaysSymbolic()
    {
        var bound = Assert.Single(Requirement.OfType(Closed(new UnknownValueType(h.Int)))!.Bounds);

        Assert.Same(size, bound.Named);
    }

    [Fact]
    public void ABoundNamingAParameterLocal_NamesTheParameter()
    {
        var upper = new Parameter("hi", h.Int);
        var local = new LocalVariable("hi", h.Int) { Parameter = upper };

        Assert.Same(upper, Assert.Single(Bounds(HandBuiltRefinements.Read(local))).Named);
    }

    [Fact]
    public void ABoundNamingAPlainLocal_NamesTheLocal()
    {
        var local = new LocalVariable("limit", h.Int);

        Assert.Same(local, Assert.Single(Bounds(HandBuiltRefinements.Read(local))).Named);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AMemberBound_RecordsTheLocalItIsReadThrough(bool throughALocal)
    {
        var owner = HandBuiltModel.Class("Tally");
        var count = new Property("Count", h.Int, new MemberModifiers()) { DeclaringType = owner };
        var receiver = new LocalVariable("self", owner);
        Expression through = throughALocal ? HandBuiltRefinements.Read(receiver) : new ThisExpression(HandBuiltModel.Node(), owner);

        var bound = Assert.Single(Bounds(new MemberAccessExpression(
            HandBuiltModel.Node(), through, "Count", h.Int, member: new PropertyResult(count, owner, []))));

        Assert.Same(count, bound.Member);
        Assert.Same(throughALocal ? receiver : null, bound.Receiver);
    }

    private RefinedType Closed(IMistType value)
    {
        var declared = HandBuiltRefinements.RefinePattern(h.Int, new ConjunctionPattern(HandBuiltModel.Node(), h.Int,
            [h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, 0L), h.Compare(h.Int, ComparisonKind.LessThan, new TypeReferenceExpression(HandBuiltModel.Node(), size))]),
            name: null);

        return new RefinedType(declared, declared.BaseType, new Dictionary<TypeParameter, IMistType> { [size] = value });
    }

    private IReadOnlyList<SymbolicBound> Bounds(Expression bound)
    {
        return Requirement.Of(h.Compare(h.Int, ComparisonKind.LessThan, bound))!.Bounds;
    }
}
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// A dependent parameter's <c>where</c> read as its refinement inside its own type, judged by the refinement
/// analyzer over hand-built bodies: <c>new int[Capacity]</c> under a <c>where</c> that keeps it from being negative is a
/// length the facts prove, as under a domain that names the refinement (adhoc-refinement-constraints Requirement 4.1,
/// flow-sensitive-refinement-inference Requirement 1.1).
/// </summary>
[Trait("Tier", "2")]
public sealed class WhereRefinementFlowTests
{
    private readonly HandBuiltSlots slots = new();

    private (MemberType Ring, TypeParameter Capacity) Ring(long floor)
    {
        var ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        var capacity = ring.TypeParametersList[0];
        capacity.RefinementNode = HandBuiltModel.Node();
        capacity.Refinement = new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.GreaterThanOrEqual, HandBuiltModel.Literal(slots.Int, floor));
        return (ring, capacity);
    }

    private ArrayCreationExpression Sized(TypeParameter capacity)
    {
        var length = new SlotCapture(capacity.Name, slots.ZeroOrPositive, slots.Int, new TypeReferenceExpression(HandBuiltModel.Node(), capacity), 3);
        return slots.Create(length);
    }

    private IReadOnlyList<Diagnostic> InMethod(long floor)
    {
        var (ring, capacity) = Ring(floor);
        return InMethod(ring, capacity);
    }

    private IReadOnlyList<Diagnostic> InMethod(MemberType ring, TypeParameter capacity)
    {
        var made = Sized(capacity);
        var method = new Method("Make", made.Type, [], new MemberModifiers()) { DeclaringType = ring };

        return slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(HandBuiltRefinements.Evaluate(made)), owner: ring, member: method);
    }

    [Fact]
    public void ALengthTheWhereProvesIsAdmittedInAMethod()
    {
        Assert.Empty(InMethod(0));
    }

    [Fact]
    public void ALengthTheWhereProvesIsAdmittedInAFieldInitializer()
    {
        var (ring, capacity) = Ring(0);
        var made = Sized(capacity);
        var field = new Field("Last", slots.ArrayOf(capacity), OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = ring, Initializer = made };

        Assert.Empty(slots.Refinements.Built.Analyze(new RefinementAnalyzer(), made, owner: ring, member: field));
    }

    [Fact]
    public void AWhereThatAdmitsANegativeLengthLeavesItRefused()
    {
        var refused = Assert.Single(InMethod(-5));

        Assert.Equal("MIST0272", refused.Number.Value);
        Assert.Contains("fails when Capacity <= -1", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWhereThatRelatesTheSlotToAnotherSlotLeavesItsLengthRefused()
    {
        var (pair, low) = Ring(0);
        var high = new TypeParameter("B", 1, pair.Scope, pair) { IsDependent = true, Domain = (MistType)slots.Int };
        pair.TypeParametersList.Add(high);
        high.RefinementNode = HandBuiltModel.Node();
        high.Refinement = new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.GreaterThanOrEqual, new TypeReferenceExpression(HandBuiltModel.Node(), low));

        var refused = Assert.Single(InMethod(pair, high));

        Assert.Equal("MIST0272", refused.Number.Value);
        Assert.Contains("fails when B <= -1", refused.Message, StringComparison.Ordinal);
    }
}
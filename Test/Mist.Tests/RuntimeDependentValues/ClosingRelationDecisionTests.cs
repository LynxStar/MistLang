using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "1")]
public sealed class ClosingRelationDecisionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly FactReading reading = new(new LiftedReturns());
    private readonly TypeParameter low;
    private readonly TypeParameter high;

    public ClosingRelationDecisionTests()
    {
        (low, high) = Pair("P<,>", "A", "B");
        Where(low, new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.GreaterThanOrEqual, HandBuiltModel.Literal(slots.Int, 0L)));
        Where(high, AtLeast(low));
    }

    private (TypeParameter Low, TypeParameter High) Pair(string name, string lowName, string highName)
    {
        var pair = HandBuiltSlots.Generic(HandBuiltModel.Class(name), lowName, slots.Int);
        var second = new TypeParameter(highName, 1, pair.Scope, pair) { IsDependent = true, Domain = (MistType)slots.Int };
        pair.TypeParametersList.Add(second);
        return (pair.TypeParametersList[0], second);
    }

    private ComparisonPattern AtLeast(TypeParameter bound)
    {
        return new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.GreaterThanOrEqual, new TypeReferenceExpression(HandBuiltModel.Node(), bound));
    }

    private static void Where(TypeParameter parameter, Pattern predicate)
    {
        parameter.RefinementNode = HandBuiltModel.Node();
        parameter.Refinement = predicate;
    }

    private Dictionary<TypeParameter, IMistType> Closing(IMistType a, IMistType b)
    {
        return new Dictionary<TypeParameter, IMistType>(ReferenceEqualityComparer.Instance) { [low] = a, [high] = b };
    }

    private Proof? Static(IMistType a, IMistType b)
    {
        return ClosingRelations.Judge(
            FactState.Empty, reading, high, b, Closing(a, b), filled => !filled.Any(argument => argument is CapturedValue),
            (ref FactState at, TypeParameter sibling, IMistType filled) => SlotReads.Value(ref at, reading, filled, null, sibling));
    }

    private Proof? Mixed(FactState state, SlotCapture a, IMistType b)
    {
        return ClosingRelations.Judge(
            state, reading, high, b, Closing(a.Value, b), filled => filled.Any(argument => argument is CapturedValue),
            (ref FactState at, TypeParameter sibling, IMistType filled) => SlotReads.Value(ref at, reading, filled, null, sibling));
    }

    [Fact]
    public void TheRelationOfAWhereIsOnlyTheBoundsThatNameAnotherSlot()
    {
        var relation = ClosingRelations.Of(high);

        Assert.True(relation?.Values.IsEverything);
        Assert.Same(low, Assert.Single(relation!.Bounds).Named);
        Assert.Null(ClosingRelations.Of(low));
    }

    [Fact]
    public void AConstantClosingIsJudgedWithEachSlotItNamesReadAsItsConstant()
    {
        _ = Assert.IsType<Refuted>(Static(slots.Literal(0), slots.Literal(-2)));
        _ = Assert.IsType<Proven>(Static(slots.Literal(1), slots.Literal(3)));
        _ = Assert.IsType<Proven>(Static(slots.Literal(3), slots.Literal(3)));
    }

    [Fact]
    public void AForwardedClosingIsJudgedOnlyByTheForwardedParametersOwnWheres()
    {
        var (x, y) = Pair("R<,>", "X", "Y");
        Assert.True(Static(x, y) is Refuted);

        Where(x, new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.GreaterThanOrEqual, HandBuiltModel.Literal(slots.Int, 0L)));
        Where(y, new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.GreaterThanOrEqual, HandBuiltModel.Literal(slots.Int, 0L)));
        Assert.True(Static(x, y) is Refuted);

        Where(y, AtLeast(x));
        _ = Assert.IsType<Proven>(Static(x, y));
    }

    [Fact]
    public void AMixedClosingIsJudgedOverTheFactsInHandOfTheCaptureItNames()
    {
        var a = slots.Capture(new LocalVariable("a", slots.Int));
        var value = new Value(slots.Int, "a", HandBuiltModel.Node());
        var held = FactState.Empty with { Bindings = Bindings.Empty.Set(a, value), Bag = Bag.Empty.Add(Fact.In(value, IntegralSet.Of(Admitted.AtLeast(0)))) };
        var bounded = held with { Bag = held.Bag.Add(Fact.In(value, IntegralSet.Of(Admitted.AtMost(1)))) };

        _ = Assert.IsType<Refuted>(Mixed(held, a, slots.Literal(1)));
        _ = Assert.IsType<Proven>(Mixed(bounded, a, slots.Literal(1)));
        Assert.Null(Static(a.Value, slots.Literal(1)));
    }

    [Fact]
    public void ASlotClosedWithUnknownOrByACaptureOfItsOwnOwesNothingAtTheClosing()
    {
        var b = slots.Capture(new LocalVariable("b", slots.Int));

        Assert.Null(Static(UnknownType.Instance, slots.Literal(3)));
        Assert.Null(Static(slots.Literal(0), b.Value));
    }
}
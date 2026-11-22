using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "1")]
public sealed class SlotReadsDecisionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly FactReading reading = new(new LiftedReturns());
    private readonly MemberType ring;
    private readonly TypeParameter capacity;

    public SlotReadsDecisionTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        capacity = ring.TypeParametersList[0];
    }

    private static MemberAccessExpression Access(Expression receiver, TypeParameter parameter)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(), receiver, parameter.Name, parameter.Domain!, member: new TypeResult(parameter));
    }

    [Fact]
    public void ALiteralSlotReadsAsItsConstant()
    {
        var state = FactState.Empty;

        var read = SlotReads.Value(ref state, reading, slots.Literal(4), null, capacity);

        Assert.NotNull(read);
        Assert.Empty(read.Variables);
        Assert.Equal(4, read.Constant);
    }

    [Fact]
    public void ACapturedSlotReadsAsTheValueItsLocalHolds()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.ZeroOrPositive));
        var held = new Value(slots.Int, "n", HandBuiltModel.Node());
        var state = FactState.Empty with { Bindings = Bindings.Empty.Set(capture, held) };

        var read = SlotReads.Value(ref state, reading, capture.Value, null, capacity);

        Assert.Equal([held], read?.Variables);
    }

    [Fact]
    public void ACreationsOwnCaptureReadThroughThatCreationIsTheValueItsLocalHoldsAndAnyOtherValuesIsItsHolder()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.ZeroOrPositive));
        var held = new Value(slots.Int, "n", HandBuiltModel.Node());
        var made = new NewExpression(HandBuiltModel.Node(), slots.Closed(ring, capture.Value), [], null, [], []) { Captures = [capture] };
        var other = new LocalVariable("other", slots.Closed(ring, capture.Value));
        var state = FactState.Empty with { Bindings = Bindings.Empty.Set(capture, held) };

        var read = SlotReads.Value(ref state, reading, capture.Value, made, capacity);
        var through = SlotReads.Value(ref state, reading, capture.Value, HandBuiltRefinements.Read(other), capacity);

        Assert.Equal([held], read?.Variables);
        Assert.Equal([state.Bindings.Get(new MemberHolder(state.Bindings.Get(other)!, capacity))!], through?.Variables);
    }

    [Fact]
    public void ACastCarriesItsOperandsSlotAndRecordsNoneOfTheCaptureItsTypeWrites()
    {
        var named = slots.Capture(new LocalVariable("n", slots.ZeroOrPositive));
        var q = new LocalVariable("q", slots.Closed(ring, named.Value));
        var operand = HandBuiltRefinements.Read(q);
        var cast = new CastExpression(HandBuiltModel.Node(), operand, slots.Closed(ring, named.Value), [], slots.Closed(ring, named.Value)) { Captures = [named] };
        var (instance, slot, written, result) = (Held("q"), Held("m"), Held("n"), Held("cast"));
        var state = FactState.Empty with
        {
            Bindings = Bindings.Empty.Set(named, written).Set(new MemberHolder(instance, capacity), slot),
            Results = FactState.Empty.Results.SetItem(operand, new Term(instance)),
        };

        var carried = SlotReads.Created(state, cast, new Term(result));
        var unknown = SlotReads.Created(state with { Bindings = Bindings.Empty.Set(named, written) }, cast, new Term(result));

        Assert.Same(slot, carried.Bindings.Get(new MemberHolder(result, capacity)));
        Assert.Null(unknown.Bindings.Get(new MemberHolder(result, capacity)));
    }

    [Fact]
    public void AUserOperatorsResultHoldsTheSlotOfTheOperandWhoseValueClosesItWhereItsReturnTypeNamesIt()
    {
        var named = slots.Capture(new LocalVariable("n", slots.ZeroOrPositive));
        var closed = slots.Closed(ring, named.Value);
        var q = new LocalVariable("q", closed);
        var left = HandBuiltRefinements.Read(q);
        var plus = new Operator(OperatorKind.Plus, slots.Closed(ring, capacity), [new Parameter("a", slots.Closed(ring, capacity)), new Parameter("b", slots.Int)],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = ring };
        var sum = new BinaryExpression(HandBuiltModel.Node(), left, BinaryOperatorKind.Add, slots.Refinements.Literal(1L), closed) { Operator = plus, ParameterTypes = [closed, slots.Int] };
        var (instance, slot, result) = (Held("q"), Held("m"), Held("sum"));
        var state = FactState.Empty with
        {
            Bindings = Bindings.Empty.Set(new MemberHolder(instance, capacity), slot),
            Results = FactState.Empty.Results.SetItem(left, new Term(instance)),
        };

        Assert.Same(slot, SlotReads.Created(state, sum, new Term(result)).Bindings.Get(new MemberHolder(result, capacity)));
    }

    private Value Held(string name)
    {
        return new Value(slots.Int, name, HandBuiltModel.Node());
    }

    [Fact]
    public void ADependentParameterReadsAsTheValueBoundUnderIt()
    {
        var parameter = slots.Dependent("N");
        var state = FactState.Empty;

        var first = SlotReads.Value(ref state, reading, parameter, null, capacity);
        var again = SlotReads.Value(ref state, reading, parameter, null, capacity);

        Assert.Equal([state.Bindings.Get(parameter)!], first?.Variables);
        Assert.Equal(first, again);
    }

    [Fact]
    public void AnUnknownSlotOfAClassReadsAsTheValueUnderTheReceiversMemberHolder()
    {
        var local = new LocalVariable("ring", slots.Closed(ring, UnknownType.Instance));
        var held = new Value(local.Type, "ring", HandBuiltModel.Node());
        var state = FactState.Empty with { Bindings = Bindings.Empty.Set(local, held) };

        var read = SlotReads.Value(ref state, reading, UnknownType.Instance, HandBuiltRefinements.Read(local), capacity);
        var again = SlotReads.Value(ref state, reading, UnknownType.Instance, HandBuiltRefinements.Read(local, 3), capacity);

        Assert.Equal([state.Bindings.Get(new MemberHolder(held, capacity))!], read?.Variables);
        Assert.Equal(read, again);
        Assert.Null(SlotReads.Value(ref state, reading, UnknownType.Instance, null, capacity));
    }

    [Fact]
    public void AnUnknownSlotOfAnAbstractIsNeverKeyedByTheObject()
    {
        var sized = (AbstractType)HandBuiltSlots.Generic(new AbstractType("ISized<>", "Probe", new Scope(null), null, null), "N", slots.Int);
        var local = new LocalVariable("some", slots.Closed(sized, new UnknownValueType(slots.Int)));
        var state = FactState.Empty;

        Assert.Null(SlotReads.Value(ref state, reading, new UnknownValueType(slots.Int), HandBuiltRefinements.Read(local), sized.TypeParametersList[0]));
    }

    [Fact]
    public void AnInstanceAccessOfADependentParameterIsASlotReadAndTheReadThroughTheTypeIsNot()
    {
        var instance = Access(HandBuiltRefinements.Local(slots.Closed(ring, slots.Literal(4)), "ring"), capacity);
        var through = Access(new TypeReferenceExpression(HandBuiltModel.Node(), slots.Closed(ring, slots.Literal(4))), capacity);
        var length = slots.Refinements.Length(HandBuiltRefinements.Local(slots.Refinements.String, "text"));

        Assert.Same(capacity, SlotReads.Parameter(instance));
        Assert.Null(SlotReads.Parameter(through));
        Assert.Same(capacity, SlotReads.Member(instance));
        Assert.Equal("Length", SlotReads.Member(length));
        Assert.True(SlotReads.IsLength(length));
        Assert.False(SlotReads.IsLength(instance));
        Assert.True(SlotReads.IsLength(slots.Refinements.Length(HandBuiltRefinements.Local(slots.ArrayOf(slots.Literal(2)), "a"))));
    }

    [Fact]
    public void TheArgumentIsReadFromTheClosingTheReceiverReachesThroughItsBase()
    {
        var nine = slots.Closed(ring, slots.Literal(9));
        var fixedRing = HandBuiltModel.Class("Fixed", nine);
        var direct = Access(HandBuiltRefinements.Local(slots.Closed(ring, slots.Literal(4)), "ring"), capacity);
        var derived = Access(HandBuiltRefinements.Local(fixedRing, "fixed"), capacity);

        Assert.Same(slots.Literal(4), SlotReads.Argument(direct, capacity));
        Assert.Same(slots.Literal(9), SlotReads.Argument(derived, capacity));
    }

    [Fact]
    public void AnArraysLengthIsReadAsASlotAndAStringsIsNot()
    {
        var array = HandBuiltRefinements.Local(slots.ArrayOf(slots.Literal(3)), "a");
        var text = HandBuiltRefinements.Local(slots.Refinements.String, "s");

        var read = SlotReads.LengthOf(array);

        Assert.NotNull(read);
        Assert.Same(slots.Array.TypeParametersList[1], Assert.IsType<TypeResult>(read.Member).Type);
        Assert.Same(slots.Int, read.Type);
        Assert.Null(SlotReads.LengthOf(text));
    }

    [Fact]
    public void APassingTestAddsThatTheScrutineesReadLiesInTheCapturesRefinementAndNotThatItIsTheValue()
    {
        var n = slots.Capture(new LocalVariable("n", slots.ZeroOrPositive));
        var held = new Value(slots.Int, "n", HandBuiltModel.Node());
        var some = new LocalVariable("ring", slots.Closed(ring, UnknownType.Instance));
        var kept = new IntegralSet([new IntegralInterval(11, 29)]);
        var state = FactState.Empty with { Bindings = Bindings.Empty.Set(n, held), Kept = FactState.Empty.Kept.Add(n, kept) };

        var tested = SlotReads.Tested(state, reading, HandBuiltRefinements.Read(some), slots.Closed(ring, n.Value));
        var read = tested.Bindings.Get(new MemberHolder(tested.Bindings.Get(some)!, capacity))!;

        Assert.Equal(kept, tested.Bag.Intervals(read));
        Assert.DoesNotContain(tested.Bag.Common.Concat(tested.Bag.Alternatives.SelectMany(alternative => alternative.Facts)), fact => fact.Term.Variables.Contains(held));
    }

    [Fact]
    public void ATestOfADependentlyClosedScrutineeAddsTheParametersValue()
    {
        var parameter = slots.Dependent("N");
        var buffer = HandBuiltSlots.Generic(HandBuiltModel.Class("Buffer<>"), "Size", slots.Int);
        var b = new LocalVariable("b", slots.Closed(buffer, parameter));
        var state = FactState.Empty;

        var tested = SlotReads.Tested(state, reading, HandBuiltRefinements.Read(b), slots.Closed(buffer, slots.Literal(5)));
        var bound = tested.Bindings.Get(parameter)!;

        Assert.Contains(tested.Bag.Common, fact => fact.Relation == Relation.Equal && fact.Term.Variables.SequenceEqual([bound]) && fact.Term.Constant == -5);
    }

    [Fact]
    public void ATestOfAnAbstractClosedWithUnknownAddsNothing()
    {
        var sized = (AbstractType)HandBuiltSlots.Generic(new AbstractType("ISized<>", "Probe", new Scope(null), null, null), "N", slots.Int);
        var some = new LocalVariable("some", slots.Closed(sized, new UnknownValueType(slots.Int)));
        var state = FactState.Empty;

        var tested = SlotReads.Tested(state, reading, HandBuiltRefinements.Read(some), slots.Closed(sized, slots.Literal(5)));

        Assert.Empty(tested.Bag.Common);
    }

    [Fact]
    public void ASynthesizedReadIsTypedByTheSlotsDomainWhileItsWhereStaysAFactOnTheValue()
    {
        var bounded = HandBuiltSlots.Generic(HandBuiltModel.Class("Bounded<>"), "Capacity", slots.Int);
        var limit = bounded.TypeParametersList[0];
        limit.RefinementNode = HandBuiltModel.Node();
        limit.Refinement = new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.GreaterThanOrEqual, HandBuiltModel.Literal(slots.Int, 1L));
        var state = FactState.Empty;

        var read = SlotReads.Read(HandBuiltRefinements.Local(slots.Closed(bounded, UnknownType.Instance), "b"), limit);
        var value = Assert.Single(reading.Read(ref state, read).Variables);

        Assert.Same(slots.Int, read.Type);
        Assert.Same(limit, Assert.IsType<TypeResult>(read.Member).Type);
        Assert.True(state.Bag.Intervals(value).Intervals.All(interval => interval.Low is { } low && low >= 1));
    }
}
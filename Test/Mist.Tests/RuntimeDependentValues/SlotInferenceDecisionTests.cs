using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// What a method's dependent parameter binds from each kind of argument slot (runtime-dependent-values
/// Requirements 8.3, 8.4 and 11.5).
/// </summary>
[Trait("Tier", "1")]
public sealed class SlotInferenceDecisionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly TypeParameter n;
    private readonly Method len;
    private readonly AbstractType sized;
    private readonly Method count;

    public SlotInferenceDecisionTests()
    {
        var scope = new Scope(null);
        n = new TypeParameter("N", 0, scope, null) { IsDependent = true, Domain = (MistType)slots.Int };
        len = new Method("Len", slots.Int, [new Parameter("a", slots.ArrayOf(n))], default) { Scope = scope };
        len.TypeParametersList.Add(n);
        sized = (AbstractType)HandBuiltSlots.Generic(new AbstractType("ISized<>", "Probe", new Scope(null), null, null), "N", slots.Int);
        count = new Method("Count", slots.Int, [new Parameter("s", slots.Closed(sized, n))], default) { Scope = scope };
        count.TypeParametersList.Add(n);
    }

    private (IMistType?[] Inferred, SlotInference Slots, TypeArgumentConflict? Conflict) Infer(Method method, params Expression[] arguments)
    {
        var inference = new SlotInference(slots.Refinements.Built.Interner);
        var inferred = TypeArgumentInference.Infer(method, arguments, TypeSubstitution.Empty, slots.Refinements.Built.Interner, null, out var conflict, null, inference);
        return (inferred, inference, conflict);
    }

    [Fact]
    public void ALiteralACaptureAndADependentParameterBindAsTheyAre()
    {
        var literal = slots.Literal(3);
        var captured = slots.Capture(new LocalVariable("k", slots.Int)).Value;
        var forwarded = slots.Dependent("M");

        Assert.Same(literal, Infer(len, HandBuiltRefinements.Local(slots.ArrayOf(literal), "a")).Inferred[0]);
        Assert.Same(captured, Infer(len, HandBuiltRefinements.Local(slots.ArrayOf(captured), "a")).Inferred[0]);
        Assert.Same(forwarded, Infer(len, HandBuiltRefinements.Local(slots.ArrayOf(forwarded), "a")).Inferred[0]);
    }

    [Fact]
    public void TheUnknownSlotOfAClassBindsACaptureThatReadsTheArgumentsSlot()
    {
        var a = HandBuiltRefinements.Local(slots.ArrayOf(new UnknownValueType(slots.Int)), "a");

        var (inferred, inference, conflict) = Infer(len, a);

        Assert.Null(conflict);
        var bound = Assert.IsType<CapturedValue>(inferred[0]);
        var capture = Assert.Single(inference.CapturesOf(len));
        Assert.Same(bound.Capture, capture);
        var read = Assert.IsType<MemberAccessExpression>(capture.Initializer);
        Assert.Same(a, read.Receiver);
        Assert.Same(slots.Array.TypeParametersList[1], Assert.IsType<TypeResult>(read.Member).Type);
        Assert.Same(Assert.IsType<ConstructedType>(a.Type).TypeArguments[1], read.Type);
        Assert.Same(slots.Int, capture.Type);
        Assert.Null(inference.RefusedFor(len));
    }

    [Fact]
    public void TheUnknownSlotOfAnAbstractBindsNothingAndIsRefused()
    {
        var some = HandBuiltRefinements.Local(slots.Closed(sized, new UnknownValueType(slots.Int)), "some");

        var (inferred, inference, _) = Infer(count, some);

        Assert.Null(inferred[0]);
        Assert.Empty(inference.CapturesOf(count));
        var refused = inference.RefusedFor(count);
        Assert.Same(n, refused?.Parameter);
        Assert.Same(some.Type, refused?.Type);
    }

    [Fact]
    public void ANamedClosingOfAnAbstractBindsItsValue()
    {
        var one = HandBuiltRefinements.Local(slots.Closed(sized, slots.Literal(1)), "one");

        var (inferred, inference, _) = Infer(count, one);

        Assert.Same(slots.Literal(1), inferred[0]);
        Assert.Null(inference.RefusedFor(count));
    }

    [Fact]
    public void TheFirstArgumentKeepsTheParameterAndTheSecondDoesNotMergeIntoIt()
    {
        var scope = new Scope(null);
        var blit = new Method("Blit", slots.Int, [new Parameter("source", slots.ArrayOf(n)), new Parameter("target", slots.ArrayOf(n))], default) { Scope = scope };
        blit.TypeParametersList.Add(n);
        var first = slots.Capture(new LocalVariable("k", slots.Int)).Value;
        var second = slots.Capture(new LocalVariable("m", slots.Int)).Value;

        var (inferred, _, conflict) = Infer(blit, HandBuiltRefinements.Local(slots.ArrayOf(first), "b"), HandBuiltRefinements.Local(slots.ArrayOf(second), "c"));
        var (literals, _, _) = Infer(blit, HandBuiltRefinements.Local(slots.ArrayOf(first), "b"), HandBuiltRefinements.Local(slots.ArrayOf(slots.Literal(3)), "c"));

        Assert.Null(conflict);
        Assert.Same(first, inferred[0]);
        Assert.Same(first, literals[0]);
    }
}
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Which dependent parameters <see cref="SlotRoutes"/> moves off the C# type parameter list, one
/// rule at a time over hand-built declarations (runtime-dependent-values design, <em>Two routes</em>).
/// </summary>
[Trait("Tier", "1")]
public sealed class SlotRouteDecisionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly ClassType wrapper;
    private readonly ClassType matrix;
    private readonly ClassType buffer;
    private readonly ClassType fixedBox;
    private readonly Method len;

    public SlotRouteDecisionTests()
    {
        ring = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        wrapper = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Wrapper<>"), "N", slots.Int);
        buffer = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Buffer<>"), "N", slots.Int);
        fixedBox = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Fixed<>"), "K", slots.Int);
        matrix = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Matrix<,>"), "Rows", slots.Int);
        matrix.TypeParametersList.Add(new TypeParameter("Cols", 1, matrix.Scope, matrix) { IsDependent = true, Domain = (MistType)slots.Int });
        var n = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        len = new Method("Len", slots.Int, [new Parameter("a", slots.ArrayOf(n))], default) { Scope = new Scope(null) };
        len.TypeParametersList.Add(n);
        n.DeclaringMethod = len;
    }

    private HashSet<TypeParameter> Routed(
        IEnumerable<(IHasTypeParameters Definition, IReadOnlyList<IMistType> Arguments)> closings, params TypeParameter[] reads)
    {
        return SlotRoutes.Discover(closings, reads, [ring, wrapper, matrix, buffer, fixedBox, len]);
    }

    [Fact]
    public void AClosingFilledWithACaptureRoutesItsSlotAndALiteralDoesNot()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));

        var routed = Routed([(ring, [capture.Value]), (fixedBox, [slots.Literal(3)]), (len, [slots.Literal(3)])]);

        Assert.Equal([ring.TypeParametersList[0]], routed);
    }

    [Fact]
    public void AValueRoutedParameterForwardedIntoASlotRoutesThatSlot()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));

        var routed = Routed([(ring, [wrapper.TypeParametersList[0]]), (wrapper, [capture.Value])]);

        Assert.Contains(wrapper.TypeParametersList[0], routed);
        Assert.Contains(ring.TypeParametersList[0], routed);
        Assert.Equal(2, routed.Count);
    }

    [Fact]
    public void ASlotReadThroughUnknownIsRouted()
    {
        var routed = Routed([(buffer, [slots.Literal(4)])], buffer.TypeParametersList[0]);

        Assert.Equal([buffer.TypeParametersList[0]], routed);
    }

    [Fact]
    public void AReadThroughUnknownIsRecognisedAndAReadThroughALiteralIsNot()
    {
        var slot = buffer.TypeParametersList[0];
        var throughUnknown = Read(slots.Closed(buffer, UnknownType.Instance), slot);
        var throughLiteral = Read(slots.Closed(buffer, slots.Literal(4)), slot);

        Assert.Same(slot, SlotRoutes.ReadThroughUnknown(throughUnknown));
        Assert.Null(SlotRoutes.ReadThroughUnknown(throughLiteral));
    }

    [Fact]
    public void ARoutedParameterRoutesEveryDependentParameterOfItsDeclaration()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));

        var routed = Routed([(matrix, [capture.Value, slots.Literal(2)])]);

        Assert.Equal(matrix.TypeParametersList, routed.OrderBy(parameter => parameter.Ordinal));
    }

    [Fact]
    public void ADeclarationNoRuleReachesKeepsItsCarrierAndArrayIsNeverRouted()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));

        var routed = Routed([(fixedBox, [slots.Literal(3)]), (slots.Array, [slots.Int, capture.Value]), (len, [slots.Literal(3)])]);

        Assert.Empty(routed);
    }

    [Fact]
    public void AnAbstractsMethodAndTheMethodAnsweringItTakeOneRoute()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        var (slot, slotParameter) = Measure("N");
        var (answer, answerParameter) = Measure("K");

        var fromSlot = SlotRoutes.Discover([(slot, [capture.Value])], [], [slot, answer], [(slot, answer)]);
        var fromAnswer = SlotRoutes.Discover([(answer, [capture.Value])], [], [slot, answer], [(slot, answer)]);
        var unlinked = SlotRoutes.Discover([(slot, [capture.Value])], [], [slot, answer]);

        Assert.Equal([answerParameter, slotParameter], fromSlot.OrderBy(parameter => parameter.Name));
        Assert.Equal([answerParameter, slotParameter], fromAnswer.OrderBy(parameter => parameter.Name));
        Assert.Equal([slotParameter], unlinked);
    }

    [Fact]
    public void ABlockSlotFilledThroughTheReceiverIsReadFromTheCallAndRoutedWhenCapturedOrInferredFromUnknown()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        var n = slots.Dependent("N");
        var block = new ExtensionBlock("self", "Probe", new Scope(null), new ExtensionBlockDeclarationNode()) { CarrierName = "WrapperExtensions", ResolvedReceiverType = slots.Closed(wrapper, n) };
        block.TypeParametersList.Add(n);
        var size = new Method("Size", slots.Int, [], default) { DeclaringBlock = block, Scope = new Scope(null) };
        block.MethodsList.Add(size);
        var closed = ExtensionSpecialization.Close(size, block, [capture.Value], slots.Refinements.Built.Interner);
        var receiver = HandBuiltRefinements.Local(slots.Closed(wrapper, capture.Value), "w");
        var call = new InvocationExpression(HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Size", slots.Int, []), [], slots.Int, closed, []);
        var plain = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), len, [slots.Literal(3)]), [], slots.Int, len, []);

        var (found, arguments) = Assert.NotNull(SlotRoutes.ClosedThroughReceiver(call));
        var (open, carried) = Assert.NotNull(SlotRoutes.ReceiverSlot(block, n));

        Assert.Same(block, found);
        Assert.Same(capture.Value, Assert.Single(arguments));
        Assert.Null(SlotRoutes.ClosedThroughReceiver(plain));
        Assert.Same(wrapper, open.Definition);
        Assert.Same(wrapper.TypeParametersList[0], carried);
        Assert.Equal([n], SlotRoutes.Discover([(block, [capture.Value])], [], [block]));
        Assert.Empty(SlotRoutes.Discover([(block, [UnknownType.Instance])], [], [block]));
        Assert.Equal([n], SlotRoutes.Discover([(block, [UnknownType.Instance])], [], [block], inferredFromUnknown: [(block, 0)]));
        Assert.Empty(SlotRoutes.Discover([(block, [slots.Literal(3)])], [], [block]));
    }

    [Fact]
    public void AMethodGroupClosesTheBlocksSlotAndASlotItsTargetLeavesUnknownIsInferred()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        var n = slots.Dependent("N");
        var block = new ExtensionBlock("self", "Probe", new Scope(null), new ExtensionBlockDeclarationNode()) { CarrierName = "WrapperExtensions", ResolvedReceiverType = slots.Closed(wrapper, n) };
        block.TypeParametersList.Add(n);
        var size = new Method("Size", slots.Int, [], default) { DeclaringBlock = block, Scope = new Scope(null) };
        block.MethodsList.Add(size);
        var closed = ExtensionSpecialization.Close(size, block, [capture.Value], slots.Refinements.Built.Interner);
        var receiver = HandBuiltRefinements.Local(slots.Closed(wrapper, capture.Value), "w");
        var bound = new FunctionReferenceExpression(HandBuiltModel.Node(), receiver, closed, new FunctionType([], slots.Int));
        var open = new FunctionReferenceExpression(HandBuiltModel.Node(), null, len, new FunctionType([slots.ArrayOf(UnknownType.Instance)], slots.Int)) { TypeArguments = [UnknownType.Instance] };
        var literal = new FunctionReferenceExpression(HandBuiltModel.Node(), null, len, new FunctionType([slots.ArrayOf(slots.Literal(3))], slots.Int)) { TypeArguments = [slots.Literal(3)] };

        var (found, arguments) = Assert.NotNull(SlotRoutes.ClosedThroughReceiver(bound));

        Assert.Same(block, found);
        Assert.Same(capture.Value, Assert.Single(arguments));
        Assert.Null(SlotRoutes.ClosedThroughReceiver(open));
        Assert.Equal([(len, 0)], SlotRoutes.InferredByGroup(open));
        Assert.Empty(SlotRoutes.InferredByGroup(literal));
        Assert.Equal([len.TypeParametersList[0]], SlotRoutes.Discover([(len, [UnknownType.Instance])], [], [len], inferredFromUnknown: SlotRoutes.InferredByGroup(open)));
        Assert.Empty(SlotRoutes.Discover([(len, [UnknownType.Instance])], [], [len]));
    }

    private (Method Method, TypeParameter Parameter) Measure(string parameter)
    {
        var declared = new TypeParameter(parameter, 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var method = new Method("Measure", slots.Int, [], default) { Scope = new Scope(null) };
        method.TypeParametersList.Add(declared);
        declared.DeclaringMethod = method;
        return (method, declared);
    }

    private static MemberAccessExpression Read(ConstructedType receiverType, TypeParameter slot)
    {
        var receiver = HandBuiltRefinements.Local(receiverType, "b");
        return new MemberAccessExpression(HandBuiltModel.Node(), receiver, slot.Name, slot.Domain!, member: new TypeResult(slot));
    }
}
using Mist.Backends.CSharpTranspiler.Lowering;
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

    private static MemberAccessExpression Read(ConstructedType receiverType, TypeParameter slot)
    {
        var receiver = HandBuiltRefinements.Local(receiverType, "b");
        return new MemberAccessExpression(HandBuiltModel.Node(), receiver, slot.Name, slot.Domain!, member: new TypeResult(slot));
    }
}
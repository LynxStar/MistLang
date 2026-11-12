using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "2")]
public sealed class UnflowedClosingTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType ring;

    public UnflowedClosingTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
    }

    private IReadOnlyList<string> ElementInto(IMistType element, IMistType elementType)
    {
        var literal = new ArrayLiteralExpression(
            HandBuiltModel.Node(), elementType, [HandBuiltRefinements.Local(element, "ring")], slots.Closed(slots.Array, elementType, slots.Literal(1)));

        return [.. slots.Refinements.Built.Analyze(new AssignabilityAnalyzer(), HandBuiltRefinements.Evaluate(literal)).Select(diagnostic => diagnostic.Number.Value)];
    }

    [Fact]
    public void AClosingTheFlowHasNoStepForIsRefusedUnlessItIsTheSameCapture()
    {
        var written = slots.Capture(new LocalVariable("n", slots.Int));
        var again = slots.Capture(new LocalVariable("n", slots.Int));

        Assert.Empty(ElementInto(slots.Closed(ring, written.Value), slots.Closed(ring, written.Value)));
        Assert.Empty(ElementInto(slots.Closed(ring, written.Value), slots.Closed(ring, UnknownType.Instance)));
        Assert.Equal(["MIST0486"], ElementInto(slots.Closed(ring, written.Value), slots.Closed(ring, again.Value)));
        Assert.Equal(["MIST0486"], ElementInto(slots.Closed(ring, slots.Dependent("N")), slots.Closed(ring, slots.Literal(5))));
        Assert.Equal(["MIST0486"], ElementInto(slots.Closed(ring, UnknownType.Instance), slots.Closed(ring, slots.Literal(5))));
        Assert.Equal(["MIST0157"], ElementInto(slots.Closed(ring, slots.Literal(3)), slots.Closed(ring, slots.Literal(5))));
    }
}
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "1")]
public sealed class SlotCaptureFlowTests
{
    private readonly HandBuiltSlots slots = new();

    [Fact]
    public void TheWalkerYieldsACapturesInitializerUnderTheNodeThatWroteTheType()
    {
        var n = new LocalVariable("n", slots.ZeroOrPositive);
        var written = slots.Capture(n);
        var source = HandBuiltRefinements.Local(slots.ArrayOf(written.Value), "a");
        var declaration = HandBuiltSlots.Declare(new LocalVariable("b", source.Type), source, written);
        var test = new TypeTestPattern(HandBuiltModel.Node(), source.Type, null) { Captures = [written] };
        var creation = slots.Create(written);

        Assert.Equal([written.Initializer, source], BodyWalker.ChildrenOf(declaration));
        Assert.Equal([written.Initializer], BodyWalker.ChildrenOf(test));
        Assert.Equal([written.Initializer], BodyWalker.ChildrenOf(creation));
    }

    [Fact]
    public void ACaptureIsDeclaredOnceBeforeTheOperandsOfItsNode()
    {
        var n = new LocalVariable("n", slots.ZeroOrPositive);
        var written = slots.Capture(n);
        var source = HandBuiltRefinements.Local(slots.ArrayOf(written.Value), "a");
        var declaration = HandBuiltSlots.Declare(new LocalVariable("b", source.Type), source, written);
        var creation = slots.Create(slots.Capture(n));

        var typed = FlowGraphBuilder.Build(HandBuiltModel.Block(declaration)).Blocks.SelectMany(block => block.Steps).ToList();
        var created = FlowGraphBuilder.Build(creation).Blocks.SelectMany(block => block.Steps).ToList();

        Assert.Equal(
            [(FlowStepKind.Evaluate, written.Initializer), (FlowStepKind.Declare, written.Declaration), (FlowStepKind.Evaluate, source), (FlowStepKind.Declare, declaration)],
            typed.Take(4).Select(step => (step.Kind, step.Node)));
        Assert.Equal(written, typed[1].Variable);
        Assert.Equal(
            [(FlowStepKind.Evaluate, creation.Size), (FlowStepKind.Declare, creation.Captures[0].Declaration), (FlowStepKind.Evaluate, creation)],
            created.Select(step => (step.Kind, step.Node)));
    }
}
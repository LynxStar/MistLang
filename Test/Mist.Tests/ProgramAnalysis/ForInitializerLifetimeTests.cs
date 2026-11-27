using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ForInitializerLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheInitializerIsReleasedAtTheLoopBoundary(bool enterLoop)
    {
        var fragments = new LifetimeFragments();
        var local = fragments.Local("token");
        var initializer = Declare(local, fragments.New(1));
        var earlyReturn = new ReturnStatement(null, null);
        var normalReturn = new ReturnStatement(null, null);
        var condition = HandBuiltModel.Literal(fragments.Boolean, enterLoop);
        var loop = new ForStatement(initializer, condition, null, HandBuiltModel.Block(earlyReturn));
        var body = HandBuiltModel.Block(loop, normalReturn);
        var method = fragments.Function("Use", body);
        var graph = FlowGraphBuilder.Build(body);

        var plan = LocalLifetimes.Plan(body, method, graph: graph);
        var owners = LifetimeOwners.Of(body, method, holder: null);

        Assert.Equal(LifetimeRelease.AtExit, plan.ReleaseOf(initializer));
        Assert.False(owners.IsHome(local, body));

        var steps = new List<FlowStep>();
        var visited = new HashSet<FlowBlock>();
        for(var block = graph.Entry; block != graph.Exit; block = Assert.Single(block.Successors))
        {
            Assert.True(visited.Add(block));
            steps.AddRange(block.Steps);
        }

        var exitOrder = steps
            .Where(step => (step.Kind == FlowStepKind.Leave && owners.IsHome(local, step.Node))
                || step is { Kind: FlowStepKind.Evaluate, Node: ReturnStatement })
            .Select(step => step.Node);
        var expectedExitOrder = enterLoop ? new Statement[] { earlyReturn, loop } : [loop, normalReturn];
        Assert.Equal(expectedExitOrder, exitOrder);
    }
}
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class FactLoopTests
{
    [Fact]
    public void TheSolverRevisitsAnImpossibleBranchFromTheWidenedHeader()
    {
        var h = new HandBuiltRefinements();
        var i = HandBuiltRefinements.Local(h.Int, "i");
        var condition = h.Binary(i, BinaryOperatorKind.GreaterThan, h.Literal(10L));
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(i, h.Literal(0L)),
            new WhileStatement(h.Binary(i, BinaryOperatorKind.LessThan, h.Literal(100L)), HandBuiltModel.Block(
                HandBuiltRefinements.If(condition, HandBuiltRefinements.Evaluate(h.Literal(0L))),
                HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(i, h.Binary(i, BinaryOperatorKind.Add, h.Literal(1L), h.Int))))));
        var graph = FlowGraphBuilder.Build(body);
        var lattice = new Watching(new FactFlow(new HashSet<object>(), new LiftedReturns(), graph: graph), i, condition);

        _ = FlowSolution<FactState>.Solve(graph, lattice, FactState.Empty);

        Assert.Contains(lattice.Branches, branch => !branch.AfterWiden && branch.Proof is Proven);
        Assert.Contains(lattice.Branches, branch => branch.AfterWiden && branch.Proof is Refuted);
        Assert.True(lattice.Steps < 256, $"The solver took {lattice.Steps} steps.");
    }

    private sealed class Watching(FactFlow inner, LocalReferenceExpression local, Expression condition) : IFlowLattice<FactState>
    {
        private bool widened;
        public List<(bool AfterWiden, Proof Proof)> Branches { get; } = [];
        public int Steps { get; private set; }

        public FactState Join(FactState left, FactState right)
        {
            return inner.Join(left, right);
        }

        public bool Same(FactState left, FactState right)
        {
            return inner.Same(left, right);
        }

        public FactState Transfer(FactState state, FlowStep step)
        {
            Assert.True(++Steps < 1000, "The loop did not converge.");
            var result = inner.Transfer(state, step);
            if(step.Kind == FlowStepKind.Assume && step.Holds && ReferenceEquals(step.Node, condition))
            {
                var value = result.Bindings.Get(local.Local)!;
                Branches.Add((widened, Prove(result.Bag, Le(11, (value, -1)), value)));
            }

            return result;
        }

        public FactState Widen(FactState previous, FactState joined, int round)
        {
            widened |= round > 3;
            Assert.True(round < 16, "Widening did not stabilize.");
            return inner.Widen(previous, joined, round);
        }
    }
}
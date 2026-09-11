// Tier 3 (Component): the shape of the flow graph FlowGraphBuilder builds over a real body, and what
// the forward solver reads off it -- the facts every flow-sensitive rule stands on, stated once here
// rather than re-proven through each rule's corpus. The reasoning behind the shape is recorded on
// order 10 of backlog/analysis-v2-capability-progression.yaml.

using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class FlowGraphTests : ProgramModelProbe
{
    public FlowGraphTests()
        : base("MistFlowGraph_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Dog
        {
            public string Name = "d";
        }
        """;

    protected override string Preamble { get; } = """
        var flag = true;
        int | string mixed = 1;
        int[] items = [1, 2];
        """;

    [Theory]
    [InlineData("int x; if(flag) { x = 1; } else { x = 2; } Console.WriteLine(x);")]
    [InlineData("var n = flag && mixed is int k;")]
    [InlineData("int r = mixed switch { int i when i > 0 => i, string s => s.Length, _ => 0 };")]
    [InlineData("int | void o = void; var v = o ?? 1;")]
    [InlineData("while(flag) { foreach(var item in items) { if(item > 1) { break; } } flag = false; }")]
    [InlineData("for(var i = 0; i < 2; i = i + 1) { Console.WriteLine(i); }")]
    [InlineData("func<int, int> f = y => { int z = y + 1; return z; }; Console.WriteLine(f(1));")]
    public void EveryNodeIsEvaluatedOnceOnAnyPathThroughIt(string statement)
    {
        var body = BodyIn(statement);
        var graph = FlowGraphBuilder.Build(body);

        var evaluated = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var inlined = graph.Nested.Keys.SelectMany(creator => BodyWalker.DescendantsAndSelf(creator).Skip(1))
            .ToHashSet(ReferenceEqualityComparer.Instance);

        foreach(var block in graph.Blocks)
        {
            var onThisBlock = block.Steps.Where(step => step.Kind == FlowStepKind.Evaluate).Select(step => step.Node).ToList();
            Assert.Equal(onThisBlock.Count, onThisBlock.Distinct(ReferenceEqualityComparer.Instance).Count());
            evaluated.UnionWith(onThisBlock);
        }

        foreach(var node in BodyWalker.DescendantsAndSelf(body))
        {
            if(node is Expression expression && !inlined.Contains(node) && !IsAssignmentTarget(body, expression))
            {
                Assert.Contains(expression, evaluated);
            }
        }
    }

    [Fact]
    public void ALiteralTrueConditionHasNoFalseExit()
    {
        var body = BodyIn("int x; while(true) { x = 1; break; } Console.WriteLine(x);");
        var graph = FlowGraphBuilder.Build(body);
        var condition = body.Statements.OfType<WhileStatement>().Single().Condition;
        var header = graph.Blocks.Single(block => block.Steps.Any(step => ReferenceEquals(step.Node, condition)));

        _ = Assert.Single(header.Successors);
    }

    [Fact]
    public void ALambdaBodyIsAGraphOfItsOwn()
    {
        var body = BodyIn("func<int, int> f = y => { int z = y + 1; return z; }; Console.WriteLine(f(1));");
        var graph = FlowGraphBuilder.Build(body);
        var lambda = BodyWalker.DescendantsAndSelf(body).OfType<LambdaExpression>().Single();
        var inner = BodyWalker.DescendantsAndSelf(lambda.Body!).ToHashSet(ReferenceEqualityComparer.Instance);

        Assert.DoesNotContain(graph.Blocks.SelectMany(block => block.Steps), step => inner.Contains(step.Node));
        var nested = graph.Nested[lambda];
        Assert.Contains(nested.Blocks.SelectMany(block => block.Steps), step => step.Kind == FlowStepKind.Introduce && step.Variable == lambda.Parameters[0]);
        Assert.Contains(nested.Blocks.SelectMany(block => block.Steps), step => inner.Contains(step.Node));
    }

    [Fact]
    public void APatternBindingIsBoundOnTheMatchedPathOnly()
    {
        var body = BodyIn("if(mixed is int k) { Console.WriteLine(k); } else { Console.WriteLine(42); }");
        var graph = FlowGraphBuilder.Build(body);
        var solution = FlowSolution<int>.Solve(graph, new BindCount(), 0);
        var atRead = new List<int>();
        var atElse = new List<int>();

        solution.Replay((state, step) =>
        {
            if(step.Node is LocalReferenceExpression { Local.Name: "k" })
            {
                atRead.Add(state);
            }
            else if(IsLiteral(step.Node, 42))
            {
                atElse.Add(state);
            }
        });

        Assert.Equal([1], atRead);
        Assert.Equal([0], atElse);
    }

    [Fact]
    public void AReturnEndsItsPathAndAJoinContinuesTheOthers()
    {
        var body = BodyIn("if(flag) { return; } Console.WriteLine(41); return; Console.WriteLine(42);");
        var graph = FlowGraphBuilder.Build(body);
        var solution = FlowSolution<int>.Solve(graph, new BindCount(), 0);
        var first = graph.Blocks.Single(block => block.Steps.Any(step => IsLiteral(step.Node, 41)));
        var second = graph.Blocks.Single(block => block.Steps.Any(step => IsLiteral(step.Node, 42)));

        Assert.True(solution.Reaches(first));
        Assert.False(solution.Reaches(second));
        Assert.True(solution.ReachesExit);
    }

    private static bool IsLiteral(object node, long value)
    {
        return node is LiteralExpression { Value: IConvertible constant and not string and not bool }
            && Convert.ToInt64(constant, System.Globalization.CultureInfo.InvariantCulture) == value;
    }

    private static bool IsAssignmentTarget(Block body, Expression expression)
    {
        return BodyWalker.DescendantsAndSelf(body).OfType<AssignmentExpression>().Any(assignment =>
            ReferenceEquals(assignment.Target, expression) && !assignment.IsCoalescing);
    }

    private sealed class BindCount : IFlowLattice<int>
    {
        public int Join(int left, int right)
        {
            return Math.Min(left, right);
        }

        public bool Same(int left, int right)
        {
            return left == right;
        }

        public int Transfer(int state, FlowStep step)
        {
            return step.Kind == FlowStepKind.Bind ? state + 1 : state;
        }
    }
}
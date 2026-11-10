using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class AnalysisContextGraphTests
{
    [Fact]
    public void AnalyzerContextsInDifferentScopesBuildEachRootOnce()
    {
        var built = new List<object>();
        var graphs = new FlowGraphs((root, introducer, introduced) =>
        {
            built.Add(root);
            return FlowGraphBuilder.Build(root, introducer, introduced, null);
        });
        var model = new HandBuiltModel();
        var body = HandBuiltModel.Block();
        var other = HandBuiltModel.Block();
        var first = model.Context(new DefiniteAssignmentAnalyzer(), new DiagnosticBag(), body, graphs: graphs);
        var second = model.Context(new RefinementAnalyzer(), new DiagnosticBag(), other, graphs: graphs);

        Assert.Empty(built);
        var graph = first.GraphOf(first.Bodies[0]);
        Assert.Same(graph, second.GraphOf(first.Bodies[0]));
        Assert.Same(graph, second.GraphOf(body));
        Assert.NotSame(graph, second.GraphOf(second.Bodies[0]));
        Assert.Equal([body, other], built);
    }

    [Fact]
    public void DefaultContextsShareTheirGraphsWithStandaloneConsumers()
    {
        var model = new HandBuiltModel();
        var body = HandBuiltModel.Block();
        var first = model.Context(new DefiniteAssignmentAnalyzer(), new DiagnosticBag(), body);
        var second = model.Context(new RefinementAnalyzer(), new DiagnosticBag(), body);

        Assert.Same(first.GraphOf(body), second.GraphOf(body));
        Assert.Same(first.GraphOf(body), FlowGraphs.Shared.GraphOf(body));
    }

    [Fact]
    public void NestedBodiesShareTheGraphsThatIntroduceTheirParametersEvenWhenRequestedFirst()
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var local = new LocalVariable("n", integer);
        var functionBody = HandBuiltModel.Block();
        var function = new LocalFunctionStatement(new Method("Inner", integer, [], default) { Body = functionBody }, [local]);
        var lambdaLocal = new LocalVariable("x", integer);
        var lambdaBody = HandBuiltModel.Block();
        var lambda = new LambdaExpression(HandBuiltModel.Node(), [lambdaLocal], null, lambdaBody, false, integer);
        var asyncBody = HandBuiltModel.Block();
        var asyncBlock = new AsyncBlockExpression(HandBuiltModel.Node(), asyncBody, integer, false);
        var body = HandBuiltModel.Block(function, new ExpressionStatement(lambda), new ExpressionStatement(asyncBlock));
        var graphs = new FlowGraphs();
        graphs.Register(body);

        var nested = graphs.GraphOf(functionBody);
        var graph = graphs.GraphOf(body);

        Assert.Same(nested, graph.Nested[function]);
        Assert.Same(graphs.GraphOf(lambdaBody), graph.Nested[lambda]);
        Assert.Same(graphs.GraphOf(asyncBody), graph.Nested[asyncBlock]);
        Assert.Contains(nested.Entry.Steps, step => step.Kind == FlowStepKind.Introduce && ReferenceEquals(step.Variable, local));
        Assert.Contains(graph.Nested[lambda].Entry.Steps, step => step.Kind == FlowStepKind.Introduce && ReferenceEquals(step.Variable, lambdaLocal));
    }
}
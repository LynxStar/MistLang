using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
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
    public void ConsumersHandedOnlyABodyReadTheGraphOfTheCompileTheyRunIn()
    {
        var built = new List<object>();
        var graphs = new FlowGraphs((root, introducer, introduced) =>
        {
            built.Add(root);
            return FlowGraphBuilder.Build(root, introducer, introduced, null);
        });
        var f = new LifetimeFragments();
        var source = f.Local("source", f.DisciplinedValue("class", SubstructuralDiscipline.Linear));
        var body = HandBuiltModel.Block(LifetimeFragments.Declare(source, LifetimeFragments.Fresh(source.Type)));
        var integer = f.Model.Type("Int32");
        var lifted = HandBuiltModel.Block(new ReturnStatement(HandBuiltModel.Literal(integer, 1), integer));
        var method = new Method("Lifted", integer, [], default) { Body = lifted };

        using(FlowGraphs.Enter(graphs))
        {
            Assert.Equal(LifetimeRelease.AtExit, LocalLifetimes.Plan(body).ReleaseOf(source));
            Assert.Equal([body], built);
            _ = LocalOwnership.For(body);
            _ = new LiftedReturns().Of(method);
            Assert.Equal([body, lifted], built);

            var context = f.Model.Context(new DefiniteAssignmentAnalyzer(), new DiagnosticBag(), body);
            Assert.Same(graphs.GraphOf(body), context.GraphOf(body));
            Assert.Same(graphs.GraphOf(lifted), context.GraphOf(lifted));
        }

        Assert.Equal([body, lifted], built);
        Assert.NotSame(graphs, FlowGraphs.Shared);
    }

    [Fact]
    public void AnalysisAndEmissionOfOneModelEnterTheSameGraphs()
    {
        var model = new HandBuiltModel();
        var other = new HandBuiltModel();
        var body = HandBuiltModel.Block();
        FlowGraph analysed;

        using(FlowGraphs.Enter(model.Model))
        {
            analysed = FlowGraphs.Shared.GraphOf(body);
        }

        using(FlowGraphs.Enter(model.Model))
        {
            Assert.Same(analysed, FlowGraphs.Shared.GraphOf(body));

            using(FlowGraphs.Enter(other.Model))
            {
                Assert.NotSame(analysed, FlowGraphs.Shared.GraphOf(body));
            }

            Assert.Same(analysed, FlowGraphs.Shared.GraphOf(body));
        }
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
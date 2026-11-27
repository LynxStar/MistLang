using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltRefinements;
using static Mist.Tests.ProgramAnalysis.RangeFlowBody;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.ProgramAnalysis;

public sealed class StoredRangeDecisionTests
{
    private const string Preamble = "bool flag = Branch.Choose(true); int[] numbers = [10, 20, 30, 40, 50];";

    private readonly RangeFlowBody flow = new();

    [Theory]
    [InlineData("0..3", "0..3", 2)]
    [InlineData("1..4", "1..4", 2)]
    [InlineData("0..3", "-0..3", 2)]
    [InlineData("..3", "..3", 2)]
    [InlineData("..3", "0..3", 2)]
    [InlineData("2..", "2..", 2)]
    [InlineData("2..", "2..^0", 2)]
    [InlineData("..", "..", 4)]
    [InlineData("..", "0..^0", 4)]
    [InlineData("^3..", "^3..", 2)]
    [InlineData("^4..^1", "^4..^1", 2)]
    [InlineData("1..^1", "1..^1", 2)]
    [Trait("Tier", "2")]
    public void EquivalentStoredRangesAtAJoin_ProveTheSliceLength(string before, string assigned, int last)
    {
        var body = JoinedRanges(before, assigned, last);
        Assert.Empty(Findings(body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Tier", "2")]
    public void EquivalentAssignmentsOnBothPathsOrALoop_KeepTheProof(bool loop)
    {
        Statement branch = loop
            ? new WhileStatement(flow.Flag, HandBuiltModel.Block(Set(flow.Window, Bounds("0..3")), Set(flow.Flag, flow.Values.Literal(false))))
            : new IfStatement(flow.Flag, HandBuiltModel.Block(Set(flow.Window, Bounds("0..3"))), HandBuiltModel.Block(Set(flow.Window, Bounds("0..3"))));
        var body = IndexAfter(2, Give(flow.Window, Bounds("0..3")), branch);

        Assert.Empty(Findings(body));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AConstantRangeCopy_KeepsItsValueAfterTheOriginalIsReassigned()
    {
        var original = Local(flow.Values.Range, "original");
        var body = IndexAfter(2, Give(original, Bounds("0..3")), Give(flow.Window, original),
            If(flow.Flag, Set(flow.Window, Bounds("0..3"))), Set(original, Bounds("0..1")));

        Assert.Empty(Findings(body));
    }

    [Theory]
    [InlineData("0..3", "0..3", 3, "MIST0274")]
    [InlineData("0..3", "1..4", 0, "MIST0275")]
    [InlineData("0..1", "0..3", 1, "MIST0275")]
    [InlineData("0..3", "0..^3", 0, "MIST0275")]
    [InlineData("^3..^1", "3..^1", 0, "MIST0275")]
    [Trait("Tier", "2")]
    public void AJoinKeepsNeitherDifferingBoundsNorAnOutOfBoundsProof(string before, string assigned, int last, string id)
    {
        var body = JoinedRanges(before, assigned, last);
        Assert.Equal(id, Assert.Single(Findings(body)).Number.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Tier", "2")]
    public void EquivalentSymbolBounds_ProveTheirCapturedLength(bool endFromBack)
    {
        var low = Local(flow.Values.Int, "low");
        var high = Local(flow.Values.Int, "high");
        RangeExpression range()
        {
            return flow.Values.Slice(low, endFromBack ? flow.Values.Back(high) : high);
        }

        var body = IndexAfter(0, Give(low, flow.Number(0L)), Give(high, flow.Number(3L)),
            Give(flow.Window, range()), If(flow.Flag, Set(flow.Window, range())));

        Assert.NotNull(Held(StateAtExit(body), flow.Window.Local).Slice);
        Assert.Empty(Findings(body));
    }

    [Fact]
    [Trait("Tier", "4")]
    public void IdenticallyNamedBoundsFromSeparateScopes_AreDifferentSymbols()
    {
        var model = BuildResolved("Range window = ..; if(flag) { int end = 3; window = 0..end; } else { int end = 1; window = 0..end; } var slice = numbers[window]; var last = slice[0];");
        var body = Body(model);
        var bounds = BodyWalker.DescendantsAndSelf(body).OfType<RangeExpression>()
            .Select(range => range.End).OfType<LocalReferenceExpression>().ToList();

        Assert.Equal(2, bounds.Count);
        Assert.Equal(bounds[0].Local.Name, bounds[1].Local.Name);
        Assert.NotSame(bounds[0].Local, bounds[1].Local);
        Assert.Null(Held(StateAtExit(body), BoundLocal(body, "window")).Slice);
        Assert.Equal("MIST0275", Assert.Single(Findings(model)).Number.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Tier", "2")]
    public void AWriteToASymbolicBound_KeepsTheCapturedValue(bool writeHigh)
    {
        var low = Local(flow.Values.Int, "low");
        var high = Local(flow.Values.Int, "high");
        var body = IndexAfter(0, Give(low, flow.Number(0L)), Give(high, flow.Number(3L)),
            Give(flow.Window, flow.Values.Slice(low, high)), If(flow.Flag, Set(flow.Window, flow.Values.Slice(low, high))),
            Set(writeHigh ? high : low, flow.Number(1L)));

        Assert.NotNull(Held(StateAtExit(body), flow.Window.Local).Slice);
        Assert.Empty(Findings(body));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ACopyOfASymbolicRange_KeepsTheCapturedValueWhenItsBoundIsWritten()
    {
        var end = Local(flow.Values.Int, "end");
        var original = Local(flow.Values.Range, "original");
        var body = IndexAfter(0, Give(end, flow.Number(3L)), Give(original, flow.To(end)),
            Give(flow.Window, original), If(flow.Flag, Set(flow.Window, original)), Set(end, flow.Number(1L)));
        var state = StateAtExit(body);

        Assert.NotNull(Held(state, original.Local).Slice);
        Assert.NotNull(Held(state, flow.Window.Local).Slice);
        Assert.Empty(Findings(body));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AReconstructedRangeOverDifferentCapturedValues_ProvidesNoStoredProof()
    {
        var end = Local(flow.Values.Int, "end");
        var body = IndexAfter(0, Give(end, flow.Number(3L)), Give(flow.Window, flow.To(end)),
            If(flow.Flag, Set(end, flow.Number(1L)), Set(flow.Window, flow.To(end))));

        Assert.Null(Held(StateAtExit(body), flow.Window.Local).Slice);
        Assert.Equal("MIST0275", Assert.Single(Findings(body)).Number.Value);
    }

    [Theory]
    [InlineData("foreach(var end in [3, 1]) { Range window = 0..end; var slice = numbers[window]; var last = slice[0]; }", "")]
    [Trait("Tier", "4")]
    public void AnUnknownForeachBound_ProvidesNoSliceLength(string loop, string types)
    {
        var model = BuildResolved(loop, types);
        var body = Body(model);
        var inspected = false;

        Solution(body).Replay((state, step) =>
        {
            if(step is not { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement { Local.Name: "slice" } })
            {
                return;
            }

            Assert.NotNull(Held(state, BoundLocal(body, "window")).Slice);
            inspected = true;
        });

        Assert.True(inspected);
        Assert.Equal("MIST0275", Assert.Single(Findings(model)).Number.Value);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void RepeatedUnsupportedBoundExpressions_ProvideNoStoredProof()
    {
        var end = new Method("End", flow.Values.Int, [], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = HandBuiltModel.Class("Pick"),
            Body = HandBuiltModel.Block(new ReturnStatement(flow.Number(3L), flow.Values.Int)),
        };
        RangeExpression range()
        {
            return flow.To(Call(end));
        }

        var body = IndexAfter(0, Give(flow.Window, range()), If(flow.Flag, Set(flow.Window, range())));

        Assert.Null(Held(StateAtExit(body), flow.Window.Local).Slice);
        Assert.Equal("MIST0275", Assert.Single(Findings(body)).Number.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Tier", "2")]
    public void AStoredRangeWrittenWithoutAVisibleEquivalentValue_IsUnproven(bool lambdaWrite)
    {
        var initial = Give(flow.Window, Bounds("0..3"));
        var branch = If(flow.Flag, Set(flow.Window, Bounds("0..3")));
        var replacement = new Method("Replacement", flow.Values.Range, [], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = HandBuiltModel.Class("Pick"),
            Body = HandBuiltModel.Block(new ReturnStatement(Bounds("0..1"), flow.Values.Range)),
        };
        var write = lambdaWrite
            ? flow.LambdaWrite(flow.Window, Bounds("0..1"))
            : Set(flow.Window, Call(replacement));
        var body = IndexAfter(2, initial, branch, write);

        Assert.Equal("MIST0275", Assert.Single(Findings(body)).Number.Value);
    }

    private Block JoinedRanges(string before, string assigned, int last)
    {
        return IndexAfter(last, Give(flow.Window, Bounds(before)), If(flow.Flag, Set(flow.Window, Bounds(assigned))));
    }

    private Block IndexAfter(int last, params Statement[] changes)
    {
        return flow.WithNumbers([.. changes, flow.ReadWindow(last)]);
    }

    private RangeExpression Bounds(string shape)
    {
        return shape switch
        {
            "0..1" => flow.Values.Slice(flow.Number(0), flow.Number(1)),
            "0..3" => flow.Values.Slice(flow.Number(0), flow.Number(3)),
            "-0..3" => flow.Values.Slice(new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, flow.Number(0), flow.Values.Int), flow.Number(3)),
            "1..4" => flow.Values.Slice(flow.Number(1), flow.Number(4)),
            "..3" => flow.Values.Slice(null, flow.Number(3)),
            "2.." => flow.Values.Slice(flow.Number(2), null),
            "2..^0" => flow.Values.Slice(flow.Number(2), flow.Back(0)),
            ".." => flow.Values.Slice(null, null),
            "0..^0" => flow.Values.Slice(flow.Number(0), flow.Back(0)),
            "^3.." => flow.Values.Slice(flow.Back(3), null),
            "^4..^1" => flow.Values.Slice(flow.Back(4), flow.Back(1)),
            "1..^1" => flow.Values.Slice(flow.Number(1), flow.Back(1)),
            "0..^3" => flow.Values.Slice(flow.Number(0), flow.Back(3)),
            "^3..^1" => flow.Values.Slice(flow.Back(3), flow.Back(1)),
            "3..^1" => flow.Values.Slice(flow.Number(3), flow.Back(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    private static InvocationExpression Call(Method method)
    {
        var receiver = new TypeReferenceExpression(HandBuiltModel.Node(), method.DeclaringType!);
        var member = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType);
        return new InvocationExpression(HandBuiltModel.Node(), member, [], method.ReturnType, method, []);
    }

    private IReadOnlyList<Diagnostic> Findings(Block body)
    {
        return flow.Values.Built.Analyze(new RefinementAnalyzer(), body);
    }

    private static ProgramModelClass BuildResolved(string statements, string types = "")
    {
        var model = StandardModelProbe.Build(Preamble + statements, out var diagnostics, types + " public class Branch { public static bool Choose(bool value) { return value; } }");
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var declarations = BodyWalker.DescendantsAndSelf(Body(model)).OfType<LocalDeclarationStatement>().ToList();
        var slice = Assert.IsType<IndexExpression>(declarations.Single(declaration => declaration.Local.Name == "slice").Initializer);
        var window = Assert.IsType<LocalReferenceExpression>(slice.Index);
        Assert.Equal("Standard.Range", window.Type.FullyQualifiedName);
        Assert.Null(slice.ParameterType);
        Assert.Null(slice.Indexer);

        var slicedType = Assert.IsType<ConstructedType>(slice.Type);
        Assert.Equal("Standard.Array<,>", slicedType.Definition.FullyQualifiedName);
        Assert.Equal("Standard.Int32", slicedType.TypeArguments[0].FullyQualifiedName);
        _ = Assert.IsType<UnknownValueType>(slicedType.TypeArguments[1]);

        var last = Assert.IsType<IndexExpression>(declarations.Single(declaration => declaration.Local.Name == "last").Initializer);
        Assert.Equal("Standard.Int32", last.Type.FullyQualifiedName);
        Assert.NotNull(last.ParameterType);

        return model;
    }

    private static Block Body(ProgramModelClass model)
    {
        return Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run").Body!;
    }

    private static LocalVariable BoundLocal(Block body, string name)
    {
        return BodyWalker.DescendantsAndSelf(body).OfType<LocalDeclarationStatement>().Single(declaration => declaration.Local.Name == name).Local;
    }

    private static Knowledge Held(FactState state, LocalVariable local)
    {
        Assert.True(state.TryGetValue(local, out var known));
        return known;
    }

    private static FactState StateAtExit(Block body)
    {
        var captured = FactState.Empty;
        Solution(body).Replay((state, step) =>
        {
            if(step.Kind == FlowStepKind.Leave && ReferenceEquals(step.Node, body))
            {
                captured = state;
            }
        });
        return captured;
    }

    private static FlowSolution<FactState> Solution(Block body)
    {
        var graph = FlowGraphBuilder.Build(body);

        return FlowSolution<FactState>.Solve(
            graph, new FactFlow(BodyHolders.Of(body), new LiftedReturns()), FactFlow.Nothing);
    }

    private static ImmutableArray<Diagnostic> Findings(ProgramModelClass model)
    {
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);
        return findings.ToImmutableArray();
    }
}
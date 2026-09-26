// Tier 3 (Component): stored range joins carry proofs only for equivalent supported bounds, over real
// Standard declarations so the slice and the element access are both resolved before flow is judged.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.ProgramAnalysis;

public sealed class StoredRangeDecisionTests
{
    private const string Preamble = "bool flag = true; int[] numbers = [10, 20, 30, 40, 50];";

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
    public void EquivalentStoredRangesAtAJoin_ProveTheSliceLength(string before, string assigned, int last)
    {
        var model = BuildResolved($"Range window = {before}; if(flag) {{ window = {assigned}; }} var slice = numbers[window]; var last = slice[{last}];");

        Assert.Empty(Findings(model));
    }

    [Theory]
    [InlineData("if(flag) { window = 0..3; } else { window = 0..3; }")]
    [InlineData("while(flag) { window = 0..3; flag = false; }")]
    public void EquivalentAssignmentsOnBothPathsOrALoop_KeepTheProof(string branch)
    {
        var model = BuildResolved($"Range window = 0..3; {branch} var slice = numbers[window]; var last = slice[2];");

        Assert.Empty(Findings(model));
    }

    [Fact]
    public void AConstantRangeCopy_KeepsItsValueAfterTheOriginalIsReassigned()
    {
        var model = BuildResolved("Range original = 0..3; Range window = original; if(flag) { window = 0..3; } original = 0..1; var slice = numbers[window]; var last = slice[2];");

        Assert.Empty(Findings(model));
    }

    [Theory]
    [InlineData("0..3", "0..3", 3, "MIST_PA_INDEX_OUT_OF_BOUNDS")]
    [InlineData("0..3", "1..4", 0, "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("0..1", "0..3", 1, "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("0..3", "0..^3", 0, "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("^3..^1", "3..^1", 0, "MIST_PA_INDEX_UNPROVEN")]
    public void AJoinKeepsNeitherDifferingBoundsNorAnOutOfBoundsProof(string before, string assigned, int last, string id)
    {
        var model = BuildResolved($"Range window = {before}; if(flag) {{ window = {assigned}; }} var slice = numbers[window]; var last = slice[{last}];");

        Assert.Equal(id, Assert.Single(Findings(model)).Id.Value);
    }

    [Theory]
    [InlineData("low..high")]
    [InlineData("low..^high")]
    public void EquivalentSymbolBounds_ProveTheirCapturedLength(string bounds)
    {
        var model = BuildResolved($"int low = 0; int high = 3; Range window = {bounds}; if(flag) {{ window = {bounds}; }} var slice = numbers[window]; var last = slice[0];");
        var body = Body(model);
        var state = StateAtExit(body);

        Assert.NotNull(state[Local(body, "window")].Slice);
        Assert.Empty(Findings(model));
    }

    [Fact]
    public void IdenticallyNamedBoundsFromSeparateScopes_AreDifferentSymbols()
    {
        var model = BuildResolved("Range window = ..; if(flag) { int end = 3; window = 0..end; } else { int end = 1; window = 0..end; } var slice = numbers[window]; var last = slice[0];");
        var body = Body(model);
        var bounds = BodyWalker.DescendantsAndSelf(body).OfType<RangeExpression>()
            .Select(range => range.End).OfType<LocalReferenceExpression>().ToList();

        Assert.Equal(2, bounds.Count);
        Assert.Equal(bounds[0].Local.Name, bounds[1].Local.Name);
        Assert.NotSame(bounds[0].Local, bounds[1].Local);
        Assert.Null(StateAtExit(body)[Local(body, "window")].Slice);
        Assert.Equal("MIST_PA_INDEX_UNPROVEN", Assert.Single(Findings(model)).Id.Value);
    }

    [Theory]
    [InlineData("low = 1;")]
    [InlineData("high = 1;")]
    public void AWriteToASymbolicBound_KeepsTheCapturedValue(string write)
    {
        var model = BuildResolved($"int low = 0; int high = 3; Range window = low..high; if(flag) {{ window = low..high; }} {write} var slice = numbers[window]; var last = slice[0];");
        var body = Body(model);

        Assert.NotNull(StateAtExit(body)[Local(body, "window")].Slice);
        Assert.Empty(Findings(model));
    }

    [Fact]
    public void ACopyOfASymbolicRange_KeepsTheCapturedValueWhenItsBoundIsWritten()
    {
        var model = BuildResolved("int end = 3; Range original = 0..end; Range window = original; if(flag) { window = original; } end = 1; var slice = numbers[window]; var last = slice[0];");
        var body = Body(model);
        var state = StateAtExit(body);

        Assert.NotNull(state[Local(body, "original")].Slice);
        Assert.NotNull(state[Local(body, "window")].Slice);
        Assert.Empty(Findings(model));
    }

    [Theory]
    [InlineData("int end = 3; Range window = 0..end; if(flag) { end = 1; window = 0..end; }")]
    public void AReconstructedRangeOverDifferentCapturedValues_ProvidesNoStoredProof(string ranges)
    {
        var model = BuildResolved(ranges + "var slice = numbers[window]; var last = slice[0];");
        var body = Body(model);

        Assert.Null(StateAtExit(body)[Local(body, "window")].Slice);
        Assert.Equal("MIST_PA_INDEX_UNPROVEN", Assert.Single(Findings(model)).Id.Value);
    }

    [Theory]
    [InlineData("foreach(var end in [3, 1]) { Range window = 0..end; var slice = numbers[window]; var last = slice[0]; }", "")]
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

            Assert.NotNull(state[Local(body, "window")].Slice);
            inspected = true;
        });

        Assert.True(inspected);
        Assert.Equal("MIST_PA_INDEX_UNPROVEN", Assert.Single(Findings(model)).Id.Value);
    }

    [Fact]
    public void RepeatedUnsupportedBoundExpressions_ProvideNoStoredProof()
    {
        var model = BuildResolved(
            "Range window = 0..Pick.End(); if(flag) { window = 0..Pick.End(); } var slice = numbers[window]; var last = slice[0];",
            "public class Pick { public static int End() { return 3; } }");
        var body = Body(model);

        Assert.Null(StateAtExit(body)[Local(body, "window")].Slice);
        Assert.Equal("MIST_PA_INDEX_UNPROVEN", Assert.Single(Findings(model)).Id.Value);
    }

    [Theory]
    [InlineData("window = Pick.Replacement();", "public class Pick { public static Range Replacement() { return 0..1; } }")]
    [InlineData("var change = () => { window = 0..1; }; change();", "")]
    public void AStoredRangeWrittenWithoutAVisibleEquivalentValue_IsUnproven(string write, string types)
    {
        var model = BuildResolved($"Range window = 0..3; if(flag) {{ window = 0..3; }} {write} var slice = numbers[window]; var last = slice[2];", types);

        Assert.Equal("MIST_PA_INDEX_UNPROVEN", Assert.Single(Findings(model)).Id.Value);
    }

    private static ProgramModelClass BuildResolved(string statements, string types = "")
    {
        var model = StandardModelProbe.Build(Preamble + statements, out var diagnostics, types);
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

    private static LocalVariable Local(Block body, string name)
    {
        return BodyWalker.DescendantsAndSelf(body).OfType<LocalDeclarationStatement>().Single(declaration => declaration.Local.Name == name).Local;
    }

    private static ImmutableDictionary<object, Knowledge> StateAtExit(Block body)
    {
        return Solution(body).AtExit;
    }

    private static FlowSolution<ImmutableDictionary<object, Knowledge>> Solution(Block body)
    {
        var graph = FlowGraphBuilder.Build(body);

        return FlowSolution<ImmutableDictionary<object, Knowledge>>.Solve(
            graph, new RefinementFlow(UnseenWrites.Of(body), new LiftedReturns()), RefinementFlow.Nothing);
    }

    private static ImmutableArray<Diagnostic> Findings(ProgramModelClass model)
    {
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);
        return findings.ToImmutableArray();
    }
}
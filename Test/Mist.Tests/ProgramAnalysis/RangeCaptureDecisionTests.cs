// Tier 3: real Standard slicing observes immutable captured bounds, conservative lengths, and endpoint refusals.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RangeCaptureDecisionTests
{
    private const string Preamble = "bool flag = true; int[] numbers = [10, 20, 30, 40, 50];";
    private const string Unknown = "public class Pick { public static int End(int value) { return value; } public static int[] Values() { return [10, 20, 30, 40, 50]; } public static void Change(ref int value) { value = 0; } }";
    private const string Intervals = "public class Pick { public static int Begin(bool flag) { if(flag) { return 0; } return 1; } public static int End(bool flag) { if(flag) { return 4; } return 5; } }";

    [Theory]
    [InlineData("int end = 3; end = 1; Range original = 0..end; Range window = original; if(flag) { window = original; } var slice = numbers[window]; var last = slice[0];")]
    [InlineData("int start = 0; int end = 3; Range original = start..end; Range window = original; start = 5; end = 0; original = 0..1; var slice = numbers[window]; var last = slice[2];")]
    [InlineData("int end = 3; Range window = 0..end; if(flag) { end = 9; window = 0..3; } var slice = numbers[window]; var last = slice[2];")]
    [InlineData("int end = 3; Range window = 0..end; if(flag) { end = 1; window = 0..end; } else { window = 0..1; } var slice = numbers[window]; var last = slice[0];")]
    [InlineData("while(flag) { int end = Pick.End(3); if(end >= 3 && end <= 4) { Range window = 0..end; end = 1; var slice = numbers[window]; var last = slice[2]; } flag = false; }")]
    [InlineData("while(flag) { int end = 3; Range window = 0..end; var slice = numbers[window]; var last = slice[2]; flag = false; }")]
    [InlineData("foreach(var end in [3, 4]) { if(end >= 3 && end <= 4) { Range window = 0..end; var slice = numbers[window]; var last = slice[2]; } }")]
    [InlineData("int end = Pick.End(3); if(end >= 3 && end <= 4) { var slice = numbers[0..end]; var last = slice[2]; }")]
    [InlineData("int end = 3; var last = numbers[0..end][2];")]
    [InlineData("int end = numbers.Length; Range window = 1..end; end = 0; var slice = numbers[window]; var last = slice[3];")]
    [InlineData("int end = numbers.Length; numbers = [1, 2, 3, 4, 5]; Range window = 1..end; var slice = numbers[window]; var last = slice[3];")]
    public void CapturedOffsets_KeepTheirValuesAcrossCopiesWritesJoinsAndRebinding(string statements)
    {
        Assert.Empty(Findings(Build(statements, Unknown)));
    }

    [Fact]
    public void AReceiverWrite_DoesNotReinterpretAnEarlierLengthCapture()
    {
        Assert.Equal("MIST_PA_INDEX_OUT_OF_BOUNDS", Assert.Single(Findings(Build("int end = numbers.Length; Range window = 0..end; numbers = [10]; var slice = numbers[window]; var last = slice[0];"))).Id.Value);
    }

    [Theory]
    [InlineData("start..end", 2, 3)]
    [InlineData("^end..^start", 2, 3)]
    [InlineData("start..^0", 3, 4)]
    public void CapturedIntervals_ProvideTheConservativeSliceLength(string bounds, int last, int shortest)
    {
        var model = Build($"int start = Pick.Begin(flag); int end = Pick.End(flag); Range window = {bounds}; start = 4; end = 0; var slice = numbers[window]; var last = slice[{last}];", Intervals);
        var body = Body(model);
        var graph = FlowGraphBuilder.Build(body);
        var solution = FlowSolution<ImmutableDictionary<object, Knowledge>>.Solve(graph, new RefinementFlow(UnseenWrites.Of(body), new LiftedReturns()), RefinementFlow.Nothing);
        var slice = BodyWalker.DescendantsAndSelf(body).OfType<LocalDeclarationStatement>().Single(declaration => declaration.Local.Name == "slice").Local;

        Assert.Equal(shortest, Lengths.Shortest(solution.AtExit[slice].Length));
        Assert.Empty(Findings(model));
    }

    [Theory]
    [InlineData("int end = Pick.End(3); Range window = 0..end; end = 3; var slice = numbers[window]; var last = slice[0];")]
    [InlineData("int end = 3; Range window = 0..end; var change = () => { end = 0; }; change(); var slice = numbers[window]; var last = slice[0];")]
    [InlineData("int end = Pick.End(3); Range window = 0..end; if(flag) { end = 3; window = 0..end; } var slice = numbers[window]; var last = slice[0];")]
    [InlineData("int end = 3; ref int alias = end; Range window = 0..alias; alias = 0; var slice = numbers[window]; var last = slice[0];")]
    [InlineData("Index end = ^1; Range window = 0..end; var slice = numbers[window]; var last = slice[0];")]
    [InlineData("int end = 3; Range window = 0..end; Pick.Change(ref end); var slice = numbers[window]; var last = slice[0];")]
    public void LaterFactsAndAliasedWrites_DoNotProveAnEarlierUnknownCapture(string statements)
    {
        Assert.Equal("MIST_PA_INDEX_UNPROVEN", Assert.Single(Findings(Build(statements, Unknown))).Id.Value);
    }

    [Fact]
    public void AReconstructedRange_UsesTheNewCapturedBound()
    {
        Assert.Equal("MIST_PA_INDEX_OUT_OF_BOUNDS", Assert.Single(Findings(Build("int start = 1; Range window = start..3; start = 4; window = start..; var slice = numbers[window]; var last = slice[1];"))).Id.Value);
    }

    [Fact]
    public void NonExactCapturedOffsets_NeverAssumeCorrelationFromSymbolIdentity()
    {
        Assert.Equal("MIST_PA_INDEX_UNPROVEN", Assert.Single(Findings(Build("int point = Pick.Begin(flag); Range window = point..point; var slice = numbers[window]; var last = slice[0];", Intervals))).Id.Value);
    }

    [Theory]
    [InlineData("0..0")]
    [InlineData("5..5")]
    [InlineData("^0..^0")]
    [InlineData("..")]
    [InlineData("0..5")]
    [InlineData("^5..^0")]
    public void InclusiveStartExclusiveEndAndEmptySlices_AreValidAtTheEdges(string bounds)
    {
        Assert.Empty(Findings(Build($"Range window = {bounds}; var slice = numbers[window];")));
    }

    [Theory]
    [InlineData("-1..3")]
    [InlineData("0..-1")]
    [InlineData("0..6")]
    [InlineData("6..")]
    [InlineData("3..1")]
    [InlineData("^6..^0")]
    [InlineData("^0..^1")]
    [InlineData("^(-1)..^0")]
    [InlineData("0..^(-1)")]
    [InlineData("^1..3")]
    public void DefinitelyInvalidSliceEndpoints_AreRefusedOnce(string bounds)
    {
        Assert.Equal("MIST_PA_INDEX_OUT_OF_BOUNDS", Assert.Single(Findings(Build($"Range window = {bounds}; var slice = numbers[window]; var last = slice[0];"))).Id.Value);
    }

    [Theory]
    [InlineData("0..end")]
    [InlineData("end..start")]
    public void EveryValueOfAnInvalidCapturedInterval_IsRefused(string bounds)
    {
        var model = Build($"int start = Pick.Begin(flag); int end = Pick.End(flag); Range window = {bounds}; var slice = numbers[window]; var last = slice[0];", Intervals,
            "bool flag = true; int[] numbers = [10, 20, 30];");

        Assert.Equal("MIST_PA_INDEX_OUT_OF_BOUNDS", Assert.Single(Findings(model)).Id.Value);
    }

    [Fact]
    public void DirectInvalidSlicing_IsRefusedBeforeAChainedAccess()
    {
        Assert.Equal("MIST_PA_INDEX_OUT_OF_BOUNDS", Assert.Single(Findings(Build("var last = numbers[6..7][0];"))).Id.Value);
    }

    [Theory]
    [InlineData("Range window = 0..6; var bad = numbers[window]; var first = bad; var second = first; var last = second[0];")]
    [InlineData("Range window = 0..6; var bad = numbers[window]; var other = bad; if(flag) { other = bad; } else { other = bad; } var last = other[0];")]
    public void ADefinitelyInvalidSlice_DoesNotCascadeThroughCopiesOrBothPaths(string statements)
    {
        Assert.Equal("MIST_PA_INDEX_OUT_OF_BOUNDS", Assert.Single(Findings(Build(statements))).Id.Value);
    }

    [Theory]
    [InlineData("cells = numbers[0..6]; return cells[0];", 1)]
    [InlineData("this.cells = numbers[0..6]; return this.cells[0];", 1)]
    [InlineData("cells = numbers[0..6]; var first = cells; var second = first; return second[0];", 1)]
    [InlineData("cells = numbers[0..6]; cells = numbers[0..1]; return cells[1];", 2)]
    [InlineData("cells = numbers[0..6]; if(flag) { cells = numbers[0..1]; } return cells[1];", 2)]
    [InlineData("cells = numbers[0..6]; Change(); return cells[0];", 2)]
    public void OwnedFields_TrackOnlyTheCurrentDefinitelyNonproducingSlice(string statements, int count)
    {
        var model = Build("var holder = new Holder(); var last = holder.Read(flag);",
            $"public class Holder {{ int[] cells; public void Change() {{ cells = [10]; }} public int Read(bool flag) {{ int[] numbers = [10, 20, 30, 40, 50]; {statements} }} }}");

        Assert.Equal(count, Findings(model).Length);
    }

    [Theory]
    [InlineData("var bad = numbers[0..6]; bad = numbers[0..1]; var last = bad[1];")]
    [InlineData("var bad = numbers[0..6]; if(flag) { bad = numbers[0..1]; } var last = bad[1];")]
    public void AValidReplacementOrPath_DoesNotSuppressItsOwnIndexError(string statements)
    {
        Assert.Equal(2, Findings(Build(statements)).Length);
    }

    [Theory]
    [InlineData("Range window = ^3..^1; var slice = numbers[window]; var last = slice[1];", null)]
    [InlineData("Range window = ^(-1)..^0; var slice = numbers[window];", "MIST_PA_INDEX_OUT_OF_BOUNDS")]
    [InlineData("Range window = ^0..^0; var slice = numbers[window];", null)]
    [InlineData("Range window = 1..^1; var slice = numbers[window]; var last = slice[0];", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("Range window = 0..3; var slice = numbers[window]; var last = slice[2];", null)]
    public void AnUnknownReceiver_UsesRuntimeEndpointChecksAndOnlySoundLengthInference(string statements, string? id)
    {
        var model = Build("int[] numbers = Pick.Values();" + statements, Unknown, preamble: "");
        var findings = Findings(model);

        if(id is null)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal(id, Assert.Single(findings).Id.Value);
        }
    }

    [Fact]
    public void ZeroFromEnd_IsAnEndpointButNotAnElementIndex()
    {
        Assert.Equal("MIST_PA_INDEX_OUT_OF_BOUNDS", Assert.Single(Findings(Build("var slice = numbers[^0..^0]; var last = numbers[^0];"))).Id.Value);
    }

    [Theory]
    [InlineData("\"abc\"[0..3]", false)]
    [InlineData("\"abc\"[0..4]", true)]
    [InlineData("\"abc\"[^4..]", true)]
    public void StringSlices_ValidateTheirEndpoints(string expression, bool refused)
    {
        var model = Build($"var slice = {expression};");
        var findings = Findings(model);

        Assert.Equal(refused ? 1 : 0, findings.Length);
    }

    [Fact]
    public void AUserComparison_DoesNotSupplyAnImplicitIndexConversionOffset()
    {
        var model = Build("var counter = new Counter(); if(counter == 3) { Range window = 0..counter; var slice = numbers[window]; var last = slice[2]; }",
            "public class Counter { public static bool operator ==(Counter counter, int value) { return true; } public static bool operator !=(Counter counter, int value) { return false; } public static implicit operator Index(Counter counter) { return ^3; } }");

        Assert.Equal("MIST_PA_INDEX_UNPROVEN", Assert.Single(Findings(model)).Id.Value);
    }

    private static ProgramModelClass Build(string statements, string types = "", string preamble = Preamble)
    {
        var model = StandardModelProbe.Build(preamble + statements, out var diagnostics, types);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        foreach(var index in BodyWalker.DescendantsAndSelf(Body(model)).OfType<IndexExpression>())
        {
            Assert.IsNotType<ErrorType>(index.Type);

            if(Lengths.IsRangeSlice(index))
            {
                Assert.Null(index.ParameterType);
                Assert.Null(index.Indexer);
            }
        }

        return model;
    }

    private static Block Body(ProgramModelClass model)
    {
        return Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run").Body!;
    }

    private static ImmutableArray<Diagnostic> Findings(ProgramModelClass model)
    {
        var diagnostics = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, diagnostics);
        return diagnostics.ToImmutableArray();
    }
}
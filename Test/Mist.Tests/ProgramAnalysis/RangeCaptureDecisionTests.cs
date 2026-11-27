using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using static Mist.Tests.Helpers.HandBuiltModel;
using static Mist.Tests.Helpers.HandBuiltRefinements;
using static Mist.Tests.ProgramAnalysis.RangeFlowBody;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class RangeCaptureDecisionTests
{
    [Theory]
    [InlineData("join-copy-after-write")]
    [InlineData("write-both-bounds-and-original")]
    [InlineData("join-equivalent-reconstruction")]
    [InlineData("join-equal-branch-bounds")]
    [InlineData("narrow-in-loop")]
    [InlineData("exact-in-loop")]
    [InlineData("narrow-foreach-variable")]
    [InlineData("direct-narrowed-range")]
    [InlineData("chained-access")]
    [InlineData("overwrite-captured-length")]
    [InlineData("replace-length-receiver")]
    public void CapturedOffsets_KeepTheirValuesAcrossCopiesWritesJoinsAndRebinding(string scenario)
    {
        var r = new RangeFlowBody();
        var h = r.Values;
        var start = Local(h.Int, "start");
        var end = Local(h.Int, "end");
        var original = Local(h.Range, "original");
        var window = r.Window;
        var flag = r.Flag;
        var finishLoop = Set(flag, h.Literal(false));
        var narrowed = r.InInterval(end, 3, 4);
        var capture = Give(window, r.To(end));
        Statement[] body = scenario switch
        {
            "join-copy-after-write" => [Give(end, r.Number(3)), Set(end, r.Number(1)), Give(original, r.To(end)), Give(window, original),
                If(flag, Set(window, original)), r.ReadWindow(0)],
            "write-both-bounds-and-original" => [Give(start, r.Number(0)), Give(end, r.Number(3)), Give(original, h.Slice(start, end)),
                Give(window, original), Set(start, r.Number(5)), Set(end, r.Number(0)), Set(original, r.To(r.Number(1))), r.ReadWindow(2)],
            "join-equivalent-reconstruction" => [Give(end, r.Number(3)), capture,
                If(flag, Set(end, r.Number(9)), Set(window, r.To(r.Number(3)))), r.ReadWindow(2)],
            "join-equal-branch-bounds" => [Give(end, r.Number(3)), capture,
                new IfStatement(flag, Block(Set(end, r.Number(1)), Set(window, r.To(end))), Block(Set(window, r.To(r.Number(1))))), r.ReadWindow(0)],
            "narrow-in-loop" => [new WhileStatement(flag, Block(Give(end, IdentityCall("End", r.Number(3))),
                If(narrowed, capture, Set(end, r.Number(1)), r.ReadWindow(2)), finishLoop))],
            "exact-in-loop" => [new WhileStatement(flag, Block(Give(end, r.Number(3)), capture, r.ReadWindow(2), finishLoop))],
            "narrow-foreach-variable" => [new ForEachStatement(end.Local, new ArrayLiteralExpression(Node(), h.Int, [r.Number(3), r.Number(4)], h.Sequence()),
                Block(If(narrowed, capture, r.ReadWindow(2))))],
            "direct-narrowed-range" => [Give(end, IdentityCall("End", r.Number(3))), If(narrowed, r.ReadSlice(r.To(end), 2))],
            "chained-access" => [Give(end, r.Number(3)), Evaluate(h.At(r.SliceOf(r.To(end)), r.Number(2)))],
            "overwrite-captured-length" => [Give(end, h.Length(r.Numbers)), Give(window, h.Slice(r.Number(1), end)), Set(end, r.Number(0)), r.ReadWindow(3)],
            "replace-length-receiver" => [Give(end, h.Length(r.Numbers)), Set(r.Numbers, r.ArrayLiteral(1, 2, 3, 4, 5)),
                Give(window, h.Slice(r.Number(1), end)), r.ReadWindow(3)],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        r.Check(null, body);
    }

    [Fact]
    public void AReceiverWrite_DoesNotReinterpretAnEarlierLengthCapture()
    {
        var r = new RangeFlowBody();
        var end = Local(r.Values.Int, "end");
        r.Check("MIST0274", Give(end, r.Values.Length(r.Numbers)), Give(r.Window, r.To(end)),
            Set(r.Numbers, r.ArrayLiteral(10)), r.ReadWindow(0));
    }

    [Theory]
    [InlineData("start..end", 2, 3)]
    [InlineData("^end..^start", 2, 3)]
    [InlineData("start..^0", 3, 4)]
    public void CapturedIntervals_ProvideTheConservativeSliceLength(string bounds, int last, int shortest)
    {
        var r = new RangeFlowBody();
        var h = r.Values;
        var start = Local(h.Int, "start");
        var end = Local(h.Int, "end");
        var range = bounds switch
        {
            "start..end" => h.Slice(start, end),
            "^end..^start" => h.Slice(h.Back(end), h.Back(start)),
            "start..^0" => h.Slice(start, r.Back(0)),
            _ => throw new ArgumentOutOfRangeException(nameof(bounds)),
        };
        var body = r.WithNumbers(Give(start, r.CallWithReturns("Begin", 0, 1)),
            Give(end, r.CallWithReturns("End", 4, 5)), Give(r.Window, range), Set(start, r.Number(4)), Set(end, r.Number(0)),
            Give(r.Slice, r.SliceOf(r.Window)), Evaluate(h.At(r.Slice, r.Number(last))));
        var graph = FlowGraphBuilder.Build(body);
        var solution = FlowSolution<FactState>.Solve(graph, new FactFlow(BodyHolders.Of(body), new LiftedReturns()), FactFlow.Nothing);
        var captured = FactState.Empty;
        solution.Replay((state, step) =>
        {
            if(step.Kind == FlowStepKind.Leave && ReferenceEquals(step.Node, body))
            {
                captured = state;
            }
        });
        Assert.True(captured.TryGetValue(r.Slice.Local, out var known));
        Assert.Equal(shortest, Lengths.Shortest(known.Length));
        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData("write-after-capture")]
    [InlineData("captured-write")]
    [InlineData("branch-reconstruction")]
    [InlineData("ref-local")]
    [InlineData("ref-argument")]
    public void LaterFactsAndAliasedWrites_DoNotProveAnEarlierUnknownCapture(string scenario)
    {
        var r = new RangeFlowBody();
        var end = Local(r.Values.Int, "end");
        var alias = Read(new LocalVariable("alias", r.Values.Int) { IsRef = true });
        var exact = Give(end, r.Number(3));
        var unknown = Give(end, IdentityCall("End", r.Number(3)));
        var capture = Give(r.Window, r.To(end));
        Statement[] body = scenario switch
        {
            "write-after-capture" => [unknown, capture, Set(end, r.Number(3))],
            "captured-write" => [exact, capture, r.LambdaWrite(end, r.Number(0))],
            "branch-reconstruction" => [unknown, capture, If(r.Flag, Set(end, r.Number(3)), Set(r.Window, r.To(end)))],
            "ref-local" => [exact, Give(alias, end), Give(r.Window, r.To(alias)), Set(alias, r.Number(0))],
            "ref-argument" => [exact, r.PassByRef(end, r.Number(0)), capture],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        r.Check("MIST0275", [.. body, r.ReadWindow(0)]);
    }

    [Fact]
    public void AReconstructedRange_UsesTheNewCapturedBound()
    {
        var r = new RangeFlowBody();
        var start = Local(r.Values.Int, "start");
        r.Check("MIST0274", Give(start, r.Number(1)), Give(r.Window, r.Values.Slice(start, r.Number(3))),
            Set(start, r.Number(4)), Set(r.Window, r.Values.Slice(start, null)), r.ReadWindow(1));
    }

    [Fact]
    public void NonExactCapturedOffsets_NeverAssumeCorrelationFromSymbolIdentity()
    {
        var r = new RangeFlowBody();
        var point = Local(r.Values.Int, "point");
        r.Check("MIST0275", Give(point, r.CallWithReturns("Begin", 0, 1)), Give(r.Window, r.Values.Slice(point, point)), r.ReadWindow(0));
    }

    [Theory]
    [InlineData(0, false, 0, false)]
    [InlineData(5, false, 5, false)]
    [InlineData(0, true, 0, true)]
    [InlineData(null, false, null, false)]
    [InlineData(0, false, 5, false)]
    [InlineData(5, true, 0, true)]
    public void InclusiveStartExclusiveEndAndEmptySlices_AreValidAtTheEdges(int? start, bool backStart, int? end, bool backEnd)
    {
        var r = new RangeFlowBody();
        r.Check(null, Give(r.Window, r.Bounds(start, backStart, end, backEnd)), Give(r.Slice, r.SliceOf(r.Window)));
    }

    [Theory]
    [InlineData(-1, false, 3, false)]
    [InlineData(0, false, -1, false)]
    [InlineData(0, false, 6, false)]
    [InlineData(6, false, null, false)]
    [InlineData(3, false, 1, false)]
    [InlineData(6, true, 0, true)]
    [InlineData(0, true, 1, true)]
    [InlineData(-1, true, 0, true)]
    [InlineData(0, false, -1, true)]
    [InlineData(1, true, 3, false)]
    public void DefinitelyInvalidSliceEndpoints_AreRefusedOnce(int? start, bool backStart, int? end, bool backEnd)
    {
        var r = new RangeFlowBody();
        r.Check("MIST0274", Give(r.Window, r.Bounds(start, backStart, end, backEnd)), r.ReadWindow(0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryValueOfAnInvalidCapturedInterval_IsRefused(bool reversed)
    {
        var r = new RangeFlowBody();
        var start = Local(r.Values.Int, "start");
        var end = Local(r.Values.Int, "end");
        var range = reversed ? r.Values.Slice(end, start) : r.To(end);
        r.Check("MIST0274", Set(r.Numbers, r.ArrayLiteral(10, 20, 30)), Give(start, r.CallWithReturns("Begin", 0, 1)),
            Give(end, r.CallWithReturns("End", 4, 5)), Give(r.Window, range), r.ReadWindow(0));
    }

    [Fact]
    public void DirectInvalidSlicing_IsRefusedBeforeAChainedAccess()
    {
        var r = new RangeFlowBody();
        r.Check("MIST0274", Evaluate(r.Values.At(r.SliceOf(r.Values.Slice(r.Number(6), r.Number(7))), r.Number(0))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADefinitelyInvalidSlice_DoesNotCascadeThroughCopiesOrBothPaths(bool branch)
    {
        var r = new RangeFlowBody();
        var first = Local(r.Values.Sequence(), "first");
        var second = Local(r.Values.Sequence(), "second");
        var copy = branch
            ? (Statement)new IfStatement(r.Flag, Block(Set(first, r.Slice)), Block(Set(first, r.Slice)))
            : Give(second, first);
        r.Check("MIST0274", Give(r.Window, r.To(r.Number(6))), Give(r.Slice, r.SliceOf(r.Window)),
            Give(first, r.Slice), copy, Evaluate(r.Values.At(branch ? first : second, r.Number(0))));
    }

    [Theory]
    [InlineData("field", 1)]
    [InlineData("this-field", 1)]
    [InlineData("copies", 1)]
    [InlineData("replace", 2)]
    [InlineData("branch", 2)]
    [InlineData("call", 2)]
    public void OwnedFields_TrackOnlyTheCurrentDefinitelyNonproducingSlice(string scenario, int count)
    {
        var r = new RangeFlowBody();
        var h = r.Values;
        var holder = Class("Holder");
        var cells = new Field("cells", h.Sequence(), default, new MemberModifiers()) { DeclaringType = holder, Initializer = r.ArrayLiteral(10) };
        var field = new FieldReferenceExpression(Node(), cells);
        Expression target = scenario == "this-field" ? new MemberAccessExpression(Node(), new ThisExpression(Node(), holder), cells.Name, cells.Type,
            member: new FieldResult(cells, holder, [])) : field;
        var first = Local(cells.Type, "first");
        var second = Local(cells.Type, "second");
        var replace = Set(field, r.SliceOf(r.To(r.Number(1))));
        var change = new Method("Change", h.Built.Type("Void"), [], new MemberModifiers()) { Body = Block(Set(field, r.ArrayLiteral(10))), DeclaringType = holder };
        Statement[] middle = scenario switch
        {
            "field" or "this-field" => [],
            "copies" => [Give(first, field), Give(second, first)],
            "replace" => [replace],
            "branch" => [If(r.Flag, replace)],
            "call" => [Evaluate(new InvocationExpression(Node(), new MethodReferenceExpression(Node(), change), [], change.ReturnType, change, []))],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var access = h.At(scenario == "copies" ? second : target, r.Number(scenario is "replace" or "branch" ? 1 : 0));
        var body = r.WithNumbers([Set(target, r.SliceOf(r.To(r.Number(6)))), .. middle, new ReturnStatement(access, h.Int)]);
        var findings = h.Built.Analyze(new RefinementAnalyzer(), body, owner: holder);
        Assert.Equal(count, findings.Count);
        Assert.Equal("MIST0274", findings[0].Number.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AValidReplacementOrPath_DoesNotSuppressItsOwnIndexError(bool conditional)
    {
        var r = new RangeFlowBody();
        var replace = Set(r.Slice, r.SliceOf(r.To(r.Number(1))));
        var body = r.WithNumbers(Give(r.Slice, r.SliceOf(r.To(r.Number(6)))),
            conditional ? If(r.Flag, replace) : replace, Evaluate(r.Values.At(r.Slice, r.Number(1))));
        Assert.Equal(2, r.Values.Built.Analyze(new RefinementAnalyzer(), body).Count);
    }

    [Theory]
    [InlineData(3, true, 1, true, 1, null)]
    [InlineData(-1, true, 0, true, null, "MIST0274")]
    [InlineData(0, true, 0, true, null, null)]
    [InlineData(1, false, 1, true, 0, "MIST0275")]
    [InlineData(0, false, 3, false, 2, null)]
    public void AnUnknownReceiver_UsesRuntimeEndpointChecksAndOnlySoundLengthInference(int start, bool backStart, int end, bool backEnd, int? last, string? id)
    {
        var r = new RangeFlowBody();
        var numbers = Local(r.Values.Sequence(), "unknown");
        r.Check(id, Give(numbers, CallReturning("Values", numbers.Type, r.ArrayLiteral(10, 20, 30, 40, 50))), Give(r.Window, r.Bounds(start, backStart, end, backEnd)),
            last is { } position ? r.ReadSlice(r.Window, position, numbers) : Give(r.Slice, r.SliceOf(r.Window, numbers)));
    }

    [Fact]
    public void ZeroFromEnd_IsAnEndpointButNotAnElementIndex()
    {
        var r = new RangeFlowBody();
        r.Check("MIST0274", Give(r.Slice, r.SliceOf(r.Values.Slice(r.Back(0), r.Back(0)))), Evaluate(r.Values.At(r.Numbers, r.Back(0))));
    }

    [Theory]
    [InlineData(0, false, 3, false)]
    [InlineData(0, false, 4, true)]
    [InlineData(4, true, null, true)]
    public void StringSlices_ValidateTheirEndpoints(int start, bool backStart, int? end, bool refused)
    {
        var r = new RangeFlowBody();
        r.Check(refused ? "MIST0274" : null, Evaluate(r.SliceOf(r.Bounds(start, backStart, end, false), r.Values.Literal("abc"))));
    }

    [Fact]
    public void AUserComparison_DoesNotSupplyAnImplicitIndexConversionOffset()
    {
        var r = new RangeFlowBody();
        var h = r.Values;
        var type = Class("Counter");
        var counter = Local(type, "counter");
        var equality = new BinaryExpression(Node(), counter, BinaryOperatorKind.Equal, r.Number(3), h.Bool)
        { Operator = new Operator(OperatorKind.EqualEqual, h.Bool, [new Parameter("counter", type), new Parameter("value", h.Int)], new MemberModifiers()) };
        var conversion = Conversion(type, type, h.Index, isIntrinsic: false);
        var endpoint = new ImplicitConversionExpression(Node(), counter, h.Index, [new ConversionFunction(conversion, type, h.Index)]);
        r.Check("MIST0275", If(equality, Give(r.Window, r.To(endpoint)), r.ReadWindow(2)));
    }
}
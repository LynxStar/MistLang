using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// Each requirement example of runtime-dependent-values that the flow proves, admitted where the facts
/// hold and refused where they do not, judged through the refinement analyzer over hand-built bodies.
/// </summary>
[Trait("Tier", "2")]
public sealed class SlotProofFlowTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType ring;
    private readonly TypeParameter capacity;
    private readonly LocalVariable n;
    private readonly LocalVariable m;
    private readonly LocalVariable i;

    public SlotProofFlowTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        capacity = ring.TypeParametersList[0];
        n = new LocalVariable("n", slots.ZeroOrPositive);
        m = new LocalVariable("m", slots.ZeroOrPositive);
        i = new LocalVariable("i", slots.Int);
    }

    private IReadOnlyList<Diagnostic> Analyze(params Statement[] statements)
    {
        return slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(statements));
    }

    private IReadOnlyList<Diagnostic> AnalyzeIn(Method method, MistType? owner, params Statement[] statements)
    {
        return slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(statements), owner: owner, member: method);
    }

    private static IReadOnlyList<string> Numbers(IEnumerable<Diagnostic> diagnostics)
    {
        return [.. diagnostics.Select(diagnostic => diagnostic.Number.Value)];
    }

    private (LocalVariable Local, LocalDeclarationStatement Declaration, SlotCapture Length) Created(LocalVariable length, string name = "a")
    {
        var capture = slots.Capture(length);
        var creation = slots.Create(capture);
        var local = new LocalVariable(name, creation.Type);
        return (local, new LocalDeclarationStatement(local, creation), capture);
    }

    private LocalDeclarationStatement Written(LocalVariable length, Expression source, string name = "d")
    {
        var written = slots.Capture(length);
        return HandBuiltSlots.Declare(new LocalVariable(name, slots.ArrayOf(written.Value)), source, written);
    }

    private LocalDeclarationStatement RingOf(LocalVariable value, Expression source, string name = "same")
    {
        var written = slots.Capture(value, slots.Int);
        return HandBuiltSlots.Declare(new LocalVariable(name, slots.Closed(ring, written.Value)), source, written);
    }

    private Expression Equal(Expression left, Expression right)
    {
        return slots.Refinements.Binary(left, BinaryOperatorKind.Equal, right);
    }

    private Expression Within(LocalVariable index, Expression limit)
    {
        return slots.Refinements.And(
            slots.Refinements.Binary(HandBuiltRefinements.Read(index), BinaryOperatorKind.GreaterThanOrEqual, slots.Refinements.Literal(0L)),
            slots.Refinements.Binary(HandBuiltRefinements.Read(index), BinaryOperatorKind.LessThan, limit));
    }

    private MemberAccessExpression Capacity(LocalVariable receiver)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(receiver), "Capacity", slots.Int, member: new TypeResult(capacity));
    }

    private static ExpressionStatement Write(LocalVariable target, LocalVariable value)
    {
        return new ExpressionStatement(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(target), HandBuiltRefinements.Read(value)));
    }

    private ExpressionStatement Indexed(LocalVariable receiver, LocalVariable index)
    {
        return HandBuiltRefinements.Evaluate(slots.Refinements.At(HandBuiltRefinements.Read(receiver), HandBuiltRefinements.Read(index)));
    }

    [Fact]
    public void AnArrayOfAnotherValueIsAdmittedOnlyWhereTheConditionProvesTheValuesIdentical()
    {
        var (a, declared, _) = Created(n);

        Assert.Empty(Analyze(declared, HandBuiltRefinements.If(Equal(HandBuiltRefinements.Read(m), HandBuiltRefinements.Read(n)), Written(m, HandBuiltRefinements.Read(a)))));

        var refused = Assert.Single(Analyze(declared, Written(m, HandBuiltRefinements.Read(a))));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'int[n]' is not proven to be 'int[m]': 'n' and 'm' are not proven identical (fails when n <= m - 1)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARingClosedWithUnknownIsANamedClosingOnlyWhereItsCapacityIsProvenToBeTheValue()
    {
        var some = new LocalVariable("ring", slots.Closed(ring, UnknownType.Instance));
        var condition = Equal(Capacity(some), HandBuiltRefinements.Read(n));

        Assert.Empty(Analyze(HandBuiltRefinements.If(condition, RingOf(n, HandBuiltRefinements.Read(some)))));

        var refused = Assert.Single(Analyze(RingOf(n, HandBuiltRefinements.Read(some))));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'Ring<unknown>' is not proven to be 'Ring<n>': 'ring.Capacity' and 'n' are not proven identical (fails when n <= ring.Capacity - 1)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIndexBelowTheValueTheArrayWasCreatedWithIsProvenUntilTheValueIsRewritten()
    {
        var (a, declared, _) = Created(n);
        var guarded = HandBuiltRefinements.If(Within(i, HandBuiltRefinements.Read(n)), Indexed(a, i));

        Assert.Empty(Analyze(declared, guarded));
        Assert.Equal(["MIST0275"], Numbers(Analyze(declared, Write(n, m), guarded)));
    }

    [Fact]
    public void AnIndexBelowOneArraysLengthProvesAnotherArrayCreatedWithTheSameValue()
    {
        var (a, declaredA, _) = Created(n, "a");
        var (b, declaredB, _) = Created(n, "b");
        var (c, declaredC, _) = Created(m, "c");
        var guard = Within(i, slots.Refinements.Length(HandBuiltRefinements.Read(a)));

        Assert.Empty(Analyze(declaredA, declaredB, HandBuiltRefinements.If(guard, Indexed(b, i))));
        Assert.Equal(["MIST0275"], Numbers(Analyze(declaredA, declaredC, HandBuiltRefinements.If(guard, Indexed(c, i)))));
    }

    [Fact]
    public void ACopySizedByASourcesLengthIsIndexedBelowThatLength()
    {
        var source = new LocalVariable("source", slots.ArrayOf(new UnknownValueType(slots.Int)));
        var other = new LocalVariable("other", slots.ArrayOf(new UnknownValueType(slots.Int)));
        var sized = new SlotCapture("source.Length", slots.ZeroOrPositive, slots.Int, slots.Refinements.Length(HandBuiltRefinements.Read(source)), 11);
        var creation = slots.Create(sized);
        var copy = new LocalVariable("copy", creation.Type);
        var declared = new LocalDeclarationStatement(copy, creation);

        Assert.Empty(Analyze(declared, HandBuiltRefinements.If(Within(i, slots.Refinements.Length(HandBuiltRefinements.Read(source))), Indexed(copy, i))));
        Assert.Equal(["MIST0275"], Numbers(Analyze(declared, HandBuiltRefinements.If(Within(i, slots.Refinements.Length(HandBuiltRefinements.Read(other))), Indexed(copy, i)))));
    }

    [Fact]
    public void AFieldTypedByTheEnclosingParameterIsIndexedByAValueBoundedByThatParameter()
    {
        var field = new Field("slots", slots.Closed(slots.Array, slots.Int, capacity), default, new MemberModifiers()) { DeclaringType = ring };
        var access = new MemberAccessExpression(HandBuiltModel.Node(), new ThisExpression(HandBuiltModel.Node(), ring), "slots", field.Type, member: new FieldResult(field, ring, []));
        var bounded = HandBuiltRefinements.RefinePattern(slots.Int, new ConjunctionPattern(HandBuiltModel.Node(), slots.Int,
        [
            new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.GreaterThanOrEqual, HandBuiltModel.Literal(slots.Int, 0L)),
            new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.LessThan, new TypeReferenceExpression(HandBuiltModel.Node(), capacity)),
        ]), null);

        IReadOnlyList<Diagnostic> indexedBy(IMistType type)
        {
            var parameter = new Parameter("at", type);
            var at = new LocalVariable("at", type) { Parameter = parameter };
            var get = new Method("Get", slots.Int, [parameter], new MemberModifiers()) { DeclaringType = ring };
            return AnalyzeIn(get, ring, HandBuiltRefinements.Evaluate(slots.Refinements.At(access, HandBuiltRefinements.Read(at))));
        }

        Assert.Empty(indexedBy(bounded));
        Assert.Equal(["MIST0275"], Numbers(indexedBy(slots.ZeroOrPositive)));
    }

    [Fact]
    public void TheBranchATestSelectsKnowsTheScrutineesReadToBeTheSlotsValue()
    {
        var some = new LocalVariable("ring", slots.Closed(ring, UnknownType.Instance));
        var tested = slots.Capture(n, slots.Int);
        var same = new LocalVariable("same", slots.Closed(ring, tested.Value));
        var test = new IsPatternExpression(
            HandBuiltModel.Node(), HandBuiltRefinements.Read(some), new TypeTestPattern(HandBuiltModel.Node(), same.Type, same) { Captures = [tested] }, slots.Refinements.Bool);

        Assert.Empty(Analyze(HandBuiltRefinements.If(test, RingOf(n, HandBuiltRefinements.Read(some), "x"), RingOf(n, HandBuiltRefinements.Read(same), "y"))));
        Assert.Equal(["MIST0486"], Numbers(Analyze(HandBuiltRefinements.If(test), RingOf(n, HandBuiltRefinements.Read(some), "x"))));
    }

    [Fact]
    public void ATestAgainstALiteralInsideADependentMethodGivesTheParameterThatValue()
    {
        var size = slots.Dependent("N");
        var buffer = HandBuiltSlots.Generic(HandBuiltModel.Class("Buffer<>"), "Size", slots.Int);
        var parameter = new Parameter("b", slots.Closed(buffer, size));
        var b = new LocalVariable("b", parameter.Type) { Parameter = parameter };
        var check = new Method("Check", slots.Refinements.Built.Type("Void"), [parameter], new MemberModifiers());
        var five = slots.Closed(buffer, slots.Literal(5));
        var test = new IsPatternExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(b), new TypeTestPattern(HandBuiltModel.Node(), five, null), slots.Refinements.Bool);

        Assert.Empty(AnalyzeIn(check, null, HandBuiltRefinements.If(test, new LocalDeclarationStatement(new LocalVariable("five", five), HandBuiltRefinements.Read(b)))));

        var refused = Assert.Single(AnalyzeIn(check, null, HandBuiltRefinements.If(test), new LocalDeclarationStatement(new LocalVariable("five", five), HandBuiltRefinements.Read(b))));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'Buffer<N>' is not proven to be 'Buffer<5>': 'N' and '5' are not proven identical (fails when N <= 4)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondArgumentBindingTheSameParameterIsProvenAtTheArgumentSite()
    {
        var (a, declaredA, length) = Created(n, "a");
        var (b, declaredB, _) = Created(n, "b");
        var three = new LocalVariable("c", slots.ArrayOf(slots.Literal(3)));
        var declaredC = new LocalDeclarationStatement(three, slots.Refinements.Array(3));
        var inferred = slots.ArrayOf(length.Value);
        var blit = new Method("Blit", slots.Refinements.Built.Type("Void"), [new Parameter("source", inferred), new Parameter("target", inferred)], new MemberModifiers());

        ExpressionStatement call(LocalVariable second)
        {
            return HandBuiltRefinements.Evaluate(new InvocationExpression(
                HandBuiltModel.Node(), slots.Refinements.Literal(0L), [HandBuiltRefinements.Read(a), HandBuiltRefinements.Read(second)], blit.ReturnType, blit, [inferred, inferred]));
        }

        Assert.Empty(Analyze(declaredA, declaredB, call(b)));

        var refused = Assert.Single(Analyze(declaredA, declaredC, call(three)));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'int[3]' is not proven to be 'int[n]': '3' and 'n' are not proven identical (fails when n <= 2)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReturnAgainstADependentReturnTypeIsProvenAtTheReturnSite()
    {
        var size = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = slots.Refinements.Between(slots.Int, 0L, 1000L) };
        var declared = slots.ArrayOf(size);
        var exact = new SlotCapture("N", slots.ZeroOrPositive, slots.Int, new TypeReferenceExpression(HandBuiltModel.Node(), size), 21);
        var longer = new SlotCapture(
            "N + 1", slots.ZeroOrPositive, slots.Int,
            slots.Refinements.Binary(new TypeReferenceExpression(HandBuiltModel.Node(), size), BinaryOperatorKind.Add, slots.Refinements.Literal(1L), slots.Int), 22);

        Assert.Empty(Analyze(new ReturnStatement(slots.Create(exact), declared)));

        var refused = Assert.Single(Analyze(new ReturnStatement(slots.Create(longer), declared)));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'N + 1' and 'N' are not proven identical", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AClosingWhoseValueTheFactsKnowIsSpelledWithTheLiteral()
    {
        var seven = new LocalVariable("n", slots.Int);
        var (a, declared, _) = Created(seven);

        var refused = Assert.Single(Analyze(new LocalDeclarationStatement(seven, slots.Refinements.Literal(7L)), declared, Written(m, HandBuiltRefinements.Read(a))));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'int[7]' is not proven to be 'int[m]': '7' and 'm' are not proven identical (fails when m <= 6)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AContractNamingASiblingSlotReadsTheSiblingFromTheClosingsOwnArgument()
    {
        var grid = HandBuiltModel.Class("Grid<,>");
        var rows = new TypeParameter("Rows", 0, grid.Scope, grid) { IsDependent = true, Domain = (MistType)slots.Int };
        var cols = new TypeParameter("Cols", 1, grid.Scope, grid) { IsDependent = true, Domain = (MistType)slots.Int };
        grid.TypeParametersList.AddRange([rows, cols]);
        var r = new LocalVariable("r", slots.Int);
        var c = new LocalVariable("c", slots.Int);
        var atLeastRows = HandBuiltRefinements.RefinePattern(
            slots.Int, new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.GreaterThanOrEqual, new TypeReferenceExpression(HandBuiltModel.Node(), rows)), null);

        ExpressionStatement created()
        {
            var first = slots.Capture(r, slots.Int);
            var second = new SlotCapture("c", atLeastRows, slots.Int, HandBuiltRefinements.Read(c), 31);
            return HandBuiltRefinements.Evaluate(new NewExpression(HandBuiltModel.Node(), slots.Closed(grid, first.Value, second.Value), [], null, [], null) { Captures = [first, second] });
        }

        Assert.Empty(Analyze(HandBuiltRefinements.If(slots.Refinements.Binary(HandBuiltRefinements.Read(c), BinaryOperatorKind.GreaterThanOrEqual, HandBuiltRefinements.Read(r)), created())));

        var refused = Assert.Single(Analyze(created()));

        Assert.Equal("MIST0272", refused.Number.Value);
        Assert.Contains("without proving its refinement '>= Rows' (fails when r >= c + 1)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASwitchArmAndAnArrayLiteralElementAreJudgedWhereTheFlowEvaluatesThem()
    {
        var (a, declaredA, _) = Created(n, "a");
        var (c, declaredC, _) = Created(m, "c");

        LocalDeclarationStatement picked()
        {
            var arms = new[]
            {
                new SwitchArm(HandBuiltModel.Node(), new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.Equal, slots.Refinements.Literal(1L)), null, HandBuiltRefinements.Read(a)),
                new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), slots.Int, null), null, HandBuiltRefinements.Read(c)),
            };
            return new LocalDeclarationStatement(new LocalVariable("picked", a.Type), new SwitchExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(n), arms, a.Type));
        }

        LocalDeclarationStatement listed()
        {
            var literal = new ArrayLiteralExpression(HandBuiltModel.Node(), a.Type, [HandBuiltRefinements.Read(a), HandBuiltRefinements.Read(c)], slots.Closed(slots.Array, a.Type, slots.Literal(2)));
            return new LocalDeclarationStatement(new LocalVariable("jag", literal.Type), literal);
        }

        var proven = HandBuiltRefinements.If(Equal(HandBuiltRefinements.Read(n), HandBuiltRefinements.Read(m)), picked(), listed());

        Assert.Empty(Analyze(declaredA, declaredC, proven));
        Assert.Equal(["MIST0486", "MIST0486"], Numbers(Analyze(declaredA, declaredC, picked(), listed())));
        Assert.Empty(slots.Refinements.Built.Analyze(new AssignabilityAnalyzer(), HandBuiltModel.Block(declaredA, declaredC, picked(), listed())));
    }
}
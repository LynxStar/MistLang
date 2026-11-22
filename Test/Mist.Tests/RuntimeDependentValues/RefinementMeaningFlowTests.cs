using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
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
/// The refinement meaning of runtime-dependent-values Requirements 2, 3.2, 7.6, 8.4 and 9.3, judged by the
/// refinement analyzer over hand-built bodies: a name in a slot stands for what the facts prove about its
/// value, and a fact alone relates a slot to the value.
/// </summary>
[Trait("Tier", "2")]
public sealed class RefinementMeaningFlowTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType ring;
    private readonly TypeParameter capacity;
    private readonly RefinedType range;
    private readonly LocalVariable n;
    private readonly LocalVariable m;
    private readonly LocalVariable i;

    public RefinementMeaningFlowTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        capacity = ring.TypeParametersList[0];
        range = slots.Refinements.Between(slots.Int, 11L, 29L, "Range");
        n = new LocalVariable("n", range);
        m = new LocalVariable("m", range);
        i = new LocalVariable("i", slots.Int);
    }

    private IReadOnlyList<Diagnostic> Analyze(params Statement[] statements)
    {
        return slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(statements));
    }

    private static IReadOnlyList<string> Numbers(IEnumerable<Diagnostic> diagnostics)
    {
        return [.. diagnostics.Select(diagnostic => diagnostic.Number.Value)];
    }

    private (LocalVariable Local, LocalDeclarationStatement Declaration) Created(LocalVariable length, string name)
    {
        var creation = slots.Create(slots.Capture(length));
        var local = new LocalVariable(name, creation.Type);
        return (local, new LocalDeclarationStatement(local, creation));
    }

    private NewExpression Ring(LocalVariable value)
    {
        var capture = slots.Capture(value, slots.Int);
        return new NewExpression(HandBuiltModel.Node(), slots.Closed(ring, capture.Value), [], null, [], []) { Captures = [capture] };
    }

    private LocalDeclarationStatement RingOf(LocalVariable value, Expression source, string name)
    {
        var written = slots.Capture(value, slots.Int);
        return HandBuiltSlots.Declare(new LocalVariable(name, slots.Closed(ring, written.Value)), source, written);
    }

    [Fact]
    public void AReassignmentWithinTheRefinementIsAdmittedAndEndsTheFactTheCreationRecorded()
    {
        var (a, declared) = Created(n, "a");
        var guarded = HandBuiltRefinements.If(
            slots.Refinements.And(
                slots.Refinements.Binary(HandBuiltRefinements.Read(i), BinaryOperatorKind.GreaterThanOrEqual, slots.Refinements.Literal(0L)),
                slots.Refinements.Binary(HandBuiltRefinements.Read(i), BinaryOperatorKind.LessThan, HandBuiltRefinements.Read(n))),
            HandBuiltRefinements.Evaluate(slots.Refinements.At(HandBuiltRefinements.Read(a), HandBuiltRefinements.Read(i))));
        var rewrite = HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(a), slots.Create(slots.Capture(m))));

        Assert.Empty(Analyze(declared, guarded));
        Assert.Empty(Analyze(declared, rewrite));
        Assert.Equal(["MIST0275"], Numbers(Analyze(declared, rewrite, guarded)));
    }

    [Fact]
    public void BlitRefusesTwoArraysOfOneRefinementAndProvesTwoArraysOfOneValue()
    {
        var (a, declaredA) = Created(n, "a");
        var (b, declaredB) = Created(n, "b");
        var (c, declaredC) = Created(m, "c");

        ExpressionStatement blit(LocalVariable second)
        {
            var length = new SlotCapture("a.Length", slots.ZeroOrPositive, slots.Int, slots.Refinements.Length(HandBuiltRefinements.Read(a)), 90);
            var inferred = slots.ArrayOf(length.Value);
            var method = new Method("Blit", slots.Refinements.Built.Type("Void"), [new Parameter("source", inferred), new Parameter("target", inferred)], new MemberModifiers());
            return HandBuiltRefinements.Evaluate(new InvocationExpression(
                HandBuiltModel.Node(), slots.Refinements.Literal(0L), [HandBuiltRefinements.Read(a), HandBuiltRefinements.Read(second)], method.ReturnType, method, [inferred, inferred])
            {
                Captures = [length],
            });
        }

        Assert.Empty(Analyze(declaredA, declaredB, blit(b)));

        var refused = Assert.Single(Analyze(declaredA, declaredC, blit(c)));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'m' and 'a.Length' are not proven identical", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATestAgainstALiteralGivesTheReadThatValueOnThePassingBranch()
    {
        var some = new LocalVariable("ring", slots.Closed(ring, UnknownType.Instance));
        var five = slots.Closed(ring, slots.Literal(5));
        var test = new IsPatternExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(some), new TypeTestPattern(HandBuiltModel.Node(), five, null), slots.Refinements.Bool);
        var declared = new LocalDeclarationStatement(new LocalVariable("five", five), HandBuiltRefinements.Read(some));

        Assert.Empty(Analyze(HandBuiltRefinements.If(test, declared)));
        Assert.Equal(["MIST0486"], Numbers(Analyze(HandBuiltRefinements.If(test), declared)));
    }

    [Fact]
    public void ATestAgainstACaptureGivesTheReadMembershipAndNeverEqualityWithTheName()
    {
        var some = new LocalVariable("ring", slots.Closed(ring, UnknownType.Instance));
        var tested = slots.Capture(n, slots.Int);
        var test = new IsPatternExpression(
            HandBuiltModel.Node(), HandBuiltRefinements.Read(some), new TypeTestPattern(HandBuiltModel.Node(), slots.Closed(ring, tested.Value), null) { Captures = [tested] },
            slots.Refinements.Bool);
        var fifteen = new LocalDeclarationStatement(new LocalVariable("fifteen", slots.Closed(ring, slots.Literal(15))), HandBuiltRefinements.Read(some));
        var isFifteen = slots.Refinements.Binary(HandBuiltRefinements.Read(n), BinaryOperatorKind.Equal, slots.Refinements.Literal(15L));

        Assert.Empty(Analyze(HandBuiltRefinements.If(test, RingOf(m, HandBuiltRefinements.Read(some), "other"))));
        Assert.Equal(["MIST0486"], Numbers(Analyze(HandBuiltRefinements.If(test, HandBuiltRefinements.If(isFifteen, fifteen)))));
        Assert.Empty(Analyze(HandBuiltRefinements.If(isFifteen, HandBuiltRefinements.If(test, fifteen))));
    }

    [Fact]
    public void AMemberReachedThroughAValueTakesAnArgumentEqualToThatValuesOwnSlot()
    {
        var written = slots.Capture(n, slots.Int);
        var q = new LocalVariable("q", slots.Closed(ring, written.Value));
        var declared = HandBuiltSlots.Declare(q, Ring(m), written);

        ExpressionStatement load(LocalVariable length)
        {
            var own = new SlotCapture("q.Capacity", slots.Int, slots.Int, new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(q), "Capacity", slots.Int, member: new TypeResult(capacity)), 91);
            var taken = slots.ArrayOf(own.Value);
            var method = new Method("Load", slots.Refinements.Built.Type("Void"), [new Parameter("items", taken)], new MemberModifiers()) { DeclaringType = ring };
            var callee = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(q), "Load", method.ReturnType);
            return HandBuiltRefinements.Evaluate(new InvocationExpression(
                HandBuiltModel.Node(), callee, [slots.Create(slots.Capture(length))], method.ReturnType, method, [taken])
            {
                Captures = [own],
            });
        }

        Assert.Empty(Analyze(declared, load(m)));

        var refused = Assert.Single(Analyze(declared, load(n)));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'n' and 'q.Capacity' are not proven identical", refused.Message, StringComparison.Ordinal);
    }
}
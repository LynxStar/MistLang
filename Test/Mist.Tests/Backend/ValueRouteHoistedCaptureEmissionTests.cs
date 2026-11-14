using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.ValueRouteProbes;

namespace Mist.Tests.Backend;

/// <summary>
/// A call owning a capture that the unit hoisting moves ahead of its statement evaluates the capture once, in the
/// statement it moves to, and the value left in its place evaluates nothing (runtime-dependent-values Requirement 1;
/// MIST-1369): a call yielding nothing taken as an argument, a tuple element, an assignment's value or an
/// interpolation hole, and a call stored in a temporary ahead of a later propagation.
/// </summary>
[Trait("Tier", "2")]
public sealed class ValueRouteHoistedCaptureEmissionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly TypeParameter capacity;
    private readonly ClassType counter;
    private readonly SlotCapture capture;
    private readonly ConstructedType closing;
    private readonly IMistType nothing;

    public ValueRouteHoistedCaptureEmissionTests()
    {
        var ring = Ring(slots, out capacity);
        counter = Generic("Counter<>");
        capture = Capture(slots, Count(slots));
        closing = slots.Closed(ring, capture.Value);
        nothing = slots.Refinements.Built.Type("Void");
    }

    [Fact]
    public void AUnitCallTakenAsAnArgumentEvaluatesItsCaptureOnce()
    {
        var use = Static("Use", nothing, new Parameter("v", nothing));
        using var routes = SlotRoutes.Enter([capacity]);

        AssertOnce(
            ["_ = p.Count;", "Probe.Counter<Probe.Ring>.Do();", "Use(Standard.Void.Instance);"],
            Statements(HandBuiltRefinements.Evaluate(Call(use, [], [Do()], [nothing]))));
    }

    [Fact]
    public void AUnitCallInATupleElementEvaluatesItsCaptureOnce()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var tuple = new TupleLiteralExpression(
            HandBuiltModel.Node(), [Do(), HandBuiltModel.Literal(slots.Int, 1L)], [], slots.Refinements.Built.Interner.GetOrCreateTupleType([nothing, slots.Int]));

        AssertOnce(
            ["_ = p.Count;", "Probe.Counter<Probe.Ring>.Do();", "(Void, int) t = (Standard.Void.Instance, 1);"],
            Statements(new LocalDeclarationStatement(new LocalVariable("t", tuple.Type), tuple)));
    }

    [Fact]
    public void AUnitCallAssignedEvaluatesItsCaptureOnce()
    {
        using var routes = SlotRoutes.Enter([capacity]);

        AssertOnce(
            ["_ = p.Count;", "Probe.Counter<Probe.Ring>.Do();", "v = Standard.Void.Instance;"],
            Statements(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(HandBuiltRefinements.Local(nothing, "v"), Do()))));
    }

    [Fact]
    public void AUnitCallInAnInterpolationEvaluatesItsCaptureOnce()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var text = slots.Refinements.Built.Type("String");
        var interpolated = new InterpolatedStringExpression(
            HandBuiltModel.Node(), [new InterpolatedStringHole(Do()), new InterpolatedStringText("!")], text);

        AssertOnce(
            ["_ = p.Count;", "Probe.Counter<Probe.Ring>.Do();", "string s = $\"{Standard.Void.Instance}!\";"],
            Statements(new LocalDeclarationStatement(new LocalVariable("s", text), interpolated)));
    }

    [Fact]
    public void ACallStoredAheadOfAPropagationEvaluatesItsCaptureOnce()
    {
        var shapes = new ResultModel();
        var result = shapes.Result(slots.Int, shapes.Text);
        var two = Static("Two", slots.Int, new Parameter("a", slots.Int), new Parameter("b", slots.Int));
        var ok = Static("Ok", result);
        using var routes = SlotRoutes.Enter([capacity]);
        var propagation = shapes.Propagate(Call(ok, [], [], []), result);
        var call = Call(two, [], [Counted("Count", slots.Int), propagation], [slots.Int, slots.Int]);

        var lines = Statements(new LocalDeclarationStatement(new LocalVariable("r", slots.Int), call));

        Assert.Equal("int __e1 = (p.Count switch { _ => Probe.Counter<Probe.Ring>.Count() });", lines[0]);
        Assert.StartsWith("int r = Two(__e1, ", lines[^1], StringComparison.Ordinal);
        _ = Assert.Single(lines, line => line.Contains("p.Count", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APromotedUnitCallThroughAConditionalAccessTakenAsAValueEvaluatesItsCaptureOnce(bool inTuple)
    {
        var built = slots.Refinements.Built;
        var service = HandBuiltModel.Class("Svc");
        var touch = new Method("Touch", nothing, [], new MemberModifiers { Accessibility = Accessibility.Public }) { DeclaringType = service, Scope = service.Scope };
        var receiver = HandBuiltRefinements.Local(built.Interner.GetOrCreateUnionType([service, nothing]), "o");
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Touch", nothing, [closing], isConditional: true);
        var call = new InvocationExpression(HandBuiltModel.Node(), access, [], nothing, touch, []) { Captures = [capture] };
        built.Model.TaskDrops.RecordDrop(touch);
        using var promotion = AsyncPromotion.Enter(built.Model);
        using var routes = SlotRoutes.Enter([capacity]);

        var lines = inTuple
            ? Statements(new LocalDeclarationStatement(
                new LocalVariable("t", built.Interner.GetOrCreateTupleType([nothing, slots.Int])),
                new TupleLiteralExpression(
                    HandBuiltModel.Node(), [call, HandBuiltModel.Literal(slots.Int, 1L)], [], built.Interner.GetOrCreateTupleType([nothing, slots.Int]))))
            : Statements(HandBuiltRefinements.Evaluate(Call(Static("Use", nothing, new Parameter("v", nothing)), [], [call], [nothing])));

        Assert.Equal("_ = p.Count;", lines[0]);
        Assert.Contains(lines, line => line.Contains("await __e1.Touch<Probe.Ring>();", StringComparison.Ordinal));
        Assert.Equal(inTuple ? "(Void, int) t = (Standard.Void.Instance, 1);" : "Use(Standard.Void.Instance);", lines[^1]);
        _ = Assert.Single(lines, line => line.Contains("p.Count", StringComparison.Ordinal));
    }

    private InvocationExpression Do()
    {
        return Counted("Do", nothing);
    }

    private InvocationExpression Counted(string name, IMistType returns)
    {
        var method = new Method(name, returns, [], new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public }) { DeclaringType = counter, Scope = counter.Scope };
        var through = new TypeReferenceExpression(HandBuiltModel.Node(), slots.Closed(counter, closing));
        return Through(through, method, [], [], [], captures: [capture]);
    }

    private static void AssertOnce(IReadOnlyList<string> expected, IReadOnlyList<string> lines)
    {
        Assert.Equal(expected, lines);
        _ = Assert.Single(lines, line => line.Contains("p.Count", StringComparison.Ordinal));
    }
}
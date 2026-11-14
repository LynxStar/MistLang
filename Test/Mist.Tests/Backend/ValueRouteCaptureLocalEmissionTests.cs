using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.ValueRouteProbes;

namespace Mist.Tests.Backend;

/// <summary>
/// A capture that a later node reads and that no receiver or argument carries (runtime-dependent-values design,
/// <em>Identity by value: the first delivery</em>): kept in a C# local declared before the statement that owns the
/// capture and assigned where the type is written, so the initializer runs once and in order, and a reassignment of
/// the named variable between the two does not reach the reader. Read by a call, by a second inference, by a method
/// group, inside a loop, and by the call whose own argument owns it, which binds that argument first.
/// </summary>
[Trait("Tier", "2")]
public sealed class ValueRouteCaptureLocalEmissionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly ClassType list;
    private readonly LocalVariable named;
    private readonly SlotCapture capture;
    private readonly ConstructedType rings;
    private readonly LocalVariable held;

    public ValueRouteCaptureLocalEmissionTests()
    {
        ring = Ring(slots, out capacity);
        list = Generic("List<>");
        named = new LocalVariable("n", slots.Int);
        capture = slots.Capture(named);
        rings = slots.Closed(list, slots.Closed(ring, capture.Value));
        held = new LocalVariable("list", rings);
    }

    [Fact]
    public void ACaptureALaterCallReadsIsKeptInALocalAssignedWhereTheTypeIsWritten()
    {
        var (len, n) = Len();
        using var routes = SlotRoutes.Enter([capacity, n]);

        Assert.Equal(
            [
                "int __c1;",
                "__c1 = n;",
                "Probe.List<Probe.Ring> list = new Probe.List<Probe.Ring>();",
                "n = 9;",
                "Len(__c1, list);",
            ],
            Statements(Declare(), Reassign(), HandBuiltRefinements.Evaluate(Read(len))));
    }

    [Fact]
    public void ASecondInferenceReadsTheSameLocal()
    {
        var (len, n) = Len();
        var m = slots.Dependent("M");
        var count = Inferring("Count", slots.Int, m, new Parameter("rings", slots.Closed(list, slots.Closed(ring, m))));
        using var routes = SlotRoutes.Enter([capacity, n, m]);

        Assert.Equal(
            ["int __c1;", "__c1 = n;", "Probe.List<Probe.Ring> list = new Probe.List<Probe.Ring>();", "Len(__c1, list);", "Count(__c1, list);"],
            Statements(Declare(), HandBuiltRefinements.Evaluate(Read(len)), HandBuiltRefinements.Evaluate(Read(count))));
    }

    [Fact]
    public void AMethodGroupReadsTheLocalInItsLambda()
    {
        var (len, n) = Len();
        using var routes = SlotRoutes.Enter([capacity, n]);
        var function = new FunctionType([rings], slots.Int);
        var group = new FunctionReferenceExpression(HandBuiltModel.Node(), null, len, function) { TypeArguments = [capture.Value] };

        var lines = Statements(Declare(), new LocalDeclarationStatement(new LocalVariable("f", function), group));

        Assert.Equal("int __c1;", lines[0]);
        Assert.Equal("__c1 = n;", lines[1]);
        Assert.Contains("(__arg0) => Len(__c1, __arg0)", lines[3], StringComparison.Ordinal);
    }

    [Fact]
    public void TheLocalIsDeclaredInsideTheLoopBodyThatOwnsTheCapture()
    {
        var (len, n) = Len();
        using var routes = SlotRoutes.Enter([capacity, n]);
        var loop = new WhileStatement(
            HandBuiltModel.Literal(slots.Refinements.Bool, true),
            HandBuiltModel.Block(Declare(), Reassign(), HandBuiltRefinements.Evaluate(Read(len))));

        Assert.Equal(
            [
                "while (true)",
                "{",
                "int __c1;",
                "__c1 = n;",
                "Probe.List<Probe.Ring> list = new Probe.List<Probe.Ring>();",
                "n = 9;",
                "Len(__c1, list);",
                "}",
            ],
            Statements(loop));
    }

    [Fact]
    public void ACallWhoseOwnArgumentOwnsTheCaptureBindsTheArgumentBeforeReadingTheLocal()
    {
        var (len, n) = Len();
        using var routes = SlotRoutes.Enter([capacity, n]);
        var creation = new NewExpression(HandBuiltModel.Node(), rings, [], null, [], null) { Captures = [capture] };
        var call = Call(len, [capture.Value], [creation], [rings]);

        Assert.Equal(
            ["int __c1;", "int k = (((__c1 = n) switch { _ => new Probe.List<Probe.Ring>() }) switch { var __e1 => Len(__c1, __e1) });"],
            Statements(new LocalDeclarationStatement(new LocalVariable("k", slots.Int), call)));
    }

    [Fact]
    public void ACaptureAnArgumentCarriesNeedsNoLocal()
    {
        var m = slots.Dependent("M");
        var size = Inferring("Size", slots.Int, m, new Parameter("r", slots.Closed(ring, m)));
        using var routes = SlotRoutes.Enter([capacity, m]);
        var closing = slots.Closed(ring, capture.Value);
        var r = new LocalVariable("r", closing);
        var creation = new NewExpression(HandBuiltModel.Node(), closing, [], null, [], null) { Captures = [capture] };

        Assert.Equal(
            ["Probe.Ring r = new Probe.Ring(n);", "Size(r.__Capacity, r);"],
            Statements(new LocalDeclarationStatement(r, creation), HandBuiltRefinements.Evaluate(Call(size, [capture.Value], [HandBuiltRefinements.Read(r)], [closing]))));
    }

    [Fact]
    public void ACallInferringASlotNestedUnderUnknownIsRefused()
    {
        var (len, n) = Len();
        var unknown = new UnknownValueType(slots.Int);
        var nested = slots.Closed(list, slots.Closed(ring, unknown));
        using var routes = SlotRoutes.Enter([n, capacity]);
        var diagnostics = new DiagnosticBag();
        var call = Call(len, [unknown], [HandBuiltRefinements.Local(nested, "l")], [nested]);

        BodyEmitter.EmitExpression(call, new CSharpWriter(), diagnostics);

        Assert.Equal([(len, 0)], SlotRoutes.InferredByCall(call));
        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0498", refusal.Number.Value);
        Assert.Contains("'Len' cannot infer 'N' from 'List<Ring<unknown>>'", refusal.Message, StringComparison.Ordinal);
    }

    private (Method Len, TypeParameter N) Len()
    {
        var n = slots.Dependent("N");
        return (Inferring("Len", slots.Int, n, new Parameter("rings", slots.Closed(list, slots.Closed(ring, n)))), n);
    }

    private LocalDeclarationStatement Declare()
    {
        return new LocalDeclarationStatement(held, new NewExpression(HandBuiltModel.Node(), rings, [], null, [], null) { Captures = [capture] });
    }

    private ExpressionStatement Reassign()
    {
        return HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(named), HandBuiltModel.Literal(slots.Int, 9L)));
    }

    private InvocationExpression Read(Method method)
    {
        return Call(method, [capture.Value], [HandBuiltRefinements.Read(held)], [rings]);
    }
}
using Mist.Backends.CSharpTranspiler.Lowering;
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
/// The body sites runtime-dependent-values MIST-1373 binds each evaluate a capture whose initializer runs code exactly
/// once, where the type is written: a typed foreach variable and a typed deconstruction before their operand, a with
/// binding before its value, <c>default(T)</c> and the element type of an array creation or a stack allocation as the
/// stamp's hidden value or else ahead of the node, and a method group with stated arguments ahead of the reference. A
/// later node that reads a capture its own type does not hold reads the C# local the owner assigned.
/// </summary>
[Trait("Tier", "2")]
public sealed class ValueRouteBodySiteEmissionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly StructType cell;
    private readonly TypeParameter size;
    private readonly ClassType bag;
    private readonly SlotCapture capture;
    private readonly ConstructedType closing;

    public ValueRouteBodySiteEmissionTests()
    {
        ring = Ring(slots, out capacity);
        cell = Cell(slots, out size);
        bag = Generic("Bag<>");
        capture = Capture(slots, Count(slots));
        closing = slots.Closed(ring, capture.Value);
    }

    [Fact]
    public void ATypedForeachVariableEvaluatesItsCaptureOnceBeforeTheCollection()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var loop = new ForEachStatement(new LocalVariable("r", closing), HandBuiltRefinements.Local(slots.Closed(slots.Array, closing, UnknownType.Instance), "rings"), HandBuiltModel.Block())
        {
            Captures = [capture],
        };

        Assert.Equal(["_ = p.Count;", "foreach (Probe.Ring r in rings)", "{", "}"], Statements(loop));
    }

    [Fact]
    public void ATypedDeconstructionEvaluatesItsCaptureOnceBeforeTheInitializer()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var pair = HandBuiltRefinements.Local(slots.Refinements.Built.Interner.GetOrCreateTupleType([closing, slots.Int]), "pair");
        var deconstruction = new TupleDeconstructionStatement([new LocalVariable("x", closing), new LocalVariable("y", slots.Int)], pair) { Captures = [capture] };

        Assert.Equal(["_ = p.Count;", "(Probe.Ring x, int y) = pair;"], Statements(deconstruction));
    }

    [Fact]
    public void AWithBindingEvaluatesItsCaptureOnceBeforeTheValue()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var ambient = new WithBlockStatement(HandBuiltRefinements.Local(closing, "made"), HandBuiltModel.Block(), new LocalVariable("w", closing), closing) { Captures = [capture] };

        Assert.Equal(["{", "_ = p.Count;", "var w = made;", "MistContext.Push<Probe.Ring>(w);"], Statements(ambient).Take(4));
    }

    [Fact]
    public void ADefaultOfAValueRoutedClosingConsumesItsCaptureAndOneOfAnotherTypeEvaluatesItAhead()
    {
        using var routes = SlotRoutes.Enter([capacity, size]);
        var stamped = new DefaultExpression(HandBuiltModel.Node(), slots.Closed(cell, capture.Value)) { Captures = [capture] };
        var nested = new DefaultExpression(HandBuiltModel.Node(), slots.Closed(bag, closing)) { Captures = [capture] };

        Assert.Equal("Probe.Cell.__CreateDefault(p.Count)", Emit(stamped));
        Assert.Equal("(p.Count switch { _ => Probe.Bag<Probe.Ring>.__CreateDefault() })", Emit(nested));
    }

    [Fact]
    public void AnArrayOrAStackAllocationStampsItsElementWithTheCaptureOrEvaluatesItAhead()
    {
        using var routes = SlotRoutes.Enter([capacity, size]);
        var two = slots.Refinements.Literal(2L);
        var cellOf = slots.Closed(cell, capture.Value);
        var pair = GenericStruct("Pair<>");
        var pairOf = slots.Closed(pair, closing);
        var array = new ArrayCreationExpression(HandBuiltModel.Node(), cellOf, two, slots.Closed(slots.Array, cellOf, slots.Literal(2))) { Captures = [capture] };
        var nested = new ArrayCreationExpression(HandBuiltModel.Node(), pairOf, two, slots.Closed(slots.Array, pairOf, slots.Literal(2))) { Captures = [capture] };
        var span = ((ConstructedType)slots.Refinements.Sequence("Span")).Definition;
        var stack = new StackallocExpression(HandBuiltModel.Node(), cellOf, two, slots.Closed(span, cellOf, slots.Literal(2))) { Captures = [capture] };

        Assert.Equal("MistArray.Stamp(new Probe.Cell[2], Probe.Cell.__CreateDefault(p.Count))", Emit(array));
        Assert.Equal("(p.Count switch { _ => new Probe.Pair<Probe.Ring>[2] })", Emit(nested));
        Assert.Equal("MistArray.Stamp(stackalloc Probe.Cell[2], Probe.Cell.__CreateDefault(p.Count))", Emit(stack));
    }

    [Fact]
    public void AMethodGroupWithStatedArgumentsEvaluatesItsCaptureOnceAheadOfTheReference()
    {
        var t = new TypeParameter("T", 0, new Scope(null), null);
        var first = Static("First", t, new Parameter("items", slots.Closed(bag, t)));
        first.TypeParametersList.Add(t);
        t.DeclaringMethod = first;
        using var routes = SlotRoutes.Enter([capacity]);
        var target = slots.Refinements.Built.Interner.GetOrCreateFunctionType([slots.Closed(bag, closing)], closing);
        var reference = new FunctionReferenceExpression(HandBuiltModel.Node(), null, first, target) { TypeArguments = [closing], Captures = [capture] };

        Assert.Equal("(p.Count switch { _ => ((System.Func<Probe.Bag<Probe.Ring>, Probe.Ring>)(First<Probe.Ring>)) })", Emit(reference));
    }

    [Fact]
    public void ALaterCreationReadsTheCaptureFromTheLocalItsDeclarationAssigned()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var r = new LocalVariable("r", closing);
        var declared = HandBuiltSlots.Declare(r, HandBuiltRefinements.Local(closing, "other"), capture);
        var renewed = HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(r), new NewExpression(HandBuiltModel.Node(), closing, [], null, [], null)));

        Assert.Equal(["int __c1;", "__c1 = p.Count;", "Probe.Ring r = other;", "r = new Probe.Ring(__c1);"], Statements(declared, renewed));
    }
}
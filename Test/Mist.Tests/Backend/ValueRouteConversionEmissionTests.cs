using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
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
/// A conversion into a value-routed type, and an operator none of whose operands carries the slot, over hand-built
/// declarations (runtime-dependent-values design, <em>Identity by value: conversions and nested types</em>): the static
/// method that takes the slot values as hidden leading parameters, and the call a cast, an implicit conversion and an
/// operator make with the values of the closing they convert to or found the operator through.
/// </summary>
[Trait("Tier", "2")]
public sealed class ValueRouteConversionEmissionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly CastOperator into;

    public ValueRouteConversionEmissionTests()
    {
        ring = Ring(slots, out capacity);
        into = new CastOperator(slots.Closed(ring, capacity), new Parameter("x", slots.Int), isImplicit: true, Public())
        {
            DeclaringType = ring,
            Body = HandBuiltModel.Block(new ReturnStatement(new NewExpression(HandBuiltModel.Node(), slots.Closed(ring, capacity), [], null, [], null), slots.Closed(ring, capacity))),
        };
        ring.CastOperatorsList.Add(into);
    }

    [Fact]
    public void AConversionWhoseOperandDoesNotCarryTheSlotIsAStaticMethodTakingItAndOneWhoseOperandDoesStaysAnOperator()
    {
        var outOf = new CastOperator(slots.Int, new Parameter("r", slots.Closed(ring, capacity)), isImplicit: false, Public())
        {
            DeclaringType = ring,
            Body = HandBuiltModel.Block(new ReturnStatement(new TypeReferenceExpression(HandBuiltModel.Node(), capacity), slots.Int)),
        };
        using var routes = SlotRoutes.Enter([capacity]);

        var method = Emitted(into);
        var kept = Emitted(outOf);

        Assert.Contains("public static Probe.Ring __Implicit(int __Capacity, int x)", method);
        Assert.Contains("return new Probe.Ring(__Capacity);", method);
        Assert.Contains("public static explicit operator int(Probe.Ring r)", kept);
        Assert.Contains("return r.__Capacity;", kept);
        Assert.True(OperatorMethods.Applies(into));
        Assert.False(OperatorMethods.Applies(outOf));
    }

    [Fact]
    public void ACarrierRoutedConversionIntoItsOwnTypeStaysAnOperator()
    {
        using var routes = SlotRoutes.Enter([]);

        Assert.False(OperatorMethods.Applies(into));
        Assert.Contains("public static implicit operator Probe.Ring<TCapacity>(int x)", Emitted(into));
    }

    [Fact]
    public void ACastCallsTheMethodWithItsTargetsValueForALiteralACaptureAndADependentParameter()
    {
        var k = slots.Dependent("K");
        var n = slots.Capture(new LocalVariable("n", slots.Int));
        var counted = Capture(slots, Count(slots));
        using var routes = SlotRoutes.Enter([capacity, k]);

        Assert.Equal("Probe.Ring.__Implicit(5, 4)", Emit(Cast(slots.Literal(5))));
        Assert.Equal("Probe.Ring.__Implicit(n, 4)", Emit(Cast(n.Value, n)));
        Assert.Equal("Probe.Ring.__Implicit(__K, 4)", Emit(Cast(k)));
        Assert.Equal("Probe.Ring.__Implicit(p.Count, 4)", Emit(Cast(counted.Value, counted)));
    }

    [Fact]
    public void ACastWhoseCaptureRunsCodeEvaluatesAnOperandThatRunsCodeFirst()
    {
        var counted = Capture(slots, Count(slots));
        var operand = Count(slots, "q");
        using var routes = SlotRoutes.Enter([capacity]);
        BodyEmitter.ResetTemporaries();

        var emitted = Emit(Cast(counted.Value, operand, counted));

        Assert.Matches(@"^\(q\.Count switch \{ var (__e\d+) => Probe\.Ring\.__Implicit\(p\.Count, \1\) \}\)$", emitted);
    }

    [Fact]
    public void AnImplicitConversionCallsTheMethodWithTheValuesOfTheTypeItConvertsTo()
    {
        var k = slots.Dependent("K");
        using var routes = SlotRoutes.Enter([capacity, k]);

        Assert.Equal("Probe.Ring.__Implicit(5, x)", CastLowering.Implicit(Converted(slots.Literal(5)), "x"));
        Assert.Equal("Probe.Ring.__Implicit(__K, x)", CastLowering.Implicit(Converted(k), "x"));
    }

    [Fact]
    public void AnImplicitConversionIntoAClosingWrittenWithACaptureReadsTheValueTheDeclarationKept()
    {
        var n = slots.Capture(new LocalVariable("n", slots.Int));
        var r = new LocalVariable("r", slots.Closed(ring, n.Value));
        var assigned = new AssignmentExpression(
            HandBuiltModel.Node(), new LocalReferenceExpression(HandBuiltModel.Node(), r), Converted(n.Value, HandBuiltModel.Literal(slots.Int, 2L)), r.Type);
        using var routes = SlotRoutes.Enter([capacity]);

        var lines = Statements(
            HandBuiltSlots.Declare(r, Converted(n.Value, HandBuiltModel.Literal(slots.Int, 1L)), n),
            new ExpressionStatement(assigned));

        var kept = Assert.Single(lines, line => line.StartsWith("int __c", StringComparison.Ordinal))[4..^1];
        Assert.Contains($"{kept} = n;", lines);
        Assert.Contains($"Probe.Ring r = Probe.Ring.__Implicit({kept}, 1);", lines);
        Assert.Contains($"r = Probe.Ring.__Implicit({kept}, 2);", lines);
    }

    [Fact]
    public void AnOperatorNoneOfWhoseOperandsIsTheDeclaringTypeIsAMethodOnEitherRouteAndItsUseCallsIt()
    {
        var one = slots.Closed(ring, slots.Literal(1));
        var plus = new Operator(OperatorKind.Plus, slots.Int, [new Parameter("a", one), new Parameter("b", slots.Int)], Public())
        {
            DeclaringType = ring,
            Body = HandBuiltModel.Block(new ReturnStatement(new TypeReferenceExpression(HandBuiltModel.Node(), capacity), slots.Int)),
        };
        var sum = new BinaryExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(one, "o"), BinaryOperatorKind.Add, HandBuiltModel.Literal(slots.Int, 2L), slots.Int)
        {
            Operator = plus,
            OperatorClosing = TypeSubstitution.ForReceiver(one),
            ParameterTypes = [one, slots.Int],
        };

        using(SlotRoutes.Enter([capacity]))
        {
            var routed = Emitted(plus);
            Assert.Contains("public static int __op_Addition(int __Capacity, Probe.Ring a, int b)", routed);
            Assert.Contains("return __Capacity;", routed);
            Assert.Equal("Probe.Ring.__op_Addition(1, o, 2)", Emit(sum));
        }

        using(SlotRoutes.Enter([]))
        {
            Assert.Contains("public static int __op_Addition(Probe.Ring<Const_1> a, int b)", Emitted(plus));
            Assert.Equal("Probe.Ring<Const_1>.__op_Addition(o, 2)", Emit(sum));
        }
    }

    [Fact]
    public void TwoConversionsOutOfAClosingAtAnotherValueThatDifferOnlyByTargetAreMethodsNamedApartByIt()
    {
        var one = slots.Closed(ring, slots.Literal(1));
        var toInt = OutOfOne(one, slots.Int);
        var toDouble = OutOfOne(one, slots.Refinements.Double);
        var called = new ConversionFunction(toDouble, one, slots.Refinements.Double) { Closing = TypeSubstitution.ForReceiver(one) };
        var cast = new CastExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(one, "o"), slots.Refinements.Double, [called], slots.Refinements.Double);

        using(SlotRoutes.Enter([capacity]))
        {
            var intName = OperatorMethods.Name(toInt);
            var doubleName = OperatorMethods.Name(toDouble);

            Assert.NotEqual(intName, doubleName);
            Assert.StartsWith("__Explicit_", intName, StringComparison.Ordinal);
            Assert.Contains($"public static int {intName}(int __Capacity, Probe.Ring r)", Emitted(toInt));
            Assert.Contains($"public static double {doubleName}(int __Capacity, Probe.Ring r)", Emitted(toDouble));
            Assert.Equal($"Probe.Ring.{doubleName}(1, o)", Emit(cast));
        }

        using(SlotRoutes.Enter([]))
        {
            Assert.Contains($"public static double {OperatorMethods.Name(toDouble)}(Probe.Ring<Const_1> r)", Emitted(toDouble));
            Assert.Equal($"Probe.Ring<Const_1>.{OperatorMethods.Name(toDouble)}(o)", Emit(cast));
        }

        Assert.Equal("__Implicit", OperatorMethods.Name(into));
    }

    private CastOperator OutOfOne(IMistType one, IMistType target)
    {
        var conversion = new CastOperator(target, new Parameter("r", one), isImplicit: false, Public())
        {
            DeclaringType = ring,
            DeclarationNode = HandBuiltModel.Node(),
            Body = HandBuiltModel.Block(new ReturnStatement(new TypeReferenceExpression(HandBuiltModel.Node(), capacity), target)),
        };
        ring.CastOperatorsList.Add(conversion);
        return conversion;
    }

    private CastExpression Cast(IMistType argument, params SlotCapture[] captures)
    {
        return Cast(argument, HandBuiltModel.Literal(slots.Int, 4L), captures);
    }

    private CastExpression Cast(IMistType argument, Expression operand, params SlotCapture[] captures)
    {
        var closing = slots.Closed(ring, argument);
        return new CastExpression(HandBuiltModel.Node(), operand, closing, [new ConversionFunction(into, slots.Int, closing)], closing) { Captures = captures };
    }

    private ImplicitConversionExpression Converted(IMistType argument, Expression? operand = null)
    {
        var closing = slots.Closed(ring, argument);
        return new ImplicitConversionExpression(
            HandBuiltModel.Node(), operand ?? HandBuiltRefinements.Local(slots.Int, "x"), closing, [new ConversionFunction(into, slots.Int, closing)]);
    }

    private static MemberModifiers Public()
    {
        return new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public };
    }

    private static List<string> Emitted(Method op)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        if(op is CastOperator conversion)
        {
            CastOperatorEmitter.Emit(conversion, writer, diagnostics);
        }
        else
        {
            OperatorEmitter.Emit((Operator)op, writer, diagnostics);
        }

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Message)));
        return [.. writer.ToString().Split('\n').Select(line => line.Trim())];
    }
}
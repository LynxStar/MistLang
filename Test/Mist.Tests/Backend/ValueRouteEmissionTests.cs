using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// The value route over hand-built declarations (runtime-dependent-values design, <em>The value
/// route</em>): where a slot's value lives once it leaves the C# type parameter list, how a read, a
/// test and a call reach it, and the four shapes the backend refuses rather than miscompiles.
/// </summary>
[Trait("Tier", "2")]
public sealed class ValueRouteEmissionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly ClassType box;

    public ValueRouteEmissionTests()
    {
        ring = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        ring.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        TypeParameterDomainResolver.ProjectValues(ring);
        capacity = ring.TypeParametersList[0];
        box = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Box<>"), "N", slots.Int);
        box.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        TypeParameterDomainResolver.ProjectValues(box);
    }

    [Fact]
    public void AValueRoutedClassKeepsTheValueInAFieldEveryConstructorSets()
    {
        using var routes = SlotRoutes.Enter([capacity]);

        var lines = TypeBody(ring);

        Assert.Contains("public class Ring", lines);
        Assert.Contains("public readonly int __Capacity;", lines);
        Assert.Contains("public Ring(int __Capacity)", lines);
        Assert.Contains("this.__Capacity = __Capacity;", lines);
        Assert.Contains("public static Ring __CreateDefault(int __Capacity)", lines);
        Assert.Contains("return new Ring(__Capacity);", lines);
        Assert.DoesNotContain(lines, line => line.Contains("TCapacity", StringComparison.Ordinal));
    }

    [Fact]
    public void AValueRoutedStructKeepsTheValueInAFieldItsFactoriesTake()
    {
        var span = (StructType)HandBuiltSlots.Generic(HandBuiltModel.Struct("FixedSpan<>"), "Length", slots.Int);
        span.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        using var routes = SlotRoutes.Enter([span.TypeParametersList[0]]);

        var lines = TypeBody(span);

        Assert.Contains("public struct FixedSpan", lines);
        Assert.Contains("public readonly int __Length;", lines);
        Assert.Contains("private FixedSpan(int __Length, Mist.Emission.CSharp.__StructInit marker)", lines);
        Assert.Contains("public FixedSpan(int __Length)", lines);
        Assert.Contains("this.__Length = __Length;", lines);
    }

    [Fact]
    public void AValueRoutedMethodTakesItsSlotsAsHiddenLeadingParameters()
    {
        var (len, n) = Len();
        using var routes = SlotRoutes.Enter([n]);

        Assert.Equal("int Len(int __N, int[] a)", MethodEmitter.EmitSignature(len, new DiagnosticBag()));
    }

    [Fact]
    public void AStaticMemberOfAValueRoutedTypeTakesTheTypesSlots()
    {
        var make = new Method("Make", slots.Closed(ring, capacity), [], new MemberModifiers { IsStatic = true }) { DeclaringType = ring, Scope = ring.Scope };
        using var routes = SlotRoutes.Enter([capacity]);

        Assert.Equal("Probe.Ring Make(int __Capacity)", MethodEmitter.EmitSignature(make, new DiagnosticBag()));
    }

    [Fact]
    public void AValueRoutedAbstractDeclaresAGetterEachImplementingTypeAnswers()
    {
        var (sized, n) = Sized();
        var tiny = HandBuiltModel.Class("Tiny");
        tiny.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        tiny.BaseAbstractTypes.Add(slots.Closed(sized, slots.Literal(8)));
        var flex = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Flex<>"), "M", slots.Int);
        flex.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        flex.BaseAbstractTypes.Add(slots.Closed(sized, flex.TypeParametersList[0]));
        using var routes = SlotRoutes.Enter([n, flex.TypeParametersList[0]]);

        Assert.Contains("int __N { get; }", TypeBody(sized));
        Assert.Contains("int Probe.ISized.__N => 8;", TypeBody(tiny));
        Assert.Contains("int Probe.ISized.__N => __M;", TypeBody(flex));
        Assert.Contains("public class Flex : Probe.ISized", TypeBody(flex));
    }

    [Fact]
    public void AReadThroughAValueIsTheField()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var read = new MemberAccessExpression(
            HandBuiltModel.Node(), HandBuiltRefinements.Local(slots.Closed(ring, UnknownType.Instance), "r"), "Capacity", slots.Int, member: new TypeResult(capacity));

        Assert.Equal("r.__Capacity", Emit(read));
    }

    [Fact]
    public void AReadThroughAClosingIsTheClosingsValue()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        var k = new TypeParameter("K", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var forwarded = new TypeParameter("F", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        using var routes = SlotRoutes.Enter([capacity, k]);

        Assert.Equal("4", Emit(ThroughClosing(slots.Literal(4))));
        Assert.Equal("n", Emit(ThroughClosing(capture.Value)));
        Assert.Equal("__K", Emit(ThroughClosing(k)));
        Assert.Equal("((int)TF.Value)", Emit(ThroughClosing(forwarded)));
    }

    [Fact]
    public void ATestAppendsTheComparisonAndALiteralSlotIsAMemberOfTheClause()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var value = HandBuiltRefinements.Local(UnknownType.Instance, "x");

        Assert.Equal("(x is Probe.Ring r && r.__Capacity == n)", Emit(Is(value, Tested(slots.Closed(ring, capture.Value), "r"))));
        Assert.Equal("(x is Probe.Ring __e1 && __e1.__Capacity == n)", Emit(Is(value, new TypeTestPattern(HandBuiltModel.Node(), slots.Closed(ring, capture.Value), null))));
        Assert.Equal("x is Probe.Ring { __Capacity: 4 } r", Emit(Is(value, Tested(slots.Closed(ring, slots.Literal(4)), "r"))));
    }

    [Fact]
    public void ASwitchArmTakesTheComparisonAsAGuard()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var switched = new SwitchExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(UnknownType.Instance, "x"),
        [
            new SwitchArm(HandBuiltModel.Node(), Tested(slots.Closed(ring, capture.Value), "r"), null, HandBuiltModel.Literal(slots.Int, 1L)),
            new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), UnknownType.Instance, null), null, HandBuiltModel.Literal(slots.Int, 0L)),
        ], slots.Int);

        Assert.Equal("x switch { Probe.Ring r when r.__Capacity == n => 1, var _ => 0 }", Emit(switched));
    }

    [Fact]
    public void AnAsFollowsTheTest()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var target = slots.Closed(ring, capture.Value);
        var conversion = new AsExpression(
            HandBuiltModel.Node(7), HandBuiltRefinements.Local(UnknownType.Instance, "x"), target, AsConversionKind.Narrowing, null, new UnionType([target, slots.Refinements.Built.Type("Void")]));

        Assert.Equal("(x is Probe.Ring __as7 && __as7.__Capacity == n ? __as7 : default(Probe.Ring?))", Emit(conversion));
    }

    [Fact]
    public void AClosingWithUnknownInAValueRoutedSlotIsSpelledAsTheType()
    {
        using var routes = SlotRoutes.Enter([capacity]);

        Assert.Equal("Probe.Ring", TypeNameEmitter.Emit(slots.Closed(ring, UnknownType.Instance)));
        Assert.Equal("dynamic", TypeNameEmitter.Emit(slots.Closed(box, UnknownType.Instance)));
    }

    [Fact]
    public void GenerateTakesTheLengthFromALiteralACaptureAndAnOpenParameter()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        var carried = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var routed = new TypeParameter("M", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        using var routes = SlotRoutes.Enter([routed]);

        Assert.Equal("MistArray.Generate<int>(3, fill)", Emit(Generate(slots.Literal(3))));
        Assert.Equal("MistArray.Generate<int>(n, fill)", Emit(Generate(capture.Value)));
        Assert.Equal("MistArray.Generate<int>(((int)TN.Value), fill)", Emit(Generate(carried)));
        Assert.Equal("MistArray.Generate<int>(__M, fill)", Emit(Generate(routed)));
    }

    [Fact]
    public void AConstructionPassesTheClosingsValue()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var creation = new NewExpression(HandBuiltModel.Node(), slots.Closed(ring, capture.Value), [], null, [], null) { Captures = [capture] };

        Assert.Equal("new Probe.Ring(n)", Emit(creation));
    }

    [Fact]
    public void AStatedSlotIsPassedAndAnInferredOneIsReadThroughItsArgumentBoundOnce()
    {
        var (len, n) = Len();
        var stated = slots.Capture(new LocalVariable("count", slots.Int));
        var zeroes = new Method("Zeroes", slots.ArrayOf(n), [], new MemberModifiers { IsStatic = true }) { Scope = new Scope(null) };
        zeroes.TypeParametersList.Add(n);
        using var routes = SlotRoutes.Enter([n]);

        var statedCall = Call(zeroes, [stated.Value], [], [], captures: [stated]);
        var plain = slots.Create(slots.Capture(new LocalVariable("n", slots.Int)));
        var local = HandBuiltRefinements.Local(plain.Type, "a");

        Assert.Equal("Zeroes(count)", Emit(statedCall));
        Assert.Equal("Len(a.Length, a)", Emit(Call(len, [((ConstructedType)plain.Type).TypeArguments[1]], [local], [plain.Type])));
        Assert.Equal("(new int[n] switch { var __e1 => Len(__e1.Length, __e1) })", Emit(Call(len, [((ConstructedType)plain.Type).TypeArguments[1]], [plain], [plain.Type])));
    }

    [Fact]
    public void AnInferredSlotReadThroughUnknownBindsItsArgumentOnce()
    {
        var m = new TypeParameter("M", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var cap = new Method("Cap", slots.Int, [new Parameter("b", slots.Closed(box, m))], new MemberModifiers { IsStatic = true }) { Scope = new Scope(null) };
        cap.TypeParametersList.Add(m);
        m.DeclaringMethod = cap;
        var make = new Method("Make", slots.Closed(box, UnknownType.Instance), [], new MemberModifiers { IsStatic = true }) { Scope = new Scope(null) };
        var argument = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), make), [], make.ReturnType, make, []);
        var read = new MemberAccessExpression(HandBuiltModel.Node(), argument, "N", slots.Int, member: new TypeResult(box.TypeParametersList[0]));
        var inferred = new SlotCapture("Make().N", slots.Int, slots.Int, read, 9);
        using var routes = SlotRoutes.Enter([m, box.TypeParametersList[0]]);

        var call = Call(cap, [inferred.Value], [argument], [slots.Closed(box, inferred.Value)], captures: [inferred]);

        Assert.Equal("(Make() switch { var __e1 => Cap(__e1.__N, __e1) })", Emit(call));
    }

    [Fact]
    public void ABoundArgumentRunsAfterTheReceiverAndEveryArgumentWrittenBeforeIt()
    {
        var meter = HandBuiltModel.Class("Meter");
        var n = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var len = new Method("Len", slots.Int, [new Parameter("tag", slots.Int), new Parameter("a", slots.ArrayOf(n))], new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = meter,
            Scope = meter.Scope,
        };
        len.TypeParametersList.Add(n);
        n.DeclaringMethod = len;
        var pick = new Method("Pick", meter, [], new MemberModifiers { IsStatic = true }) { Scope = new Scope(null) };
        var say = new Method("Say", slots.Int, [], new MemberModifiers { IsStatic = true }) { Scope = new Scope(null) };
        using var routes = SlotRoutes.Enter([n]);
        var plain = slots.Create(slots.Capture(new LocalVariable("n", slots.Int)));
        var length = ((ConstructedType)plain.Type).TypeArguments[1];
        var tag = HandBuiltRefinements.Local(slots.Int, "tag");

        Assert.Equal(
            "((Pick(), Say(), new int[n]) switch { (var __e1, var __e2, var __e3) => __e1.Len(__e3.Length, __e2, __e3) })",
            Emit(Through(Call(pick, [], [], []), len, [length], [Call(say, [], [], []), plain], [slots.Int, plain.Type])));
        Assert.Equal(
            "((Pick(), new int[n]) switch { (var __e1, var __e2) => __e1.Len(__e2.Length, tag, __e2) })",
            Emit(Through(Call(pick, [], [], []), len, [length], [tag, plain], [slots.Int, plain.Type])));
        Assert.Equal(
            "(new int[n] switch { var __e1 => m.Len(__e1.Length, tag, __e1) })",
            Emit(Through(HandBuiltRefinements.Local(meter, "m"), len, [length], [tag, plain], [slots.Int, plain.Type])));
    }

    [Fact]
    public void ADefaultThatConsumesTheDeclarationsCaptureLeavesNoDiscardedRead()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var count = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(UnknownType.Instance, "p"), "Count", slots.Int);
        var capture = new SlotCapture("p.Count", slots.Int, slots.Int, count, 9);
        var closing = slots.Closed(ring, capture.Value);
        var defaulted = HandBuiltSlots.Declare(new LocalVariable("r", closing), new DefaultExpression(HandBuiltModel.Node(), closing), capture);
        var copied = HandBuiltSlots.Declare(new LocalVariable("r", closing), HandBuiltRefinements.Local(closing, "other"), capture);

        Assert.Equal(["Probe.Ring r = Probe.Ring.__CreateDefault(p.Count);"], Statements(defaulted));
        Assert.Equal(["_ = p.Count;", "Probe.Ring r = other;"], Statements(copied));
    }

    [Fact]
    public void AStructOverAValueRoutedAncestorAnswersTheAncestorsSlotFromItsOwn()
    {
        var cell = (StructType)HandBuiltSlots.Generic(HandBuiltModel.Struct("Cell<>"), "N", slots.Int);
        cell.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var wide = (StructType)HandBuiltSlots.Generic(HandBuiltModel.Struct("Wide<>"), "M", slots.Int);
        wide.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        wide.BaseType = slots.Closed(cell, wide.TypeParametersList[0]);
        using var routes = SlotRoutes.Enter([cell.TypeParametersList[0], wide.TypeParametersList[0]]);

        var lines = TypeBody(wide);

        Assert.Contains("public readonly int __M;", lines);
        Assert.Contains("public int __N => __M;", lines);
        Assert.Contains("public Wide(int __M)", lines);
        Assert.Contains("return Probe.Cell.__FromParts(value.__M, default);", lines);
        Assert.DoesNotContain(lines, line => line.Contains(": base(", StringComparison.Ordinal));
    }

    [Fact]
    public void AValueRoutedClosingPassedForAConstructorConstrainedParameterIsRefused()
    {
        var t = new TypeParameter("T", 0, new Scope(null), null);
        t.ConstraintsList.Add(new ConstructorConstraint([]));
        var again = new Method("Again", t, [new Parameter("seed", t)], new MemberModifiers { IsStatic = true }) { Scope = new Scope(null) };
        again.TypeParametersList.Add(t);
        t.DeclaringMethod = again;
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        var closing = slots.Closed(ring, capture.Value);
        using var routes = SlotRoutes.Enter([capacity]);

        var refusal = Assert.Single(Refused(Call(again, [closing], [HandBuiltRefinements.Local(closing, "seed")], [closing])));
        Assert.Equal("MIST0495", refusal.Number.Value);
        Assert.Contains("'Ring<n>' cannot close the constructor constraint 'new()'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExtensionMethodTakesItsSlotAsAHiddenLeadingParameter()
    {
        var meter = HandBuiltModel.Class("Meter");
        meter.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var block = new ExtensionBlock("self", "Probe", new Scope(null), null) { CarrierName = "MeterExtensions", ResolvedReceiverType = meter };
        var n = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var len = new Method("Len", slots.Int, [new Parameter("a", slots.ArrayOf(n))], default)
        {
            DeclaringBlock = block,
            Scope = new Scope(null),
            Body = HandBuiltModel.Block(new ReturnStatement(HandBuiltModel.Literal(slots.Int, 1L), slots.Int)),
        };
        len.TypeParametersList.Add(n);
        n.DeclaringMethod = len;
        block.MethodsList.Add(len);
        using var routes = SlotRoutes.Enter([n]);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;

        ExtensionBlockEmitter.Emit(block, writer, diagnostics, ref anyEmitted);

        Assert.Contains("public static int Len(this Meter self, int __N, int[] a)", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("TN", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AValueRoutedTypeClosingAParameterizedConstructorConstraintIsRefused()
    {
        var counter = (ClassType)HandBuiltSlots.Generic(new ClassType("Counter<>", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode()), "N", slots.Int);
        using var routes = SlotRoutes.Enter([counter.TypeParametersList[0]]);
        var diagnostics = new DiagnosticBag();

        var kept = ValueRouteEmitter.ClosableConstraints(counter, [new ConstructorWitness([slots.Int], [slots.Int], [slots.Int])], diagnostics);

        Assert.Empty(kept);
        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0495", refusal.Number.Value);
        Assert.Contains("'Counter' cannot close the constructor constraint 'new(int)'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArrayOrAStackAllocationOfAValueRoutedStructIsRefused()
    {
        var cell = (StructType)HandBuiltSlots.Generic(HandBuiltModel.Struct("Cell<>"), "N", slots.Int);
        using var routes = SlotRoutes.Enter([cell.TypeParametersList[0]]);
        var element = slots.Closed(cell, slots.Literal(7));
        var size = HandBuiltModel.Literal(slots.Int, 2L);

        var array = Assert.Single(Refused(new ArrayCreationExpression(HandBuiltModel.Node(), element, size, slots.Closed(slots.Array, element, slots.Literal(2)))));
        var allocation = Assert.Single(Refused(new StackallocExpression(HandBuiltModel.Node(), element, size, slots.Closed(slots.Array, element, slots.Literal(2)))));
        Assert.Equal("MIST0496", array.Number.Value);
        Assert.Equal("MIST0496", allocation.Number.Value);
        Assert.Contains("'Cell<7>' cannot be the element of an array", array.Message, StringComparison.Ordinal);
        Assert.Contains("'Cell<7>' cannot be the element of a stack allocation", allocation.Message, StringComparison.Ordinal);
        Assert.Empty(Refused(new ArrayCreationExpression(HandBuiltModel.Node(), slots.Closed(ring, slots.Literal(7)), size, slots.Closed(slots.Array, slots.Closed(ring, slots.Literal(7)), slots.Literal(2)))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverloadsWhoseEmittedParametersCoincideAreRefusedAtTheLaterOne(bool routed)
    {
        var program = new ClassType("Program", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode());
        var first = routed ? slots.Closed(ring, slots.Literal(3)) : slots.ArrayOf(slots.Literal(3));
        var second = routed ? slots.Closed(ring, slots.Literal(4)) : slots.ArrayOf(slots.Literal(4));
        var three = new Method("Name", slots.Int, [new Parameter("r", first)], default) { DeclaringType = program, Scope = program.Scope, DeclarationNode = new BodyTypeTypeDeclarationNode() };
        var four = new Method("Name", slots.Int, [new Parameter("r", second)], default) { DeclaringType = program, Scope = program.Scope, DeclarationNode = new BodyTypeTypeDeclarationNode() };
        var other = new Method("Other", slots.Int, [new Parameter("r", second)], default) { DeclaringType = program, Scope = program.Scope, DeclarationNode = new BodyTypeTypeDeclarationNode() };
        program.MethodsList.AddRange([three, four, other]);
        using var routes = SlotRoutes.Enter(routed ? [capacity] : []);
        var diagnostics = new DiagnosticBag();

        ValueRouteEmitter.Report(program, diagnostics);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0497", refusal.Number.Value);
        Assert.Contains(routed ? "coincide with 'Name(Ring<3>)': the two differ only in 'Capacity'" : "coincide with 'Name(int[3])': the two differ only in 'Length'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticStorageInAValueRoutedTypeIsRefused()
    {
        var counter = (ClassType)HandBuiltSlots.Generic(new ClassType("Counter<>", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode()), "N", slots.Int);
        counter.FieldsList.Add(new Field("hits", slots.Int, OwnershipKind.Unrestricted, new MemberModifiers { IsStatic = true }) { DeclaringType = counter });
        using var routes = SlotRoutes.Enter([counter.TypeParametersList[0]]);
        var diagnostics = new DiagnosticBag();

        ValueRouteEmitter.Report(counter, diagnostics);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0491", refusal.Number.Value);
        Assert.StartsWith("'hits' is static storage of 'Counter'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATypeValueOfAValueRoutedClosingIsRefused()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var literal = new TypeLiteralExpression(HandBuiltModel.Node(), slots.Closed(ring, capture.Value), slots.Refinements.Built.Type("Type"));

        var refusal = Assert.Single(Refused(literal));
        Assert.Equal("MIST0492", refusal.Number.Value);
    }

    [Fact]
    public void ATypeImplementingAValueRoutedAbstractAtTwoValuesIsRefused()
    {
        var (sized, n) = Sized();
        var both = new ClassType("Both", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode());
        both.BaseAbstractTypes.Add(slots.Closed(sized, slots.Literal(1)));
        both.BaseAbstractTypes.Add(slots.Closed(sized, slots.Literal(2)));
        using var routes = SlotRoutes.Enter([n]);
        var diagnostics = new DiagnosticBag();

        ValueRouteEmitter.Report(both, diagnostics);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0493", refusal.Number.Value);
        Assert.Contains("implements 'ISized' at two values of 'N'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATestAgainstARuntimeClosingUnderNotIsRefused()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var negated = new NegatedPattern(HandBuiltModel.Node(), UnknownType.Instance, Tested(slots.Closed(ring, capture.Value), "r"));

        var refusal = Assert.Single(Refused(Is(HandBuiltRefinements.Local(UnknownType.Instance, "x"), negated)));
        Assert.Equal("MIST0494", refusal.Number.Value);
    }

    [Fact]
    public void ATestAgainstAClosingNestedInAnotherTypesArgumentIsRefused()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        var list = HandBuiltModel.Class("List<>");
        list.TypeParametersList.Add(new TypeParameter("T", 0, list.Scope, list));
        using var routes = SlotRoutes.Enter([capacity]);
        var nested = slots.Closed(list, slots.Closed(ring, capture.Value));

        var refusal = Assert.Single(Refused(Is(HandBuiltRefinements.Local(UnknownType.Instance, "x"), Tested(nested, "items"))));
        Assert.Equal("MIST0013", refusal.Number.Value);
    }

    private (Method Len, TypeParameter N) Len()
    {
        var n = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var len = new Method("Len", slots.Int, [new Parameter("a", slots.ArrayOf(n))], new MemberModifiers { IsStatic = true }) { Scope = new Scope(null) };
        len.TypeParametersList.Add(n);
        n.DeclaringMethod = len;
        return (len, n);
    }

    private (AbstractType Sized, TypeParameter N) Sized()
    {
        var sized = (AbstractType)HandBuiltSlots.Generic(new AbstractType("ISized<>", "Probe", new Scope(null), null, null), "N", slots.Int);
        sized.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        TypeParameterDomainResolver.ProjectValues(sized);
        return (sized, sized.TypeParametersList[0]);
    }

    private MemberAccessExpression ThroughClosing(IMistType argument)
    {
        return new MemberAccessExpression(
            HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), slots.Closed(ring, argument)), "Capacity", slots.Int,
            member: new PropertyResult(capacity.ValueProperty!, ring, []));
    }

    private NewExpression Generate(IMistType length)
    {
        var fill = HandBuiltRefinements.Local(UnknownType.Instance, "fill");
        return new NewExpression(HandBuiltModel.Node(), slots.ArrayOf(length), [fill], null, [], null);
    }

    private static InvocationExpression Through(
        Expression receiver, Method method, IReadOnlyList<IMistType> typeArguments, IReadOnlyList<Expression> arguments, IReadOnlyList<IMistType> parameterTypes)
    {
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType, typeArguments);
        return new InvocationExpression(HandBuiltModel.Node(), access, arguments, method.ReturnType, method, parameterTypes);
    }

    private static List<string> Statements(Statement statement)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitStatement(statement, writer, diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Message)));
        return [.. writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim())];
    }

    private static InvocationExpression Call(
        Method method, IReadOnlyList<IMistType> typeArguments, IReadOnlyList<Expression> arguments, IReadOnlyList<IMistType> parameterTypes,
        IReadOnlyList<SlotCapture>? captures = null)
    {
        return new InvocationExpression(
            HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method, typeArguments), arguments, method.ReturnType, method, parameterTypes)
        {
            Captures = captures ?? [],
        };
    }

    private IsPatternExpression Is(Expression scrutinee, Pattern pattern)
    {
        return new IsPatternExpression(HandBuiltModel.Node(), scrutinee, pattern, slots.Refinements.Bool);
    }

    private static TypeTestPattern Tested(IMistType type, string binding)
    {
        return new TypeTestPattern(HandBuiltModel.Node(), type, new LocalVariable(binding, type));
    }

    private List<string> TypeBody(MemberType type)
    {
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(slots.Refinements.Built.Model, diagnostics), slots.Refinements.Built.Model.VariadicUses, writer, diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Message)));
        return [.. writer.ToString().Split('\n').Select(line => line.Trim())];
    }

    private static string Emit(Expression expression)
    {
        return PropertyPatternEmissionTests.Emit(expression);
    }

    private static IReadOnlyList<Diagnostic> Refused(Expression expression)
    {
        var diagnostics = new DiagnosticBag();
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitExpression(expression, new CSharpWriter(), diagnostics);
        return diagnostics.ToImmutableArray();
    }
}
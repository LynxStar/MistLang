using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
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
/// test and a call reach it, the shapes the backend refuses rather than miscompiles, and the places a
/// bare read of a slot lands where no hidden member carries it: an operator, a nested type, an attribute
/// argument and a restated default, and the type positions a value-routed parameter's own name is refused in.
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
    public void AnExtensionCallBindsTheReceiverAndTheArgumentCarryingItsSlotOnce()
    {
        var (block, meter) = MeterBlock();
        var n = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var len = new Method("Len", slots.Int, [new Parameter("a", slots.ArrayOf(n))], default) { DeclaringBlock = block, Scope = new Scope(null) };
        len.TypeParametersList.Add(n);
        n.DeclaringMethod = len;
        block.MethodsList.Add(len);
        var pick = new Method("Pick", meter, [], new MemberModifiers { IsStatic = true }) { Scope = new Scope(null) };
        using var routes = SlotRoutes.Enter([n]);
        var plain = slots.Create(slots.Capture(new LocalVariable("n", slots.Int)));
        var length = ((ConstructedType)plain.Type).TypeArguments[1];

        Assert.Equal(
            "((Pick(), new int[n]) switch { (var __e1, var __e2) => Probe.MeterExtensions.Len(__e1, __e2.Length, __e2) })",
            Emit(Through(Call(pick, [], [], []), len, [length], [plain], [plain.Type])));
        Assert.Equal(
            "Probe.MeterExtensions.Len(m, a.Length, a)",
            Emit(Through(HandBuiltRefinements.Local(meter, "m"), len, [length], [HandBuiltRefinements.Local(plain.Type, "a")], [plain.Type])));
    }

    [Fact]
    public void AnExtensionCallWithNoArgumentsSeparatesTheReceiverFromItsHiddenSlot()
    {
        var (block, meter) = MeterBlock();
        var n = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var scaled = new Method("Scaled", slots.Int, [], default) { DeclaringBlock = block, Scope = new Scope(null) };
        scaled.TypeParametersList.Add(n);
        n.DeclaringMethod = scaled;
        block.MethodsList.Add(scaled);
        var stated = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([n]);
        var access = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(meter, "m"), "Scaled", slots.Int, [stated.Value]);
        var call = new InvocationExpression(HandBuiltModel.Node(), access, [], slots.Int, scaled, []) { Captures = [stated] };

        Assert.Equal("Probe.MeterExtensions.Scaled(m, n)", Emit(call));
    }

    [Fact]
    public void ABlockWhoseOwnSlotIsValueRoutedTakesItAsAHiddenLeadingParameter()
    {
        var (block, n) = ArrayBlock();
        var count = new Method("Count", slots.Int, [], default)
        {
            DeclaringBlock = block,
            Scope = new Scope(null),
            Body = HandBuiltModel.Block(new ReturnStatement(HandBuiltModel.Literal(slots.Int, 1L), slots.Int)),
        };
        block.MethodsList.Add(count);
        using var routes = SlotRoutes.Enter([n]);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;

        ExtensionBlockEmitter.Emit(block, writer, diagnostics, ref anyEmitted);

        Assert.Contains("public static int Count(this int[] self, int __N)", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("TN", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnExtensionCallReadsTheBlocksSlotThroughAnArrayReceiverBoundOnce()
    {
        var (block, n) = ArrayBlock();
        var count = new Method("Count", slots.Int, [], default) { DeclaringBlock = block, Scope = new Scope(null) };
        block.MethodsList.Add(count);
        var pick = new Method("Pick", slots.ArrayOf(UnknownType.Instance), [], new MemberModifiers { IsStatic = true }) { Scope = new Scope(null) };
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([n]);

        Assert.Equal(
            "(Pick() switch { var __e1 => Probe.ArrayExtensions.Count(__e1, __e1.Length) })",
            Emit(Through(Call(pick, [], [], []), Closed(count, block, UnknownType.Instance), [], [], [])));
        Assert.Equal(
            "Probe.ArrayExtensions.Count(a, a.Length)",
            Emit(Through(HandBuiltRefinements.Local(slots.ArrayOf(capture.Value), "a"), Closed(count, block, capture.Value), [], [], [])));
    }

    [Fact]
    public void AnExtensionCallReadsTheBlocksSlotThroughAUserTypedReceiver()
    {
        var (block, n) = BoxBlock(slots.Closed(box, slots.Dependent("N")));
        var size = new Method("Size", slots.Int, [], default) { DeclaringBlock = block, Scope = new Scope(null) };
        block.MethodsList.Add(size);
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([n, box.TypeParametersList[0]]);

        Assert.Equal(
            "Probe.BoxExtensions.Size(b, b.__N)",
            Emit(Through(HandBuiltRefinements.Local(slots.Closed(box, capture.Value), "b"), Closed(size, block, capture.Value), [], [], [])));
        Assert.Equal(
            "Probe.BoxExtensions.Size(u, u.__N)",
            Emit(Through(HandBuiltRefinements.Local(slots.Closed(box, UnknownType.Instance), "u"), Closed(size, block, UnknownType.Instance), [], [], [])));
        Assert.Equal(
            "Probe.BoxExtensions.Size(c, 5)",
            Emit(Through(HandBuiltRefinements.Local(slots.Closed(box, slots.Literal(5)), "c"), Closed(size, block, slots.Literal(5)), [], [], [])));
    }

    [Fact]
    public void AMethodGroupOfAValueRoutedMethodReadsItsSlotFromTheDelegatesArgument()
    {
        var (len, n) = Len();
        var m = slots.Dependent("M");
        var cap = new Method("Cap", slots.Int, [new Parameter("b", slots.Closed(box, m))], new MemberModifiers { IsStatic = true }) { Scope = new Scope(null) };
        cap.TypeParametersList.Add(m);
        m.DeclaringMethod = cap;
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([n, m, box.TypeParametersList[0]]);

        Assert.Equal("((System.Func<int[], int>)((__arg0) => Len(__arg0.Length, __arg0)))", Emit(Group(len, UnknownType.Instance, slots.ArrayOf(UnknownType.Instance))));
        Assert.Equal("((System.Func<int[], int>)((__arg0) => Len(__arg0.Length, __arg0)))", Emit(Group(len, capture.Value, slots.ArrayOf(capture.Value))));
        Assert.Equal("((System.Func<Probe.Box, int>)((__arg0) => Cap(__arg0.__N, __arg0)))", Emit(Group(cap, UnknownType.Instance, slots.Closed(box, UnknownType.Instance))));
    }

    [Fact]
    public void AStaticMemberGroupOfAValueRoutedTypePassesTheClosingsValue()
    {
        var twice = new Method("Twice", slots.Int, [], new MemberModifiers { IsStatic = true }) { DeclaringType = ring, Scope = ring.Scope };
        using var routes = SlotRoutes.Enter([capacity]);
        var reference = new FunctionReferenceExpression(
            HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), slots.Closed(ring, slots.Literal(3))), twice, new FunctionType([], slots.Int));

        Assert.Equal("((System.Func<int>)(() => Probe.Ring.Twice(3)))", Emit(reference));
    }

    [Fact]
    public void ABoundGroupOfABlockMethodReadsTheBlocksSlotThroughTheBoundReceiver()
    {
        var (block, n) = BoxBlock(slots.Closed(box, slots.Dependent("N")));
        var size = new Method("Size", slots.Int, [], default) { DeclaringBlock = block, Scope = new Scope(null) };
        block.MethodsList.Add(size);
        var (arrayBlock, k) = ArrayBlock();
        var count = new Method("Count", slots.Int, [], default) { DeclaringBlock = arrayBlock, Scope = new Scope(null) };
        arrayBlock.MethodsList.Add(count);
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([n, k, box.TypeParametersList[0]]);

        Assert.Equal(
            "((System.Func<Probe.Box, System.Func<int>>)(__receiver => () => Probe.BoxExtensions.Size(__receiver, __receiver.__N)))(b)",
            Emit(Bound(HandBuiltRefinements.Local(slots.Closed(box, capture.Value), "b"), Closed(size, block, capture.Value))));
        Assert.Equal(
            "((System.Func<Probe.Box, System.Func<int>>)(__receiver => () => Probe.BoxExtensions.Size(__receiver, __receiver.__N)))(u)",
            Emit(Bound(HandBuiltRefinements.Local(slots.Closed(box, UnknownType.Instance), "u"), Closed(size, block, UnknownType.Instance))));
        Assert.Equal(
            "((System.Func<Probe.Box, System.Func<int>>)(__receiver => () => Probe.BoxExtensions.Size(__receiver, 5)))(c)",
            Emit(Bound(HandBuiltRefinements.Local(slots.Closed(box, slots.Literal(5)), "c"), Closed(size, block, slots.Literal(5)))));
        Assert.Equal(
            "((System.Func<int[], System.Func<int>>)(__receiver => () => Probe.ArrayExtensions.Count(__receiver, __receiver.Length)))(a)",
            Emit(Bound(HandBuiltRefinements.Local(slots.ArrayOf(capture.Value), "a"), Closed(count, arrayBlock, capture.Value))));
    }

    [Fact]
    public void AMethodGroupWhoseSlotIsNestedInTheDelegatesParameterIsRefused()
    {
        var list = ListType();
        var m = slots.Dependent("M");
        var cap = new Method("Cap", slots.Int, [new Parameter("boxes", slots.Closed(list, slots.Closed(box, m)))], new MemberModifiers { IsStatic = true }) { Scope = new Scope(null) };
        cap.TypeParametersList.Add(m);
        m.DeclaringMethod = cap;
        using var routes = SlotRoutes.Enter([m, box.TypeParametersList[0]]);

        var refusal = Assert.Single(Refused(Group(cap, UnknownType.Instance, slots.Closed(list, slots.Closed(box, UnknownType.Instance)))));

        Assert.Equal("MIST0498", refusal.Number.Value);
        Assert.Contains("'Cap' cannot infer 'M' from 'List<Box<int>>'", refusal.Message, StringComparison.Ordinal);
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
    public void AnOverloadCoincidingThroughAHiddenSlotParameterIsRefused()
    {
        var program = new ClassType("Program", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode());
        var n = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var len = new Method("Len", slots.Int, [new Parameter("a", slots.ArrayOf(n))], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = program,
            Scope = program.Scope,
            DeclarationNode = new BodyTypeTypeDeclarationNode(),
        };
        len.TypeParametersList.Add(n);
        n.DeclaringMethod = len;
        var plain = new Method("Len", slots.Int, [new Parameter("k", slots.Int), new Parameter("a", slots.ArrayOf(UnknownType.Instance))], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = program,
            Scope = program.Scope,
            DeclarationNode = new BodyTypeTypeDeclarationNode(),
        };
        program.MethodsList.AddRange([len, plain]);
        using var routes = SlotRoutes.Enter([n]);
        var diagnostics = new DiagnosticBag();

        ValueRouteEmitter.Report(program, diagnostics);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0497", refusal.Number.Value);
        Assert.Contains("coincide with 'Len<N>(int[N])': the two differ only in 'N' of 'int[N]'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverloadCoincidingThroughASlotErasedInsideAnotherTypesArgumentIsRefused()
    {
        var program = new ClassType("Program", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode());
        var holder = HandBuiltModel.Class("Holder<>");
        holder.TypeParametersList.Add(new TypeParameter("T", 0, holder.Scope, holder));
        var three = new Method("Name", slots.Int, [new Parameter("r", slots.Closed(holder, slots.Closed(ring, slots.Literal(3))))], default) { DeclaringType = program, Scope = program.Scope, DeclarationNode = new BodyTypeTypeDeclarationNode() };
        var four = new Method("Name", slots.Int, [new Parameter("r", slots.Closed(holder, slots.Closed(ring, slots.Literal(4))))], default) { DeclaringType = program, Scope = program.Scope, DeclarationNode = new BodyTypeTypeDeclarationNode() };
        program.MethodsList.AddRange([three, four]);
        using var routes = SlotRoutes.Enter([capacity]);
        var diagnostics = new DiagnosticBag();

        ValueRouteEmitter.Report(program, diagnostics);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0497", refusal.Number.Value);
        Assert.Contains("coincide with 'Name(Holder<Ring<3>>)': the two differ only in 'Capacity' of 'Ring<3>'", refusal.Message, StringComparison.Ordinal);
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
    public void ATypeValueOfAValueRoutedParameterIsRefusedWhereTheParameterIsSpelled()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var literal = new TypeLiteralExpression(HandBuiltModel.Node(), capacity, slots.Refinements.Built.Type("Type"));

        var refusal = Assert.Single(Refused(literal));
        Assert.Equal("MIST0500", refusal.Number.Value);
        Assert.StartsWith("'Capacity' of 'Ring' cannot stand where a C# type is needed", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclaredTypeNamingAValueRoutedParameterIsRefused()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        ring.FieldsList.Add(new Field("Items", slots.Closed(ListType(), capacity), OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public }) { DeclaringType = ring });
        var diagnostics = new DiagnosticBag();

        TypeEmitter.EmitTypeBody(ring, ConstraintClosureMap.Build(slots.Refinements.Built.Model, diagnostics), slots.Refinements.Built.Model.VariadicUses, new CSharpWriter(), diagnostics);

        var refusals = diagnostics.ToImmutableArray();
        Assert.NotEmpty(refusals);
        Assert.All(refusals, refusal => Assert.Equal("MIST0500", refusal.Number.Value));
    }

    [Fact]
    public void ATypeArgumentNamingAValueRoutedParameterIsRefused()
    {
        var program = new ClassType("Program", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode());
        var name = new Method("Name", slots.Int, [], new MemberModifiers { IsStatic = true }) { DeclaringType = program, Scope = program.Scope };
        name.TypeParametersList.Add(new TypeParameter("T", 0, program.Scope, null) { DeclaringMethod = name });
        using var routes = SlotRoutes.Enter([capacity]);

        var refusal = Assert.Single(Refused(Call(name, [capacity], [], [])));
        Assert.Equal("MIST0500", refusal.Number.Value);
    }

    [Fact]
    public void AConstructionNamingAValueRoutedParameterIsRefused()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var creation = new NewExpression(HandBuiltModel.Node(), slots.Closed(ListType(), capacity), [], null, [], null);

        var refusal = Assert.Single(Refused(creation));
        Assert.Equal("MIST0500", refusal.Number.Value);
    }

    [Fact]
    public void ATypeTestNamingAValueRoutedParameterIsRefusedWithOrWithoutABinding()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var scrutinee = HandBuiltRefinements.Local(UnknownType.Instance, "x");

        var bound = Assert.Single(Refused(Is(scrutinee, Tested(capacity, "y"))));
        var bare = Assert.Single(Refused(Is(scrutinee, new TypeTestPattern(HandBuiltModel.Node(), capacity, binding: null))));
        Assert.Equal("MIST0500", bound.Number.Value);
        Assert.Equal("MIST0500", bare.Number.Value);
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

    [Fact]
    public void AnOperatorReadsTheOwnersSlotFromTheOperandThatCarriesIt()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var closing = slots.Closed(ring, capacity);
        var plus = new Operator(OperatorKind.Plus, slots.Int, [new Parameter("a", closing), new Parameter("b", closing)], new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public })
        {
            DeclaringType = ring,
            Body = HandBuiltModel.Block(new ReturnStatement(new TypeReferenceExpression(HandBuiltModel.Node(), capacity), slots.Int)),
        };
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        OperatorEmitter.Emit(plus, writer, diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Message)));
        var lines = Lines(writer);
        Assert.Contains("public static int operator +(Probe.Ring a, Probe.Ring b)", lines);
        Assert.Contains("return a.__Capacity;", lines);
    }

    [Fact]
    public void AConversionReadsTheOwnersSlotFromItsOperandAndOneWhoseOperandDoesNotCarryItIsRefused()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var closing = slots.Closed(ring, capacity);
        var outOf = new CastOperator(slots.Int, new Parameter("m", closing), isImplicit: false, new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public })
        {
            DeclaringType = ring,
            Body = HandBuiltModel.Block(read()),
        };
        var into = new CastOperator(closing, new Parameter("v", slots.Int), isImplicit: true, new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public })
        {
            DeclaringType = ring,
            Body = HandBuiltModel.Block(read()),
        };
        var writer = new CSharpWriter();
        var kept = new DiagnosticBag();
        var refused = new DiagnosticBag();

        CastOperatorEmitter.Emit(outOf, writer, kept);
        CastOperatorEmitter.Emit(into, new CSharpWriter(), refused);

        Assert.False(kept.HasErrors, string.Join("\n", kept.ToImmutableArray().Select(diagnostic => diagnostic.Message)));
        Assert.Contains("public static explicit operator int(Probe.Ring m)", Lines(writer));
        Assert.Contains("return m.__Capacity;", Lines(writer));
        var refusal = Assert.Single(refused.ToImmutableArray());
        Assert.Equal("MIST0499", refusal.Number.Value);
        Assert.StartsWith("'Capacity' of 'Ring' cannot be read in the conversion from 'int', whose operand does not carry it", refusal.Message, StringComparison.Ordinal);

        ReturnStatement read()
        {
            return new ReturnStatement(new TypeReferenceExpression(HandBuiltModel.Node(), capacity), slots.Int);
        }
    }

    [Fact]
    public void ANestedTypeReadingItsEnclosingTypesSlotIsRefusedAndItsNameDropsTheSlot()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var inner = new ClassType("Inner", "Probe", new Scope(ring.Scope), ring, new BodyTypeTypeDeclarationNode())
        {
            Modifiers = new MemberModifiers { Accessibility = Accessibility.Public },
        };
        inner.MethodsList.Add(new Method("Get", slots.Int, [], new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = inner,
            Body = HandBuiltModel.Block(new ReturnStatement(new TypeReferenceExpression(HandBuiltModel.Node(), capacity), slots.Int)),
        });
        var diagnostics = new DiagnosticBag();

        TypeEmitter.EmitTypeBody(inner, ConstraintClosureMap.Build(slots.Refinements.Built.Model, diagnostics), slots.Refinements.Built.Model.VariadicUses, new CSharpWriter(), diagnostics);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0499", refusal.Number.Value);
        Assert.StartsWith("'Capacity' of 'Ring' cannot be read in the nested type 'Inner'", refusal.Message, StringComparison.Ordinal);
        var pair = (ClassType)HandBuiltSlots.Generic(new ClassType("Pair<>", "Probe", new Scope(ring.Scope), ring, new BodyTypeTypeDeclarationNode()), "M", slots.Int);
        Assert.StartsWith("Probe.Ring.Pair<", TypeNameEmitter.Emit(slots.Closed(pair, slots.Literal(2))), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAttributeArgumentReadingAValueRoutedSlotIsRefused()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var attribute = new DeclaredAttribute(HandBuiltModel.Class("Marker"), [new TypeReferenceExpression(HandBuiltModel.Node(), capacity)], []);
        var diagnostics = new DiagnosticBag();

        AttributeEmitter.EmitLines([attribute], new CSharpWriter(), diagnostics);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0499", refusal.Number.Value);
        Assert.Contains("cannot be read in an attribute argument, which is a constant", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInstanceInitializerOfAValueRoutedTypeRunsInTheConstructorAfterTheHiddenFieldIsSet()
    {
        var counter = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Counter<>"), "N", slots.Int);
        counter.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        TypeParameterDomainResolver.ProjectValues(counter);
        var n = counter.TypeParametersList[0];
        counter.FieldsList.Add(new Field("Cap", slots.Int, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = counter,
            Initializer = new TypeReferenceExpression(HandBuiltModel.Node(), n),
        });
        counter.PropertiesList.Add(new Property("Twice", slots.Int, new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = counter,
            Getter = new PropertyAccessor { IsAuto = true },
            Initializer = new TypeReferenceExpression(HandBuiltModel.Node(), n),
        });
        using var routes = SlotRoutes.Enter([n]);

        var lines = TypeBody(counter);

        Assert.Contains("public int Cap;", lines);
        Assert.Contains("public int Twice { get; }", lines);
        var set = lines.IndexOf("this.__N = __N;");
        var cap = lines.IndexOf("this.Cap = __N;");
        var twice = lines.IndexOf("this.Twice = __N;");
        Assert.True(set >= 0 && set < cap && cap < twice, string.Join("\n", lines));
    }

    [Fact]
    public void ARestatedDefaultReadsTheAbstractsSlotAsTheImplementingTypesClosingSuppliesIt()
    {
        var (sized, n) = Sized();
        sized.MethodsList.Add(new Method("Twice", slots.Int, [], new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = sized,
            Body = HandBuiltModel.Block(new ReturnStatement(new TypeReferenceExpression(HandBuiltModel.Node(), n), slots.Int)),
        });
        var tiny = HandBuiltModel.Class("Tiny");
        tiny.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        tiny.BaseAbstractTypes.Add(slots.Closed(sized, slots.Literal(8)));
        var flex = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Flex<>"), "M", slots.Int);
        flex.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        flex.BaseAbstractTypes.Add(slots.Closed(sized, flex.TypeParametersList[0]));
        using var routes = SlotRoutes.Enter([n, flex.TypeParametersList[0]]);

        Assert.Contains("return __N;", TypeBody(sized));
        Assert.Contains("return 8;", TypeBody(tiny));
        Assert.Contains("return __M;", TypeBody(flex));
    }

    private static List<string> Lines(CSharpWriter writer)
    {
        return [.. writer.ToString().Split('\n').Select(line => line.Trim())];
    }

    private (ExtensionBlock Block, TypeParameter N) ArrayBlock()
    {
        var n = slots.Dependent("N");
        var block = new ExtensionBlock("self", "Probe", new Scope(null), new ExtensionBlockDeclarationNode()) { CarrierName = "ArrayExtensions", ResolvedReceiverType = slots.ArrayOf(n) };
        block.TypeParametersList.Add(n);
        return (block, n);
    }

    private static (ExtensionBlock Block, TypeParameter N) BoxBlock(ConstructedType receiver, TypeParameter? n = null)
    {
        n ??= (TypeParameter)receiver.TypeArguments[0];
        var block = new ExtensionBlock("self", "Probe", new Scope(null), new ExtensionBlockDeclarationNode()) { CarrierName = "BoxExtensions", ResolvedReceiverType = receiver };
        block.TypeParametersList.Add(n);
        return (block, n);
    }

    private static ClassType ListType()
    {
        var list = HandBuiltModel.Class("List<>");
        list.TypeParametersList.Add(new TypeParameter("T", 0, list.Scope, list));
        return list;
    }

    private Method Closed(Method method, ExtensionBlock block, IMistType argument)
    {
        return ExtensionSpecialization.Close(method, block, [argument], slots.Refinements.Built.Interner);
    }

    private static (ExtensionBlock Block, ClassType Meter) MeterBlock()
    {
        var meter = HandBuiltModel.Class("Meter");
        meter.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var block = new ExtensionBlock("self", "Probe", new Scope(null), new ExtensionBlockDeclarationNode()) { CarrierName = "MeterExtensions", ResolvedReceiverType = meter };
        return (block, meter);
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

    private static FunctionReferenceExpression Group(Method method, IMistType typeArgument, IMistType parameterType)
    {
        return new FunctionReferenceExpression(HandBuiltModel.Node(), null, method, new FunctionType([parameterType], method.ReturnType)) { TypeArguments = [typeArgument] };
    }

    private static FunctionReferenceExpression Bound(Expression receiver, Method method)
    {
        return new FunctionReferenceExpression(HandBuiltModel.Node(), receiver, method, new FunctionType([], method.ReturnType));
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
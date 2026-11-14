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
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// How a member renamed because its C# parameter list coincides with another's through a slot is declared,
/// and how each kind of reference spells the new name, over hand-built declarations beside the value route's
/// emission tests (runtime-dependent-values design, <em>Identity by value: the first delivery</em>, item 3).
/// </summary>
[Trait("Tier", "2")]
public sealed class OverloadNameEmissionTests
{
    private const string Three = "Name__Probe_dRing_l3_r";
    private const string Four = "Name__Probe_dRing_l4_r";

    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;

    public OverloadNameEmissionTests()
    {
        ring = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        ring.Modifiers = Public;
        TypeParameterDomainResolver.ProjectValues(ring);
        capacity = ring.TypeParametersList[0];
    }

    [Fact]
    public void ACoincidingPairIsDeclaredUnderItsNewNames_AndAProgramWithoutOneUnderItsOwn()
    {
        var (program, _, _) = Pair();

        using(var routes = SlotRoutes.Enter([capacity]))
        using(Names(program))
        {
            var lines = TypeBody(program);

            Assert.Contains($"public int {Three}(Probe.Ring p0)", lines);
            Assert.Contains($"public int {Four}(Probe.Ring p0)", lines);
        }

        using(var routes = SlotRoutes.Enter([]))
        using(Names(program))
        {
            var lines = TypeBody(program);

            Assert.Equal(2, lines.Count(line => line.StartsWith("public int Name(", StringComparison.Ordinal)));
            Assert.DoesNotContain(lines, line => line.Contains("Name__", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ACallByNameAndACallThroughAReceiverSpellTheChosenOverloadsName()
    {
        var (program, three, four) = Pair();
        var r = HandBuiltRefinements.Local(Ring(4), "r");
        var p = HandBuiltRefinements.Local(program, "p");
        using var routes = SlotRoutes.Enter([capacity]);
        using var names = Names(program);

        Assert.Equal($"{Four}(r)", Emit(Call(four, r)));
        Assert.Equal($"p.{Three}(r)", Emit(Through(p, three, r)));
    }

    [Fact]
    public void AMethodGroupOfOneMemberOfThePairSpellsThatMembersName()
    {
        var (program, _, four) = Pair();
        var p = HandBuiltRefinements.Local(program, "p");
        using var routes = SlotRoutes.Enter([capacity]);
        using var names = Names(program);

        var group = new FunctionReferenceExpression(HandBuiltModel.Node(), p, four, new FunctionType([Ring(4)], slots.Int));

        Assert.Equal($"p.{Four}", Emit(group));
    }

    [Fact]
    public void ABaseMemberADerivedOneWouldHideIsDeclaredReachedAndGroupedUnderItsNewName()
    {
        var baseClass = Declared("Base");
        var three = Method(baseClass, "Name", Ring(3));
        var derived = Declared("Derived");
        derived.BaseType = baseClass;
        _ = Method(derived, "Name", Ring(4));
        var d = HandBuiltRefinements.Local(derived, "d");
        var r = HandBuiltRefinements.Local(Ring(3), "r");
        using var routes = SlotRoutes.Enter([capacity]);
        using var names = Names(baseClass, derived);

        Assert.Contains($"public int {Three}(Probe.Ring p0)", TypeBody(baseClass));
        Assert.Contains($"public int {Four}(Probe.Ring p0)", TypeBody(derived));
        Assert.EndsWith($".{Three}(r)", Emit(Through(d, three, r)), StringComparison.Ordinal);
        Assert.Equal($"d.{Three}", Emit(new FunctionReferenceExpression(HandBuiltModel.Node(), d, three, new FunctionType([Ring(3)], slots.Int))));
    }

    [Fact]
    public void ABaseIndexerADerivedOneWouldHideIsReadAndWrittenThroughItsAccessorsFromADerivedReceiver()
    {
        var baseClass = Declared("Base");
        var three = AddIndexer(baseClass, 3);
        var derived = Declared("Derived");
        derived.BaseType = baseClass;
        _ = AddIndexer(derived, 4);
        var d = HandBuiltRefinements.Local(derived, "d");
        var element = new IndexExpression(HandBuiltModel.Node(), d, HandBuiltRefinements.Local(Ring(3), "r"), slots.Int) { Indexer = three };
        using var routes = SlotRoutes.Enter([capacity]);
        using var names = Names(baseClass, derived);

        Assert.Contains("public int get_Item__Probe_dRing_l3_r(Probe.Ring r)", TypeBody(baseClass));
        Assert.Contains("public int get_Item__Probe_dRing_l4_r(Probe.Ring r)", TypeBody(derived));
        Assert.Equal("d.get_Item__Probe_dRing_l3_r(r)", Emit(element));
        Assert.Equal(
            "d.set_Item__Probe_dRing_l3_r(r, 1)",
            Emit(new AssignmentExpression(HandBuiltModel.Node(), element, HandBuiltModel.Literal(slots.Int, 1), slots.Int)));
    }

    [Fact]
    public void AnAbstractsPairAndTheImplementingTypesPairAreDeclaredUnderOneName()
    {
        var namer = new AbstractType("Namer", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode()) { Modifiers = Public };
        _ = Method(namer, "Name", Ring(3));
        var slotFour = Method(namer, "Name", Ring(4));
        var plain = Declared("Plain");
        plain.BaseAbstractTypes.Add(namer);
        _ = Method(plain, "Name", Ring(3));
        _ = Method(plain, "Name", Ring(4));
        var held = HandBuiltRefinements.Local(namer, "namer");
        using var routes = SlotRoutes.Enter([capacity]);
        using var names = Names(namer, plain);

        Assert.Contains($"int {Three}(Probe.Ring p0);", TypeBody(namer));
        Assert.Contains($"public int {Four}(Probe.Ring p0)", TypeBody(plain));
        Assert.Equal($"namer.{Four}(r)", Emit(Through(held, slotFour, HandBuiltRefinements.Local(Ring(4), "r"))));
    }

    [Fact]
    public void AnExtensionBlocksPairIsDeclaredUnderItsNewNames()
    {
        var block = new ExtensionBlock("self", "Probe", new Scope(null), new ExtensionBlockDeclarationNode()) { CarrierName = "TextExtensions", ResolvedReceiverType = slots.Int };
        foreach(var value in new[] { 3, 4 })
        {
            block.MethodsList.Add(new Method("Name", slots.Int, [new Parameter("r", Ring(value))], Public) { DeclaringBlock = block, Scope = block.Scope, DeclarationNode = Node() });
        }

        using var routes = SlotRoutes.Enter([capacity]);
        using var names = OverloadNames.Enter(OverloadNames.Decide([], [block], interner: null), new DiagnosticBag());
        var writer = new CSharpWriter();
        var emitted = false;

        ExtensionBlockEmitter.Emit(block, writer, new DiagnosticBag(), ref emitted);

        Assert.Contains($"public static int {Three}(this int self, Probe.Ring r)", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains($"public static int {Four}(this int self, Probe.Ring r)", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ARenamedConstructorTakesItsMarker_AndACreationAndAThisChainPassIt()
    {
        var box = Declared("Box");
        var three = AddConstructor(box, 3);
        var four = new Constructor("Box", box, [new Parameter("r", Ring(4))], Public)
        {
            DeclaringType = box,
            Scope = box.Scope,
            DeclarationNode = Node(),
            Body = HandBuiltModel.Block(),
            ChainKind = ConstructorChainKind.This,
            ChainedConstructor = three,
        };
        four.ChainArgumentsList.Add(HandBuiltRefinements.Local(Ring(3), "spare"));
        box.ConstructorsList.Add(four);
        using var routes = SlotRoutes.Enter([capacity]);
        using var names = Names(box);

        var lines = TypeBody(box);

        Assert.Contains("public readonly struct __Ctor__Probe_dRing_l3_r { }", lines);
        Assert.Contains("public Box(__Ctor__Probe_dRing_l3_r __overload, Probe.Ring r)", lines);
        Assert.Contains("public Box(__Ctor__Probe_dRing_l4_r __overload, Probe.Ring r) : this(default(__Ctor__Probe_dRing_l3_r), spare)", lines);
        Assert.Equal(
            "new Box(default(Box.__Ctor__Probe_dRing_l4_r), r)",
            Emit(new NewExpression(HandBuiltModel.Node(), box, [HandBuiltRefinements.Local(Ring(4), "r")], four, [], [Ring(4)])));
    }

    [Fact]
    public void ARenamedIndexerIsItsAccessorMethods_AndAReadAWriteAndACompoundCallThem()
    {
        var shelf = Declared("Shelf");
        var three = AddIndexer(shelf, 3);
        _ = AddIndexer(shelf, 4);
        var s = HandBuiltRefinements.Local(shelf, "s");
        var r = HandBuiltRefinements.Local(Ring(3), "r");
        var one = HandBuiltModel.Literal(slots.Int, 1);
        var element = new IndexExpression(HandBuiltModel.Node(), s, r, slots.Int) { Indexer = three };
        using var routes = SlotRoutes.Enter([capacity]);
        using var names = Names(shelf);

        var lines = TypeBody(shelf);

        Assert.Contains("public int get_Item__Probe_dRing_l3_r(Probe.Ring r)", lines);
        Assert.Contains("public int set_Item__Probe_dRing_l3_r(Probe.Ring r, int value)", lines);
        Assert.Contains("return value;", lines);
        Assert.DoesNotContain(lines, line => line.Contains("this[", StringComparison.Ordinal));
        Assert.Equal("s.get_Item__Probe_dRing_l3_r(r)", Emit(element));
        Assert.Equal("s.set_Item__Probe_dRing_l3_r(r, 1)", Emit(new AssignmentExpression(HandBuiltModel.Node(), element, one, slots.Int)));
        Assert.Equal(
            "((s, r) switch { (var __e1, var __e2) => __e1.set_Item__Probe_dRing_l3_r(__e2, __e1.get_Item__Probe_dRing_l3_r(__e2) + (1)) })",
            Emit(new CompoundAssignmentExpression(HandBuiltModel.Node(), element, BinaryOperatorKind.Add, one, slots.Int, null, IncrementForm.None)));
    }

    [Fact]
    public void ARenamedOperatorIsAStaticMethod_AndAnOperationCallsIt()
    {
        var meter = Declared("Meter");
        var plusThree = AddOperator(meter, 3);
        _ = AddOperator(meter, 4);
        var m = HandBuiltRefinements.Local(meter, "m");
        var r = HandBuiltRefinements.Local(Ring(3), "r");
        using var routes = SlotRoutes.Enter([capacity]);
        using var names = Names(meter);

        Assert.Contains("public static int op_Addition__Probe_dMeter_pProbe_dRing_l3_r(Meter a, Probe.Ring r)", TypeBody(meter));
        Assert.Equal(
            "Meter.op_Addition__Probe_dMeter_pProbe_dRing_l3_r(m, r)",
            Emit(new BinaryExpression(HandBuiltModel.Node(), m, BinaryOperatorKind.Add, r, slots.Int) { Operator = plusThree }));
    }

    [Fact]
    public void ARenamedConversionIsAStaticMethod_AndAnImplicitConversionAndACastCallIt()
    {
        var label = Declared("Label");
        _ = AddConversion(label, 3);
        var fromFour = AddConversion(label, 4);
        var r = HandBuiltRefinements.Local(Ring(4), "r");
        using var routes = SlotRoutes.Enter([capacity]);
        using var names = Names(label);
        const string renamed = "op_Implicit_Label__Probe_dRing_l4_r_p_tProbe_dLabel";

        Assert.Contains($"public static Label {renamed}(Probe.Ring r)", TypeBody(label));
        Assert.Equal(
            $"Label.{renamed}(r)",
            Emit(new ImplicitConversionExpression(HandBuiltModel.Node(), r, label, [new ConversionFunction(fromFour, Ring(4), label)])));
        Assert.Equal(
            $"Label.{renamed}(r)",
            Emit(new CastExpression(HandBuiltModel.Node(), r, label, [new ConversionFunction(fromFour, Ring(4), label)], label)));
    }

    private (ClassType Program, Method Three, Method Four) Pair()
    {
        var program = Declared("Program");
        return (program, Method(program, "Name", Ring(3)), Method(program, "Name", Ring(4)));
    }

    private ConstructedType Ring(int value)
    {
        return slots.Closed(ring, slots.Literal(value));
    }

    private static ClassType Declared(string name)
    {
        return new ClassType(name, "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode()) { Modifiers = Public };
    }

    private Method Method(MemberType owner, string name, params IMistType[] parameters)
    {
        var method = new Method(name, slots.Int, [.. parameters.Select((type, i) => new Parameter($"p{i}", type))], Public)
        {
            DeclaringType = owner,
            Scope = owner.Scope,
            DeclarationNode = Node(),
            Body = owner is AbstractType ? null : HandBuiltModel.Block(),
        };
        owner.MethodsList.Add(method);
        return method;
    }

    private Constructor AddConstructor(ClassType owner, int value)
    {
        var constructor = new Constructor(owner.Name, owner, [new Parameter("r", Ring(value))], Public)
        {
            DeclaringType = owner,
            Scope = owner.Scope,
            DeclarationNode = Node(),
            Body = HandBuiltModel.Block(),
        };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private Indexer AddIndexer(ClassType owner, int value)
    {
        var indexer = new Indexer(slots.Int, [new Parameter("r", Ring(value))], Public)
        {
            DeclaringType = owner,
            DeclarationNode = Node(),
            Getter = new PropertyAccessor { ExpressionBody = HandBuiltModel.Literal(slots.Int, value) },
            Setter = new PropertyAccessor { Body = HandBuiltModel.Block() },
        };
        owner.IndexersList.Add(indexer);
        return indexer;
    }

    private Operator AddOperator(ClassType owner, int value)
    {
        var op = new Operator(OperatorKind.Plus, slots.Int, [new Parameter("a", owner), new Parameter("r", Ring(value))], new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true })
        {
            DeclaringType = owner,
            Scope = owner.Scope,
            DeclarationNode = Node(),
            Body = HandBuiltModel.Block(),
        };
        owner.OperatorsList.Add(op);
        return op;
    }

    private CastOperator AddConversion(ClassType owner, int value)
    {
        var conversion = new CastOperator(owner, new Parameter("r", Ring(value)), true, new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true })
        {
            DeclaringType = owner,
            Scope = owner.Scope,
            DeclarationNode = Node(),
            Body = HandBuiltModel.Block(),
        };
        owner.CastOperatorsList.Add(conversion);
        return conversion;
    }

    private IDisposable Names(params MemberType[] types)
    {
        return OverloadNames.Enter(OverloadNames.Decide(types, [], slots.Refinements.Built.Interner), new DiagnosticBag());
    }

    private static InvocationExpression Call(Method method, Expression argument)
    {
        return new InvocationExpression(
            HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method, []), [argument], method.ReturnType, method, [argument.Type]);
    }

    private static InvocationExpression Through(Expression receiver, Method method, Expression argument)
    {
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType, []);
        return new InvocationExpression(HandBuiltModel.Node(), access, [argument], method.ReturnType, method, [argument.Type]);
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

    private static Storm.Runtime.SyntaxNode Node()
    {
        return new BodyTypeTypeDeclarationNode();
    }
}
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Which members take a new C# name because their emitted parameter lists coincide through a slot, and
/// which name (runtime-dependent-values design, <em>Identity by value: the first delivery</em>, item 3),
/// decided over hand-built members.
/// </summary>
[Trait("Tier", "1")]
public sealed class OverloadNameDecisionTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;

    public OverloadNameDecisionTests()
    {
        ring = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        ring.Modifiers = Public;
        TypeParameterDomainResolver.ProjectValues(ring);
        capacity = ring.TypeParametersList[0];
    }

    [Fact]
    public void ACoincidingPairIsNamedFromEachMembersOwnSignature_AndAMemberCoincidingWithNothingKeepsItsName()
    {
        var program = Declared("Program");
        var three = Method(program, "Name", Ring(3));
        var four = Method(program, "Name", Ring(4));
        var other = Method(program, "Other", Ring(4));
        using var routes = SlotRoutes.Enter([capacity]);

        var renamed = Decide(program);

        Assert.Equal("Name__Probe_dRing_l3_r", renamed[three.DeclarationNode!]);
        Assert.Equal("Name__Probe_dRing_l4_r", renamed[four.DeclarationNode!]);
        Assert.False(renamed.ContainsKey(other.DeclarationNode!));
    }

    [Fact]
    public void AThirdOverloadDoesNotChangeTheNamesOfTheFirstTwo()
    {
        var program = Declared("Program");
        var three = Method(program, "Name", Ring(3));
        var four = Method(program, "Name", Ring(4));
        using var routes = SlotRoutes.Enter([capacity]);
        var before = Decide(program);

        var five = Method(program, "Name", Ring(5));
        var after = Decide(program);

        Assert.Equal(before[three.DeclarationNode!], after[three.DeclarationNode!]);
        Assert.Equal(before[four.DeclarationNode!], after[four.DeclarationNode!]);
        Assert.Equal("Name__Probe_dRing_l5_r", after[five.DeclarationNode!]);
    }

    [Fact]
    public void NothingIsRenamedWhereTheSlotStaysInTheCSharpType()
    {
        var program = Declared("Program");
        _ = Method(program, "Name", Ring(3));
        _ = Method(program, "Name", Ring(4));
        using var routes = SlotRoutes.Enter([]);

        Assert.Empty(Decide(program));
    }

    [Fact]
    public void AnErasedLengthCoincidesWithoutAnyRoutedSlot()
    {
        var program = Declared("Program");
        var three = Method(program, "Name", slots.ArrayOf(slots.Literal(3)));
        var four = Method(program, "Name", slots.ArrayOf(slots.Literal(4)));
        using var routes = SlotRoutes.Enter([]);

        var renamed = Decide(program);

        Assert.NotEqual(renamed[three.DeclarationNode!], renamed[four.DeclarationNode!]);
    }

    [Fact]
    public void AHiddenLeadingSlotParameterMakesTwoListsCoincide_AndTheArityIsPartOfTheName()
    {
        var program = Declared("Meter");
        var n = slots.Dependent("N");
        var routed = Method(program, "Foo", slots.Int);
        routed.TypeParametersList.Add(n);
        n.DeclaringMethod = routed;
        var plain = Method(program, "Foo", slots.Int, slots.Int);
        using var routes = SlotRoutes.Enter([n]);

        var renamed = Decide(program);

        Assert.StartsWith("Foo___g1_p", renamed[routed.DeclarationNode!], StringComparison.Ordinal);
        Assert.StartsWith("Foo__", renamed[plain.DeclarationNode!], StringComparison.Ordinal);
        Assert.NotEqual(renamed[routed.DeclarationNode!], renamed[plain.DeclarationNode!]);
    }

    [Fact]
    public void ConstructorsIndexersOperatorsAndConversionsAreRenamedLikeMethods()
    {
        var box = Declared("Box");
        var constructors = new[] { 3, 4 }.Select(value => Add(box, new Constructor("Box", box, [new Parameter("r", Ring(value))], Public) { DeclaringType = box, DeclarationNode = Node() })).ToList();
        var indexers = new[] { 3, 4 }.Select(value => AddIndexer(box, value)).ToList();
        var operators = new[] { 3, 4 }.Select(value => AddOperator(box, value)).ToList();
        var conversions = new[] { 3, 4 }.Select(value => AddConversion(box, value)).ToList();
        using var routes = SlotRoutes.Enter([capacity]);

        var renamed = Decide(box);

        Assert.Equal("__Ctor__Probe_dRing_l3_r", renamed[constructors[0].DeclarationNode!]);
        Assert.Equal("Item__Probe_dRing_l4_r", renamed[indexers[1].DeclarationNode!]);
        Assert.Equal("op_Addition__Probe_dBox_pProbe_dRing_l3_r", renamed[operators[0].DeclarationNode!]);
        Assert.Equal("op_Implicit_Box__Probe_dRing_l4_r_p_tProbe_dBox", renamed[conversions[1].DeclarationNode!]);
        Assert.Equal(8, renamed.Count);
    }

    [Fact]
    public void AnAbstractsMemberAndTheMemberAnsweringItTakeOneName_DerivedFromTheAbstracts()
    {
        var namer = new AbstractType("Namer", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode()) { Modifiers = Public };
        var slotThree = Method(namer, "Name", Ring(3));
        var slotFour = Method(namer, "Name", Ring(4));
        var plain = Declared("Plain");
        plain.BaseAbstractTypes.Add(namer);
        var answerThree = Method(plain, "Name", Ring(3));
        var answerFour = Method(plain, "Name", Ring(4));
        var quiet = Declared("Quiet");
        quiet.BaseAbstractTypes.Add(namer);
        var onlyAnswer = Method(quiet, "Name", Ring(3));
        using var routes = SlotRoutes.Enter([capacity]);

        var renamed = Decide(namer, plain, quiet);

        Assert.Equal(renamed[slotThree.DeclarationNode!], renamed[answerThree.DeclarationNode!]);
        Assert.Equal(renamed[slotFour.DeclarationNode!], renamed[answerFour.DeclarationNode!]);
        Assert.Equal(renamed[slotThree.DeclarationNode!], renamed[onlyAnswer.DeclarationNode!]);
    }

    [Fact]
    public void AMemberAnsweringASlotWithNoMistSourceKeepsItsName_AndTheOtherIsRenamedAroundIt()
    {
        var equatable = new AbstractType("IEquatable", "System", new Scope(null), null, null) { Modifiers = Public };
        var clrSlot = new Method("Equals", slots.Int, [new Parameter("other", Ring(3))], Public) { DeclaringType = equatable, Scope = equatable.Scope };
        equatable.MethodsList.Add(clrSlot);
        var both = Declared("Both");
        both.BaseAbstractTypes.Add(equatable);
        var pinned = Method(both, "Equals", Ring(3));
        var other = Method(both, "Equals", Ring(4));
        using var routes = SlotRoutes.Enter([capacity]);

        var decision = OverloadNames.Decide([equatable, both], [], slots.Refinements.Built.Interner);

        Assert.False(decision.Renamed.ContainsKey(pinned.DeclarationNode!));
        Assert.Equal("Equals__Probe_dRing_l4_r", decision.Renamed[other.DeclarationNode!]);
        Assert.Empty(decision.Unnamed);
    }

    [Fact]
    public void OneSlotAnsweredAtTwoClosingsHasNoCSharpNameToGive_AndIsMist0497()
    {
        var generic = new AbstractType("IEquatable<>", "System", new Scope(null), null, null) { Modifiers = Public };
        var t = new TypeParameter("T", 0, generic.Scope, generic);
        generic.TypeParametersList.Add(t);
        generic.MethodsList.Add(new Method("Equals", slots.Int, [new Parameter("other", t)], Public) { DeclaringType = generic, Scope = generic.Scope });
        var both = Declared("Both");
        both.BaseAbstractTypes.Add(slots.Closed(generic, Ring(3)));
        both.BaseAbstractTypes.Add(slots.Closed(generic, Ring(4)));
        _ = Method(both, "Equals", Ring(3));
        _ = Method(both, "Equals", Ring(4));
        using var routes = SlotRoutes.Enter([capacity]);
        var decision = OverloadNames.Decide([generic, both], [], slots.Refinements.Built.Interner);
        var diagnostics = new DiagnosticBag();

        using(OverloadNames.Enter(decision, diagnostics))
        {
        }

        Assert.Empty(decision.Renamed);
        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0497", refusal.Number.Value);
        Assert.Contains("coincide with 'Equals(Ring<3>)': the two differ only in 'Capacity'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFlattenedAncestorsMemberCoincidesWithTheDerivedStructs()
    {
        var baseStruct = HandBuiltModel.Struct("Base");
        baseStruct.Modifiers = Public;
        var inherited = Method(baseStruct, "Name", Ring(3));
        var derived = new StructType("Derived", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode()) { Modifiers = Public, BaseType = baseStruct };
        var own = Method(derived, "Name", Ring(4));
        using var routes = SlotRoutes.Enter([capacity]);

        var renamed = Decide(derived);

        Assert.True(renamed.ContainsKey(inherited.DeclarationNode!));
        Assert.True(renamed.ContainsKey(own.DeclarationNode!));
    }

    [Fact]
    public void ADefaultAConformerRestatesCoincidesWithTheConformersOwnMember()
    {
        var namer = new AbstractType("Namer", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode()) { Modifiers = Public };
        var fallback = Method(namer, "Name", Ring(4));
        var withBody = new Method(fallback.Name, fallback.ReturnType, fallback.ParametersList, fallback.Modifiers)
        {
            DeclaringType = namer,
            Scope = namer.Scope,
            DeclarationNode = fallback.DeclarationNode,
            Body = HandBuiltModel.Block(),
        };
        namer.MethodsList[0] = withBody;
        var plain = Declared("Plain");
        plain.BaseAbstractTypes.Add(namer);
        var own = Method(plain, "Name", Ring(3));
        using var routes = SlotRoutes.Enter([capacity]);

        var renamed = Decide(namer, plain);

        Assert.True(renamed.ContainsKey(withBody.DeclarationNode!));
        Assert.True(renamed.ContainsKey(own.DeclarationNode!));
    }

    [Fact]
    public void ABaseClassesMemberCoincidesWithTheDerivedClasses_AndEachIsNamedFromItsOwnSignature()
    {
        var baseClass = Declared("Base");
        var inherited = Method(baseClass, "Name", Ring(3));
        var derived = Declared("Derived");
        derived.BaseType = baseClass;
        var own = Method(derived, "Name", Ring(4));
        var sibling = Declared("Sibling");
        sibling.BaseType = baseClass;
        var quiet = Method(sibling, "Name", slots.Int);
        using var routes = SlotRoutes.Enter([capacity]);

        var decision = OverloadNames.Decide([baseClass, derived, sibling], [], slots.Refinements.Built.Interner);

        Assert.Equal("Name__Probe_dRing_l3_r", decision.Renamed[inherited.DeclarationNode!]);
        Assert.Equal("Name__Probe_dRing_l4_r", decision.Renamed[own.DeclarationNode!]);
        Assert.False(decision.Renamed.ContainsKey(quiet.DeclarationNode!));
        Assert.Equal(2, decision.Renamed.Count);
        Assert.Empty(decision.Unnamed);
    }

    [Fact]
    public void AGenericBasesMemberCoincidesOnlyAtTheClosingADerivedClassWrites_AndKeepsOneNameForEveryClosing()
    {
        var generic = Declared("Base<>");
        var t = new TypeParameter("T", 0, generic.Scope, generic);
        generic.TypeParametersList.Add(t);
        var inherited = Method(generic, "Name", t);
        var derived = Declared("Derived");
        derived.BaseType = slots.Closed(generic, Ring(3));
        var own = Method(derived, "Name", Ring(4));
        var other = Declared("Other");
        other.BaseType = slots.Closed(generic, slots.Int);
        var apart = Method(other, "Name", Ring(4));
        using var routes = SlotRoutes.Enter([capacity]);

        var renamed = Decide(generic, derived, other);

        Assert.Equal(OverloadNames.NameFor(inherited), renamed[inherited.DeclarationNode!]);
        Assert.Equal("Name__Probe_dRing_l4_r", renamed[own.DeclarationNode!]);
        Assert.False(renamed.ContainsKey(apart.DeclarationNode!));
    }

    [Fact]
    public void ABaseClassesIndexerCoincidesWithTheDerivedClasses()
    {
        var baseClass = Declared("Base");
        var inherited = AddIndexer(baseClass, 3);
        var derived = Declared("Derived");
        derived.BaseType = baseClass;
        var own = AddIndexer(derived, 4);
        using var routes = SlotRoutes.Enter([capacity]);

        var renamed = Decide(baseClass, derived);

        Assert.Equal("Item__Probe_dRing_l3_r", renamed[inherited.DeclarationNode!]);
        Assert.Equal("Item__Probe_dRing_l4_r", renamed[own.DeclarationNode!]);
    }

    [Fact]
    public void ABaseMemberAnsweringASlotWithNoMistSourceKeepsItsName_AndTheDerivedMemberIsRenamedAroundIt()
    {
        var equatable = new AbstractType("IEquatable", "System", new Scope(null), null, null) { Modifiers = Public };
        equatable.MethodsList.Add(new Method("Equals", slots.Int, [new Parameter("other", Ring(3))], Public) { DeclaringType = equatable, Scope = equatable.Scope });
        var baseClass = Declared("Base");
        baseClass.BaseAbstractTypes.Add(equatable);
        var pinned = Method(baseClass, "Equals", Ring(3));
        var derived = Declared("Derived");
        derived.BaseType = baseClass;
        var own = Method(derived, "Equals", Ring(4));
        using var routes = SlotRoutes.Enter([capacity]);

        var decision = OverloadNames.Decide([equatable, baseClass, derived], [], slots.Refinements.Built.Interner);

        Assert.False(decision.Renamed.ContainsKey(pinned.DeclarationNode!));
        Assert.Equal("Equals__Probe_dRing_l4_r", decision.Renamed[own.DeclarationNode!]);
        Assert.Empty(decision.Unnamed);
    }

    [Fact]
    public void AnExtensionBlocksPairIsRenamed()
    {
        var block = new ExtensionBlock("self", "Probe", new Scope(null), new ExtensionBlockDeclarationNode()) { CarrierName = "StringExtensions", ResolvedReceiverType = slots.Int };
        var three = new Method("Name", slots.Int, [new Parameter("r", Ring(3))], Public) { DeclaringBlock = block, Scope = block.Scope, DeclarationNode = Node() };
        var four = new Method("Name", slots.Int, [new Parameter("r", Ring(4))], Public) { DeclaringBlock = block, Scope = block.Scope, DeclarationNode = Node() };
        block.MethodsList.AddRange([three, four]);
        using var routes = SlotRoutes.Enter([capacity]);

        var decision = OverloadNames.Decide([], [block], interner: null);

        Assert.Equal(2, decision.Renamed.Count);
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
        };
        owner.MethodsList.Add(method);
        return method;
    }

    private static Constructor Add(ClassType owner, Constructor constructor)
    {
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private Indexer AddIndexer(ClassType owner, int value)
    {
        var indexer = new Indexer(slots.Int, [new Parameter("r", Ring(value))], Public) { DeclaringType = owner, DeclarationNode = Node() };
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
        };
        owner.CastOperatorsList.Add(conversion);
        return conversion;
    }

    private IReadOnlyDictionary<Storm.Runtime.SyntaxNode, string> Decide(params MemberType[] types)
    {
        return OverloadNames.Decide(types, [], slots.Refinements.Built.Interner).Renamed;
    }

    private static Storm.Runtime.SyntaxNode Node()
    {
        return new BodyTypeTypeDeclarationNode();
    }
}
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// A member reached through a value whose class the value's type closes with <c>unknown</c>, judged by the
/// refinement analyzer over hand-built bodies: its signature still reads that value's own slot
/// (runtime-dependent-values Requirement 7.6), proven by the value's creation or by its own read.
/// </summary>
[Trait("Tier", "2")]
public sealed class UnknownReceiverSlotFlowTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType ring;
    private readonly TypeParameter capacity;
    private readonly LocalVariable n;
    private readonly LocalVariable m;
    private readonly LocalVariable u;

    public UnknownReceiverSlotFlowTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.ZeroOrPositive);
        capacity = ring.TypeParametersList[0];
        var range = slots.Refinements.Between(slots.Int, 11L, 29L, "Range");
        n = new LocalVariable("n", range);
        m = new LocalVariable("m", range);
        u = new LocalVariable("u", slots.Closed(ring, UnknownType.Instance));
    }

    private IMistType Void => slots.Refinements.Built.Type("Void");

    private IMistType Unknown => slots.Refinements.Built.Interner.GetOrCreateUnknownValue(slots.Int);

    private IReadOnlyList<Diagnostic> Analyze(params Statement[] statements)
    {
        return slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(statements));
    }

    private static void Refused(IReadOnlyList<Diagnostic> diagnostics, string message)
    {
        var refused = Assert.Single(diagnostics);
        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains(message, refused.Message, StringComparison.Ordinal);
    }

    private LocalDeclarationStatement RingOfM()
    {
        return OfM(u, ring);
    }

    private LocalDeclarationStatement OfM(LocalVariable local, MemberType definition)
    {
        var made = slots.Capture(m, slots.Int);
        return HandBuiltSlots.Declare(local, new NewExpression(HandBuiltModel.Node(), slots.Closed(definition, made.Value), [], null, [], []) { Captures = [made] });
    }

    private ExpressionStatement Load(Expression length, LocalVariable? through = null, bool conditional = false, Expression? receiver = null, IMistType? slot = null)
    {
        var method = new Method("Load", Void, [new Parameter("items", slots.ArrayOf(capacity))], new MemberModifiers()) { DeclaringType = ring };
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), receiver ?? HandBuiltRefinements.Read(through ?? u), "Load", Void, isConditional: conditional);
        return HandBuiltRefinements.Evaluate(new InvocationExpression(HandBuiltModel.Node(), callee, [length], Void, method, [slots.ArrayOf(slot ?? Unknown)]));
    }

    private Operator Plus()
    {
        return new Operator(OperatorKind.Plus, slots.Closed(ring, capacity), [new Parameter("a", slots.Closed(ring, capacity)), new Parameter("b", slots.ArrayOf(capacity))],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = ring };
    }

    private ExpressionStatement Add(Expression length)
    {
        var closed = slots.Closed(ring, Unknown);
        return HandBuiltRefinements.Evaluate(new BinaryExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(u), BinaryOperatorKind.Add, length, closed)
        {
            Operator = Plus(),
            ParameterTypes = [closed, slots.ArrayOf(Unknown)],
        });
    }

    private ExpressionStatement AddTo(Expression length)
    {
        var closed = slots.Closed(ring, Unknown);
        return HandBuiltRefinements.Evaluate(new CompoundAssignmentExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(u), BinaryOperatorKind.Add, length, u.Type, Plus(), IncrementForm.None)
        {
            ResultType = closed,
            ParameterTypes = [closed, slots.ArrayOf(Unknown)],
        });
    }

    private ArrayCreationExpression Sized(LocalVariable length)
    {
        return slots.Create(slots.Capture(length));
    }

    private NewExpression Ring(LocalVariable length)
    {
        var made = slots.Capture(length, slots.Int);
        return new NewExpression(HandBuiltModel.Node(), slots.Closed(ring, made.Value), [], null, [], []) { Captures = [made] };
    }

    private ArrayCreationExpression SizedByReassigning(LocalVariable length)
    {
        var reassigned = HandBuiltRefinements.Assign(HandBuiltRefinements.Read(u), Ring(length));
        var read = new MemberAccessExpression(HandBuiltModel.Node(), reassigned, capacity.Name, slots.Int, member: new TypeResult(capacity));
        return slots.Create(new SlotCapture("(u = new Ring()).Capacity", slots.ZeroOrPositive, slots.Int, read, 11));
    }

    private ExpressionStatement Put(Expression length)
    {
        var scope = new ExtensionScope(null);
        var block = new ExtensionBlock("self", "", scope, null);
        var c = new TypeParameter("C", 0, scope, null) { IsDependent = true, Domain = slots.ZeroOrPositive };
        block.TypeParametersList.Add(c);
        block.ResolvedReceiverType = slots.Closed(ring, c);
        var open = new Method("Put", Void, [new Parameter("items", slots.ArrayOf(c))], new MemberModifiers()) { DeclaringBlock = block };
        var closed = new Method("Put", Void, [new Parameter("items", slots.ArrayOf(Unknown))], new MemberModifiers()) { DeclaringBlock = block, SourceDeclaration = open };
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(u), "Put", Void);
        return HandBuiltRefinements.Evaluate(new InvocationExpression(HandBuiltModel.Node(), callee, [length], Void, closed, [slots.ArrayOf(Unknown)]));
    }

    private LocalVariable Optional()
    {
        return new LocalVariable("u", slots.Refinements.Built.Interner.GetOrCreateUnionType([slots.Closed(ring, UnknownType.Instance), Void]));
    }

    private IMistType Lifted(IMistType held)
    {
        return slots.Refinements.Built.Interner.GetOrCreateUnionType([held, Void]);
    }

    private (LocalVariable Holder, Property Member) Holding(IMistType closing)
    {
        var holder = HandBuiltSlots.Generic(HandBuiltModel.Class("Hd<>"), "K", slots.ZeroOrPositive);
        var member = new Property("R", Lifted(slots.Closed(ring, holder.TypeParametersList[0])), new MemberModifiers()) { DeclaringType = holder };
        return (new LocalVariable("h", slots.Closed(holder, closing)), member);
    }

    private ExpressionStatement LoadThrough(LocalVariable h, Property member, LocalVariable length, bool known)
    {
        var owner = member.DeclaringType!;
        var own = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(h), "K", slots.Int, member: new TypeResult(owner.TypeParametersList[0]));
        var k = new SlotCapture("h.K", slots.ZeroOrPositive, slots.Int, own, 5);
        IMistType slot = known ? k.Value : UnknownType.Instance;
        var reached = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(h), member.Name, Lifted(slots.Closed(ring, slot)), member: new PropertyResult(member, owner, []))
        {
            Captures = known ? [k] : [],
        };
        return Load(Sized(length), conditional: true, receiver: reached, slot: known ? slot : null);
    }

    private ExpressionStatement Keep(LocalVariable h, Field member, Expression value)
    {
        var owner = member.DeclaringType!;
        var target = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(h), member.Name, Lifted(slots.Closed(ring, UnknownType.Instance)), member: new FieldResult(member, owner, []));
        return HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(target, value));
    }

    private ExpressionStatement Store(LocalVariable through, Expression value)
    {
        var last = new Field("Last", slots.ArrayOf(capacity), OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = ring };
        var lifted = slots.Refinements.Built.Interner.GetOrCreateUnionType([slots.ArrayOf(Unknown), Void]);
        var target = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(through), last.Name, lifted, isConditional: true, member: new FieldResult(last, ring, []));
        return HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(target, value));
    }

    [Fact]
    public void AMethodReachedThroughAValueCreatedAtAnotherLengthIsRefusedAnArrayItsSlotIsNotProvenToEqual()
    {
        Refused(Analyze(RingOfM(), Load(Sized(n))), "'int[n]' is not proven to be 'int[u.Capacity]': 'n' and 'u.Capacity' are not proven identical (fails when m <= n - 1)");
    }

    [Fact]
    public void AMethodReachedThroughAValueNothingIsKnownOfIsRefusedAnArrayOfAnyOtherLength()
    {
        Refused(Analyze(Load(Sized(n))), "'n' and 'u.Capacity' are not proven identical (fails when n <= u.Capacity - 1)");
    }

    [Fact]
    public void TheValuesCreationProvesItsOwnSlotForACallAndForAnOperator()
    {
        Assert.Empty(Analyze(RingOfM(), Load(Sized(m)), Add(Sized(m))));
        Refused(Analyze(RingOfM(), Add(Sized(n))), "'n' and 'u.Capacity' are not proven identical (fails when m <= n - 1)");
    }

    [Fact]
    public void AMethodADerivedValueInheritsReadsTheValuesOwnSlotThatItsClassForwardsToTheBase()
    {
        var kid = HandBuiltSlots.Generic(HandBuiltModel.Class("Kid<>"), "C", slots.ZeroOrPositive);
        ((ConcreteType)kid).BaseType = slots.Closed(ring, kid.TypeParametersList[0]);
        var derived = new LocalVariable("u", slots.Closed(kid, UnknownType.Instance));

        Refused(Analyze(OfM(derived, kid), Load(Sized(n), derived)), "'int[n]' is not proven to be 'int[u.C]': 'n' and 'u.C' are not proven identical");
        Assert.Empty(Analyze(OfM(derived, kid), Load(Sized(m), derived)));
    }

    [Fact]
    public void AnArrayAsLongAsTheValuesOwnReadIsTakenWhereNothingElseIsKnown()
    {
        var read = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(u), capacity.Name, slots.Int, member: new TypeResult(capacity));
        var length = new SlotCapture("u.Capacity", slots.ZeroOrPositive, slots.Int, read, 7);

        Assert.Empty(Analyze(Load(slots.Create(length))));
    }

    [Fact]
    public void AnArgumentThatReassignsTheReceiverMeetsTheSlotOfTheValueTheCallRunsOn()
    {
        Refused(Analyze(RingOfM(), Load(SizedByReassigning(n))), "'(u = new Ring()).Capacity' and 'u.Capacity' are not proven identical (fails when m <= n - 1)");
        Assert.Empty(Analyze(RingOfM(), Load(SizedByReassigning(m))));
    }

    [Fact]
    public void ACompoundAssignmentsRightSideThatReassignsTheTargetMeetsTheSlotOfTheValueItHeldBefore()
    {
        Refused(Analyze(RingOfM(), AddTo(SizedByReassigning(n))), "'(u = new Ring()).Capacity' and 'u.Capacity' are not proven identical (fails when m <= n - 1)");
        Assert.Empty(Analyze(RingOfM(), AddTo(SizedByReassigning(m))));
    }

    [Fact]
    public void AMemberReachedByOptionalChainingMeetsTheReceiversOwnSlot()
    {
        var optional = Optional();

        Refused(Analyze(HandBuiltSlots.Declare(optional, Ring(m)), Load(Sized(n), optional, conditional: true)), "'n' and 'u.Capacity' are not proven identical (fails when m <= n - 1)");
        Assert.Empty(Analyze(HandBuiltSlots.Declare(optional, Ring(m)), Load(Sized(m), optional, conditional: true)));
    }

    [Fact]
    public void AFieldWrittenByOptionalChainingMeetsTheReceiversOwnSlot()
    {
        var optional = Optional();

        Refused(Analyze(HandBuiltSlots.Declare(optional, Ring(m)), Store(optional, Sized(n))), "'n' and 'u.Capacity' are not proven identical (fails when m <= n - 1)");
        Assert.Empty(Analyze(HandBuiltSlots.Declare(optional, Ring(m)), Store(optional, Sized(m))));
    }

    [Fact]
    public void AnExtensionReachedThroughAValueMeetsThatValuesOwnSlotItsBlockReads()
    {
        Refused(Analyze(RingOfM(), Put(Sized(n))), "'n' and 'u.Capacity' are not proven identical (fails when m <= n - 1)");
        Assert.Empty(Analyze(RingOfM(), Put(Sized(m))));
    }

    [Fact]
    public void AMemberReachedByOptionalChainingThroughAKnownOwnersMemberTypedWithItsSlotMeetsTheOwnersSlot()
    {
        var made = slots.Capture(m, slots.Int);
        var (h, member) = Holding(made.Value);
        var declared = HandBuiltSlots.Declare(h, new NewExpression(HandBuiltModel.Node(), h.Type, [], null, [], []) { Captures = [made] });

        Refused(Analyze(declared, LoadThrough(h, member, n, known: true)), "'n' and 'h.R.Capacity' are not proven identical (fails when m <= n - 1)");
        Assert.Empty(Analyze(declared, LoadThrough(h, member, m, known: true)));
    }

    [Fact]
    public void AMemberReachedByOptionalChainingThroughAnUnknownOwnersMemberTypedWithItsSlotMeetsTheOwnersSlot()
    {
        var (h, member) = Holding(UnknownType.Instance);
        var owner = member.DeclaringType!;

        Refused(Analyze(OfM(h, owner), LoadThrough(h, member, n, known: false)), "'n' and 'h.R.Capacity' are not proven identical (fails when m <= n - 1)");
        Assert.Empty(Analyze(OfM(h, owner), LoadThrough(h, member, m, known: false)));
    }

    [Fact]
    public void AValueStoredInAnOptionalMemberTypedWithItsOwnersSlotThroughAnUnknownOwnerMeetsTheOwnersSlot()
    {
        var (h, member) = Holding(UnknownType.Instance);
        var owner = member.DeclaringType!;
        var kept = new Field("F", member.Type, OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = owner };

        Refused(Analyze(OfM(h, owner), Keep(h, kept, Ring(n))), "'n' and 'h.K' are not proven identical (fails when m <= n - 1)");
        Assert.Empty(Analyze(OfM(h, owner), Keep(h, kept, Ring(m))));
    }

    [Fact]
    public void AnOptionalStoredInAnOptionalMemberTypedWithItsOwnersSlotMeetsTheOwnersSlotThroughTheValueItHolds()
    {
        var (h, member) = Holding(UnknownType.Instance);
        var owner = member.DeclaringType!;
        var kept = new Field("F", member.Type, OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = owner };
        var x = new LocalVariable("x", Lifted(slots.Closed(ring, UnknownType.Instance)));

        Refused(Analyze(OfM(h, owner), HandBuiltSlots.Declare(x, Ring(n)), Keep(h, kept, HandBuiltRefinements.Read(x))), "'x.Capacity' and 'h.K' are not proven identical (fails when m <= n - 1)");
        Assert.Empty(Analyze(OfM(h, owner), HandBuiltSlots.Declare(x, Ring(m)), Keep(h, kept, HandBuiltRefinements.Read(x))));
    }

    [Fact]
    public void AbsenceStoredInAnOptionalMemberTypedWithItsOwnersSlotThroughAnUnknownOwnerHasNoSlotToMeet()
    {
        var (h, member) = Holding(UnknownType.Instance);
        var owner = member.DeclaringType!;
        var kept = new Field("F", member.Type, OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = owner };
        var contextual = Lifted(slots.Closed(ring, slots.ZeroOrPositive));

        Refused(Analyze(OfM(h, owner), Keep(h, kept, Ring(n))), "'n' and 'h.K' are not proven identical (fails when m <= n - 1)");
        Assert.Empty(Analyze(OfM(h, owner), Keep(h, kept, new VoidLiteralExpression(HandBuiltModel.Node(), VoidReading.Absence, contextual))));
        Assert.Empty(Analyze(OfM(h, owner), Keep(h, kept, new DefaultExpression(HandBuiltModel.Node(), contextual))));
    }

    [Fact]
    public void AnOptionalValueStoredThroughAnUnknownOwnerIsSpelledByTheValueItHolds()
    {
        var (h, member) = Holding(UnknownType.Instance);
        var owner = member.DeclaringType!;
        var kept = new Field("F", member.Type, OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = owner };
        var pick = new Method("Pick", Lifted(slots.Closed(ring, Unknown)), [], new MemberModifiers { IsStatic = true }) { DeclaringType = ring };
        var picked = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), pick), [], pick.ReturnType, pick, []);

        var refused = Assert.Single(Analyze(OfM(h, owner), Keep(h, kept, picked)));
        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'Ring<int>.Capacity' and 'h.K' are not proven identical (fails when m <= Ring<int>.Capacity - 1)", refused.Message, StringComparison.Ordinal);
    }
}
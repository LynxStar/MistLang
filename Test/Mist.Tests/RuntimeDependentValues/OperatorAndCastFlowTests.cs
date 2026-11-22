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
/// The rest of the places a member's signature is reached through a value, judged by the refinement analyzer
/// over hand-built bodies (runtime-dependent-values Requirements 1.7, 7.6 and 8.4): a user operator whose
/// owner an operand's value closes, a cast, which names the operand's object rather than a new one, a compound
/// assignment's operator and the member it writes through a value, and a field initializer naming its own dependent parameter.
/// </summary>
[Trait("Tier", "2")]
public sealed class OperatorAndCastFlowTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType ring;
    private readonly TypeParameter capacity;
    private readonly MemberType cell;
    private readonly MemberType list;
    private readonly LocalVariable n;
    private readonly LocalVariable m;

    public OperatorAndCastFlowTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        capacity = ring.TypeParametersList[0];
        cell = HandBuiltSlots.Generic(HandBuiltModel.Class("Cell<>"), "N", slots.Int);
        list = HandBuiltModel.Class("List<>");
        list.TypeParametersList.Add(new TypeParameter("T", 0, list.Scope, list));
        var range = slots.Refinements.Between(slots.Int, 11L, 29L, "Range");
        n = new LocalVariable("n", range);
        m = new LocalVariable("m", range);
    }

    private IMistType Void => slots.Refinements.Built.Type("Void");

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

    private (LocalVariable Q, LocalDeclarationStatement Declared, SlotCapture Written) RingOfM()
    {
        var written = slots.Capture(n, slots.Int);
        var made = slots.Capture(m, slots.Int);
        var q = new LocalVariable("q", slots.Closed(ring, written.Value));
        var creation = new NewExpression(HandBuiltModel.Node(), slots.Closed(ring, made.Value), [], null, [], []) { Captures = [made] };
        return (q, HandBuiltSlots.Declare(q, creation, written), written);
    }

    private NewExpression Made(MemberType definition, LocalVariable value)
    {
        var made = slots.Capture(value, slots.Int);
        return new NewExpression(HandBuiltModel.Node(), slots.Closed(definition, made.Value), [], null, [], []) { Captures = [made] };
    }

    // Ring<Capacity> operator +(Ring<Capacity> a, int[Capacity] b), closed with the left operand's type.
    private BinaryExpression Add(Expression left, IMistType slot, Expression right)
    {
        var plus = new Operator(OperatorKind.Plus, slots.Closed(ring, capacity), [new Parameter("a", slots.Closed(ring, capacity)), new Parameter("b", slots.ArrayOf(capacity))],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = ring };
        var closed = slots.Closed(ring, slot);
        return new BinaryExpression(HandBuiltModel.Node(), left, BinaryOperatorKind.Add, right, closed) { Operator = plus, ParameterTypes = [closed, slots.ArrayOf(slot)] };
    }

    private ExpressionStatement Load(Expression receiver, IMistType taken, LocalVariable length)
    {
        var method = new Method("Load", Void, [new Parameter("items", taken)], new MemberModifiers()) { DeclaringType = ring };
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Load", Void);
        return HandBuiltRefinements.Evaluate(new InvocationExpression(HandBuiltModel.Node(), callee, [slots.Create(slots.Capture(length))], Void, method, [taken]));
    }

    [Fact]
    public void AUserOperatorTakesAnOperandEqualToTheSlotOfTheOperandWhoseValueClosesIt()
    {
        var (q, declared, written) = RingOfM();

        ExpressionStatement add(LocalVariable length)
        {
            return HandBuiltRefinements.Evaluate(Add(HandBuiltRefinements.Read(q), written.Value, slots.Create(slots.Capture(length))));
        }

        Assert.Empty(Analyze(declared, add(m)));
        Refused(Analyze(declared, add(n)), "'n' and 'q.Capacity' are not proven identical (fails when n <= m - 1)");

        ExpressionStatement twice(LocalVariable length)
        {
            var first = Add(HandBuiltRefinements.Read(q), written.Value, slots.Create(slots.Capture(m)));
            return HandBuiltRefinements.Evaluate(Add(first, written.Value, slots.Create(slots.Capture(length))));
        }

        Assert.Empty(Analyze(declared, twice(m)));
        Refused(Analyze(declared, twice(n)), "'n' and 'Ring<n>.Capacity' are not proven identical (fails when n <= m - 1)");
    }

    [Fact]
    public void ACastNamesItsOperandsObjectSoAMemberReachedThroughItReadsThatObjectsSlot()
    {
        var (q, declared, _) = RingOfM();

        ExpressionStatement load(LocalVariable length)
        {
            var named = slots.Capture(n, slots.Int);
            var cast = new CastExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(q), slots.Closed(ring, named.Value), [], slots.Closed(ring, named.Value)) { Captures = [named] };
            return Load(cast, slots.ArrayOf(named.Value), length);
        }

        Assert.Empty(Analyze(declared, load(m)));
        Refused(Analyze(declared, load(n)), "'n' and 'Ring<n>.Capacity' are not proven identical (fails when n <= m - 1)");
    }

    [Fact]
    public void ACompoundAssignmentMeetsItsOperatorWithTheTargetsOwnSlotAndAMemberWithTheReceiversOwn()
    {
        var parameter = cell.TypeParametersList[0];
        var plus = new Operator(OperatorKind.Plus, slots.Closed(cell, parameter), [new Parameter("a", slots.Closed(cell, parameter)), new Parameter("b", slots.Closed(cell, parameter))],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = cell };

        ExpressionStatement grow(Expression target, LocalVariable length)
        {
            return HandBuiltRefinements.Evaluate(new CompoundAssignmentExpression(HandBuiltModel.Node(), target, BinaryOperatorKind.Add, Made(cell, length), target.Type, plus, IncrementForm.None)
            {
                ParameterTypes = [target.Type, target.Type],
            });
        }

        var named = slots.Capture(n, slots.Int);
        var c = new LocalVariable("c", slots.Closed(cell, named.Value));
        var declared = HandBuiltSlots.Declare(c, Made(cell, m), named);

        Assert.Empty(Analyze(declared, grow(HandBuiltRefinements.Read(c), m)));
        Refused(Analyze(declared, grow(HandBuiltRefinements.Read(c), n)), "'n' and 'c.N' are not proven identical (fails when n <= m - 1)");

        var total = new Property("Total", slots.Closed(cell, capacity), new MemberModifiers()) { DeclaringType = ring };
        var written = slots.Capture(n, slots.Int);
        var rings = new LocalVariable("list", slots.Closed(list, slots.Closed(ring, written.Value)));
        var element = new IndexExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(rings), slots.Refinements.Literal(0), slots.Closed(ring, written.Value));
        var member = new MemberAccessExpression(HandBuiltModel.Node(), element, "Total", slots.Closed(cell, written.Value), member: new PropertyResult(total, ring, []));

        Refused(Analyze(grow(member, n)), "'n' and 'Ring<n>.Total.N' are not proven identical");

        var loose = slots.Closed(cell, new UnknownValueType(slots.Int));
        var taking = new Operator(OperatorKind.Plus, slots.Closed(cell, parameter), [new Parameter("a", loose), new Parameter("b", slots.Closed(cell, parameter))],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = cell };
        var added = Made(cell, m);
        var replaced = new CompoundAssignmentExpression(HandBuiltModel.Node(), member, BinaryOperatorKind.Add, added, added.Type, taking, IncrementForm.None)
        {
            ParameterTypes = [loose, added.Type],
        };

        Refused(Analyze(HandBuiltRefinements.Evaluate(replaced)), "and 'Ring<n>.Capacity' are not proven identical");
    }

    [Fact]
    public void AFieldInitializerNamingItsOwnDependentParameterCreatesExactlyThatValue()
    {
        var own = new SlotCapture("Capacity", slots.Int, slots.Int, new TypeReferenceExpression(HandBuiltModel.Node(), capacity), 97);
        var made = slots.Create(own);
        var field = new Field("Slots", slots.ArrayOf(capacity), OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = ring, Initializer = made };

        Assert.Empty(slots.Refinements.Built.Analyze(new RefinementAnalyzer(), made, owner: ring, member: field));

        var five = new ArrayCreationExpression(HandBuiltModel.Node(), slots.Int, slots.Refinements.Literal(5L), slots.ArrayOf(slots.Literal(5)));
        var wrong = new Field("Slots", slots.ArrayOf(capacity), OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = ring, Initializer = five };

        Refused(slots.Refinements.Built.Analyze(new RefinementAnalyzer(), five, owner: ring, member: wrong), "'5' and 'Capacity' are not proven identical");
    }
}
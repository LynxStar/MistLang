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

    private ExpressionStatement Load(Expression length, LocalVariable? through = null)
    {
        var method = new Method("Load", Void, [new Parameter("items", slots.ArrayOf(capacity))], new MemberModifiers()) { DeclaringType = ring };
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(through ?? u), "Load", Void);
        return HandBuiltRefinements.Evaluate(new InvocationExpression(HandBuiltModel.Node(), callee, [length], Void, method, [slots.ArrayOf(Unknown)]));
    }

    private ExpressionStatement Add(Expression length)
    {
        var plus = new Operator(OperatorKind.Plus, slots.Closed(ring, capacity), [new Parameter("a", slots.Closed(ring, capacity)), new Parameter("b", slots.ArrayOf(capacity))],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = ring };
        var closed = slots.Closed(ring, Unknown);
        return HandBuiltRefinements.Evaluate(new BinaryExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(u), BinaryOperatorKind.Add, length, closed)
        {
            Operator = plus,
            ParameterTypes = [closed, slots.ArrayOf(Unknown)],
        });
    }

    private ArrayCreationExpression Sized(LocalVariable length)
    {
        return slots.Create(slots.Capture(length));
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
}
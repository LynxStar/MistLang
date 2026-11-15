using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Which type parameters <see cref="ClosingWitnesses"/> gives a witness, over hand-built sensitivity and closings
/// (runtime-dependent-values design, <em>Identity by value: a closing as a type argument</em>): a marked parameter a
/// closing of a value-routed type fills, one only plain types fill, a chain the closing reaches from its outermost
/// writer, an abstract's method slot and its answer together, and a reason no witness answers.
/// </summary>
[Trait("Tier", "1")]
public sealed class ClosingWitnessDecisionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly ClassType tester;
    private readonly TypeParameter t;

    public ClosingWitnessDecisionTests()
    {
        ring = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        capacity = ring.TypeParametersList[0];
        tester = HandBuiltModel.Class("Tester<>");
        t = Parameter(tester, "T");
    }

    [Fact]
    public void ATestedParameterAClosingOfAValueRoutedTypeFillsTakesAWitness()
    {
        using var routes = SlotRoutes.Enter([capacity]);

        var found = Decide([TestOf(t)], [(tester, [slots.Closed(ring, slots.Literal(5))])]);

        Assert.Equal([t], found);
    }

    [Fact]
    public void ATestedParameterOnlyPlainTypesFillTakesNone()
    {
        using var routes = SlotRoutes.Enter([capacity]);

        Assert.Empty(Decide([TestOf(t)], [(tester, [slots.Int]), (tester, [slots.Closed(HandBuiltModel.Class("Plain"))])]));
    }

    [Fact]
    public void AClosingOfACarrierRoutedTypeTakesNone()
    {
        using var routes = SlotRoutes.Enter([]);

        Assert.Empty(Decide([TestOf(t)], [(tester, [slots.Closed(ring, slots.Literal(5))])]));
    }

    [Fact]
    public void AChainTakesAWitnessAtEveryStepFromTheOutermostWriterInward()
    {
        var outer = HandBuiltModel.Class("Outer<>");
        var o = Parameter(outer, "O");
        var inner = HandBuiltModel.Class("Inner<>");
        var i = Parameter(inner, "I");
        using var routes = SlotRoutes.Enter([capacity]);

        var found = Decide([TestOf(t)], [(tester, [i]), (inner, [o]), (outer, [slots.Closed(ring, slots.Literal(5))])]);

        Assert.Equal(3, found.Count);
        Assert.Contains(o, found);
        Assert.Contains(i, found);
        Assert.Contains(t, found);
    }

    [Fact]
    public void AnAbstractsMethodSlotAndTheMethodAnsweringItTakeAWitnessTogether()
    {
        var (slot, s) = Generic(new AbstractType("IChecker", "Probe", new Scope(null), null, null), "S");
        var (answer, a) = Generic(HandBuiltModel.Class("Checker"), "A");
        using var routes = SlotRoutes.Enter([capacity]);
        var sensitivity = SlotSensitivity.Discover([TestOf(a)], [], [], [(slot, answer)]);

        var found = ClosingWitnesses.Decide(sensitivity, [(slot, [slots.Closed(ring, slots.Literal(5))])], [(slot, answer)]);

        Assert.Contains(s, found);
        Assert.Contains(a, found);
    }

    [Fact]
    public void AParameterMarkedOnlyForATypeValueTakesNone()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var literal = new TypeLiteralExpression(HandBuiltModel.Node(), t, slots.Refinements.Built.Type("Type"));

        Assert.Empty(Decide([literal], [(tester, [slots.Closed(ring, slots.Literal(5))])]));
    }

    [Fact]
    public void AWitnessIsOnlyAsWideAsTheDecision()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t]);

        Assert.True(ClosingWitnesses.IsWitnessed(t));
        Assert.Equal([t], ClosingWitnesses.Stored(tester));
        Assert.Equal([t], SlotRoutes.Carried(tester));
        Assert.Equal("__closing_T", SlotRoutes.Hidden(t));
        Assert.Equal("MistClosing<T>", SlotRoutes.Keyword(t));
        Assert.Empty(ClosingWitnesses.Stored(HandBuiltModel.Class("Plain<>")));
    }

    private static HashSet<TypeParameter> Decide(IEnumerable<object> roots, IEnumerable<(IHasTypeParameters Definition, IReadOnlyList<IMistType> Arguments)> fills)
    {
        var closings = fills.ToList();
        return ClosingWitnesses.Decide(SlotSensitivity.Discover(roots, [], closings), closings, []);
    }

    private IsPatternExpression TestOf(IMistType type)
    {
        return new IsPatternExpression(
            HandBuiltModel.Node(), HandBuiltRefinements.Local(UnknownType.Instance, "x"), new TypeTestPattern(HandBuiltModel.Node(), type, binding: null), slots.Refinements.Bool);
    }

    private (Method Method, TypeParameter Parameter) Generic(MemberType host, string parameter)
    {
        var declared = new TypeParameter(parameter, 0, new Scope(null), null);
        var method = new Method("Is", slots.Refinements.Bool, [new Parameter("x", UnknownType.Instance)], new MemberModifiers())
        {
            DeclaringType = host,
            Scope = host.Scope,
        };
        method.TypeParametersList.Add(declared);
        declared.DeclaringMethod = method;
        host.MethodsList.Add(method);
        return (method, declared);
    }

    private static TypeParameter Parameter(MemberType owner, string name)
    {
        var parameter = new TypeParameter(name, owner.TypeParametersList.Count, owner.Scope, owner);
        owner.TypeParametersList.Add(parameter);
        return parameter;
    }
}
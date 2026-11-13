using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Which type parameters <see cref="SlotSensitivity"/> marks, one reason at a time over hand-built bodies and
/// closings (runtime-dependent-values design, <em>A closing as a type argument</em>): each reason, each form
/// of the test exemption, forwarding through two declarations, through a base or an implemented abstract and
/// from an abstract's method slot to the method answering it, and a declaration that only holds, passes and
/// calls through its parameter.
/// </summary>
[Trait("Tier", "1")]
public sealed class SlotSensitivityDecisionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType tester;
    private readonly TypeParameter t;
    private readonly IMistType text;

    public SlotSensitivityDecisionTests()
    {
        tester = HandBuiltModel.Class("Tester<>");
        t = Parameter(tester, "T");
        text = slots.Refinements.Built.Type("String");
    }

    [Fact]
    public void ATestOfAValueAgainstTheParameterMarksIt()
    {
        var found = Discover(Is(Local(UnknownType.Instance), Test(t)));

        Assert.Equal(SlotSensitivity.Kind.Test, Assert.Single(found.ReasonsOf(t)).Kind);
        Assert.Equal([t], found.Sensitive);
    }

    [Fact]
    public void ATestOfAValueOfTheParametersOwnTypeIsExempt()
    {
        Assert.Empty(Discover(Is(Local(t), Test(t))).Sensitive);
    }

    [Theory]
    [InlineData("String")]
    [InlineData("Void")]
    public void ATestOfAUnionHoldingTheParameterIsExempt(string other)
    {
        var union = Union(t, slots.Refinements.Built.Type(other));

        Assert.Empty(Discover(Is(Local(union), Test(t))).Sensitive);
    }

    [Fact]
    public void ATestAgainstATypeThatMentionsTheParameterMarksItWhenTheValueAdmitsMoreThanThatClosing()
    {
        var holder = HandBuiltModel.Class("Holder<>");
        _ = Parameter(holder, "U");

        var found = Discover(Is(Local(Union(t, text)), Test(slots.Closed(holder, t))));

        Assert.Equal([t], found.Sensitive);
    }

    [Fact]
    public void ASwitchArmAgainstTheParameterMarksItUnlessTheScrutineeAdmitsOnlyIt()
    {
        Assert.Equal([t], Discover(Switch(Local(UnknownType.Instance), Test(t))).Sensitive);
        Assert.Empty(Discover(Switch(Local(t), Test(t))).Sensitive);
    }

    [Fact]
    public void AnAsAgainstTheParameterMarksItUnlessTheOperandAdmitsOnlyIt()
    {
        Assert.Equal([t], Discover(As(Local(UnknownType.Instance), t)).Sensitive);
        Assert.Empty(Discover(As(Local(Union(t, text)), t)).Sensitive);
    }

    [Fact]
    public void StorageOfTheParameterFromTheClrZeroMarksIt()
    {
        var sized = slots.Closed(slots.Array, t, slots.Literal(2));
        object[] roots =
        [
            new ArrayCreationExpression(HandBuiltModel.Node(), t, slots.Refinements.Literal(2L), sized),
            new StackallocExpression(HandBuiltModel.Node(), t, slots.Refinements.Literal(2L), sized),
            new DefaultExpression(HandBuiltModel.Node(), t),
        ];

        foreach(var root in roots)
        {
            var found = Discover(root);

            Assert.Equal(SlotSensitivity.Kind.Storage, Assert.Single(found.ReasonsOf(t)).Kind);
            Assert.Equal([t], found.Sensitive);
        }
    }

    [Fact]
    public void StaticStorageMarksEveryParameterOfTheDeclaringTypeAndItsEnclosingTypes()
    {
        var counter = HandBuiltModel.Class("Counter<>");
        var k = Parameter(counter, "K");
        counter.FieldsList.Add(new Field("hits", slots.Int, default, new MemberModifiers { IsStatic = true }) { DeclaringType = counter });
        var outer = HandBuiltModel.Class("Outer<>");
        var o = Parameter(outer, "O");
        var inner = new ClassType("Inner", "Probe", outer.Scope, outer, null);
        inner.PropertiesList.Add(new Property("Count", slots.Int, new MemberModifiers { IsStatic = true }) { DeclaringType = inner, UsesBackingField = true });
        var plain = HandBuiltModel.Class("Plain<>");
        var p = Parameter(plain, "P");
        plain.FieldsList.Add(new Field("count", slots.Int, default, new MemberModifiers()) { DeclaringType = plain });

        var found = SlotSensitivity.Discover([], [counter, outer, inner, plain], []);

        Assert.Equal(SlotSensitivity.Kind.Static, Assert.Single(found.ReasonsOf(k)).Kind);
        Assert.Equal(SlotSensitivity.Kind.Static, Assert.Single(found.ReasonsOf(o)).Kind);
        Assert.False(found.IsSensitive(p));
    }

    [Fact]
    public void ATypeValueOfTheParameterMarksIt()
    {
        var found = Discover(new TypeLiteralExpression(HandBuiltModel.Node(), t, slots.Refinements.Built.Type("Type")));

        Assert.Equal(SlotSensitivity.Kind.TypeValue, Assert.Single(found.ReasonsOf(t)).Kind);
    }

    [Fact]
    public void AClosingPassedToASensitiveParameterForwardsThroughTwoDeclarationsAndTheReasonReadsAsTheChain()
    {
        var holder = HandBuiltModel.Class("Holder<>");
        var u = Parameter(holder, "U");
        var outer = HandBuiltModel.Class("Outer<>");
        var v = Parameter(outer, "V");
        var ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);

        var found = SlotSensitivity.Discover([Is(Local(UnknownType.Instance), Test(t))], [], [(tester, [u]), (holder, [v])]);

        var fromHolder = Assert.Single(found.ReasonsOf(u));
        var fromOuter = Assert.Single(found.ReasonsOf(v));
        Assert.Equal(SlotSensitivity.Kind.Forwarded, fromHolder.Kind);
        Assert.Same(t, fromHolder.Target);
        Assert.Equal(SlotSensitivity.Kind.Forwarded, fromOuter.Kind);
        Assert.Same(u, fromOuter.Target);
        Assert.Equal(3, found.Sensitive.Count);

        using var routes = SlotRoutes.Enter(ring.TypeParametersList);
        var (reason, closing) = Assert.NotNull(found.Judge(v, slots.Closed(ring, slots.Literal(5))));

        Assert.Equal(
            "passes a type that mentions 'V' to 'U' of 'Holder', which passes a type that mentions 'U' to 'T' of 'Tester', "
            + "which tests a value against a type that mentions 'T', which the shared body cannot tell from another closing's",
            reason);
        Assert.Same(ring, closing.Definition);
        Assert.Null(found.Judge(v, slots.Closed(holder, slots.Int)));
    }

    [Fact]
    public void ABaseAndAnImplementedAbstractForwardTheirParametersThroughTheInternedClosing()
    {
        var derived = HandBuiltModel.Class("Derived<>");
        var u = Parameter(derived, "U");
        derived.BaseType = slots.Closed(tester, u);
        var conforming = HandBuiltModel.Class("Conforming<>");
        var w = Parameter(conforming, "W");
        conforming.BaseAbstractTypes.Add(slots.Closed(tester, w));
        var interned = slots.Refinements.Built.Interner.ConstructedTypes
            .Where(constructed => constructed.Definition is IHasTypeParameters)
            .Select(constructed => ((IHasTypeParameters)constructed.Definition, constructed.TypeArguments));

        var found = SlotSensitivity.Discover([Is(Local(UnknownType.Instance), Test(t))], [derived, conforming], interned);

        Assert.Same(t, Assert.Single(found.ReasonsOf(u)).Target);
        Assert.Same(t, Assert.Single(found.ReasonsOf(w)).Target);
    }

    [Fact]
    public void AnAbstractsMethodSlotTakesTheSensitivityOfTheMethodAnsweringItAndTheReasonNamesTheAnswer()
    {
        var (slot, s) = Generic(new AbstractType("IChecker", "Probe", new Scope(null), null, null), "S");
        var (answer, a) = Generic(HandBuiltModel.Class("Checker"), "A");
        var ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        var body = Is(Local(UnknownType.Instance), Test(a));

        var linked = SlotSensitivity.Discover([body], [], [], [(slot, answer)]);
        var unlinked = SlotSensitivity.Discover([body], [], []);

        var reason = Assert.Single(linked.ReasonsOf(s));
        Assert.Equal(SlotSensitivity.Kind.Answered, reason.Kind);
        Assert.Same(a, reason.Target);
        Assert.Equal([a], unlinked.Sensitive);

        using var routes = SlotRoutes.Enter(ring.TypeParametersList);
        var (text, closing) = Assert.NotNull(linked.Judge(s, slots.Closed(ring, slots.Literal(5))));

        Assert.Equal(
            "is answered by 'Is' of 'Checker', which tests a value against a type that mentions 'A', which the shared body cannot tell from another closing's",
            text);
        Assert.Same(ring, closing.Definition);
    }

    [Fact]
    public void ADeclarationThatHoldsPassesReturnsAndCallsThroughItsParameterIsNotMarked()
    {
        var holder = HandBuiltModel.Class("Holder<>");
        var u = Parameter(holder, "U");
        var list = HandBuiltModel.Class("List<>");
        _ = Parameter(list, "E");
        var body = HandBuiltModel.Block(
            HandBuiltRefinements.Evaluate(new NewExpression(HandBuiltModel.Node(), slots.Closed(list, u), [], null, [], null)),
            HandBuiltRefinements.Evaluate(new InvocationExpression(
                HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), Local(u), "Twice", slots.Int), [], slots.Int, null, null)),
            new LocalDeclarationStatement(new LocalVariable("copy", u), Local(u)),
            HandBuiltRefinements.Evaluate(slots.Refinements.Binary(Local(u), BinaryOperatorKind.Equal, Local(u))));

        var found = SlotSensitivity.Discover([body], [holder, list], [(list, [u])]);

        Assert.Empty(found.Sensitive);
    }

    private static SlotSensitivity Discover(object root)
    {
        return SlotSensitivity.Discover([root], [], []);
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

    private IsPatternExpression Is(Expression scrutinee, Pattern pattern)
    {
        return new IsPatternExpression(HandBuiltModel.Node(), scrutinee, pattern, slots.Refinements.Bool);
    }

    private SwitchExpression Switch(Expression scrutinee, Pattern pattern)
    {
        return new SwitchExpression(
            HandBuiltModel.Node(), scrutinee, [new SwitchArm(HandBuiltModel.Node(), pattern, null, slots.Refinements.Literal(1L))], slots.Int);
    }

    private static AsExpression As(Expression operand, IMistType target)
    {
        return new AsExpression(HandBuiltModel.Node(), operand, target, AsConversionKind.Narrowing, null, target);
    }

    private static TypeTestPattern Test(IMistType type)
    {
        return new TypeTestPattern(HandBuiltModel.Node(), type, binding: null);
    }

    private static LocalReferenceExpression Local(IMistType type)
    {
        return HandBuiltRefinements.Local(type, "x");
    }

    private IMistType Union(params IMistType[] constituents)
    {
        return slots.Refinements.Built.Interner.GetOrCreateUnionType(constituents);
    }
}
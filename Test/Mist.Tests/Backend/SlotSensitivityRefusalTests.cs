using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
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
/// The refusal of a closing of a value-routed type as a slot-sensitive type argument over a hand-built model
/// (runtime-dependent-values design, <em>Identity by value: a closing as a type argument</em>): a closing a witness
/// answers for is admitted whether stated or carried by a call, and the three reasons no witness answers are refused
/// at the closing, a type value, a test that nests the parameter or the argument's closing in a CLR container, and a
/// CLR generic a struct closing reaches directly or through a declaration; a call through an abstract's method slot,
/// a declaration that only holds and passes its parameter, and the test exemption written without a slot comparison
/// in a copy union specialization makes.
/// </summary>
[Trait("Tier", "2")]
public sealed class SlotSensitivityRefusalTests
{
    private const string Routed =
        "whose dependent parameter 'Capacity' is closed somewhere with a value known only when the program runs, so every closing of";

    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly StructType cell;
    private readonly TypeParameter n;
    private readonly IMistType text;

    public SlotSensitivityRefusalTests()
    {
        ring = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        ring.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        TypeParameterDomainResolver.ProjectValues(ring);
        capacity = ring.TypeParametersList[0];
        cell = (StructType)HandBuiltSlots.Generic(HandBuiltModel.Struct("Cell<>"), "N", slots.Int);
        TypeParameterDomainResolver.ProjectValues(cell);
        n = cell.TypeParametersList[0];
        text = slots.Refinements.Built.Type("String");
    }

    [Fact]
    public void AStatedClosingOfADeclarationAWitnessAnswersForIsAdmitted()
    {
        var (tester, _) = Tester();
        var (holder, _) = Holder(tester);
        var (counter, _) = Counter();
        var (blank, _) = Blank();
        var access = new MemberAccessExpression(
            HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), slots.Closed(counter, slots.Closed(ring, slots.Literal(3)))), "hits", slots.Int);

        Assert.Empty(Refuse(
            [Body(tester), New(slots.Closed(tester, slots.Closed(ring, slots.Literal(5)))), New(slots.Closed(holder, slots.Closed(ring, slots.Literal(5)))), access,
                blank.Body!, Call(blank, [slots.Closed(cell, slots.Literal(7))])],
            [tester, holder, counter]));
    }

    [Fact]
    public void ATypeValueDeclarationRefusesAStatedClosing()
    {
        var (same, _) = Same();

        var refused = Assert.Single(Refuse([same.Body!, Call(same, [slots.Closed(ring, slots.Literal(3))])], []));

        Assert.Equal("MIST0502", refused.Number.Value);
        Assert.Equal(
            $"'Ring<3>' cannot fill 'A' of 'Same': it mentions 'Ring<3>', {Routed} 'Ring<3>' is one C# type, and 'Same' reads a type that mentions 'A' "
            + "as a type value, and a CLR type cannot carry the slot's value.",
            refused.Message);
    }

    [Fact]
    public void AContainerTestRefusesAStatedClosingAndATestOfTheParameterRefusesAnArgumentNestingOneInAContainer()
    {
        var list = List();
        var (inList, _) = Tester(t => slots.Closed(list, t), "InList<>");
        var (tester, _) = Tester();

        var contained = Assert.Single(Refuse([Body(inList), New(slots.Closed(inList, slots.Closed(ring, slots.Literal(5))))], [inList]));
        var nested = Assert.Single(Refuse([Body(tester), New(slots.Closed(tester, slots.Closed(list, slots.Closed(ring, slots.Literal(5)))))], [tester]));

        Assert.Equal(
            $"'Ring<5>' cannot fill 'T' of 'InList': it mentions 'Ring<5>', {Routed} 'Ring<5>' is one C# type, and 'InList' tests a value against a type "
            + "that nests 'T' in a CLR container, which has nowhere to keep the slot's value.",
            contained.Message);
        Assert.Equal(
            $"'List<Ring<5>>' cannot fill 'T' of 'Tester': it mentions 'Ring<5>', {Routed} 'Ring<5>' is one C# type, and 'Tester' tests a value against 'T', "
            + "and the argument nests 'Ring<5>' in a CLR container, which has nowhere to keep the slot's value.",
            nested.Message);
    }

    [Fact]
    public void AClrGenericRefusesAStructClosingDirectlyAndThroughADeclarationAndAdmitsAClassClosing()
    {
        var list = List();
        var keeper = HandBuiltModel.Class("Keeper<>");
        var u = new TypeParameter("U", 0, keeper.Scope, keeper);
        keeper.TypeParametersList.Add(u);
        keeper.FieldsList.Add(new Field("items", slots.Closed(list, u), default, new MemberModifiers()) { DeclaringType = keeper });

        var direct = Assert.Single(Refuse([New(slots.Closed(list, slots.Closed(cell, slots.Literal(7)))), New(slots.Closed(list, slots.Closed(ring, slots.Literal(7))))], []));
        var through = Assert.Single(Refuse([New(slots.Closed(keeper, slots.Closed(cell, slots.Literal(7)))), New(slots.Closed(keeper, slots.Closed(ring, slots.Literal(7))))], [keeper]));

        Assert.Equal(
            "'Cell<7>' cannot fill 'E' of 'List': it mentions 'Cell<7>', whose dependent parameter 'N' is closed somewhere with a value known only when the "
            + "program runs, so every closing of 'Cell<7>' is one C# type, and 'List' is a CLR generic, whose body no description of the closing reaches and "
            + "which may hand back a zeroed value for 'E'.",
            direct.Message);
        Assert.EndsWith(
            "'Keeper' passes a type that mentions 'U' to a CLR generic, whose body no description of the closing reaches and which may hand back a zeroed value.",
            through.Message);
    }

    [Theory]
    [InlineData("test", null)]
    [InlineData("storage", null)]
    [InlineData("static", null)]
    [InlineData("forwarded", null)]
    [InlineData("type value", "reads a type that mentions 'P' as a type value, and a CLR type cannot carry the slot's value")]
    [InlineData("contained", "tests a value against a type that nests 'P' in a CLR container, which has nowhere to keep the slot's value")]
    public void AClosingACallCarriesIsRefusedAtTheCallOnlyForAReasonNoWitnessAnswers(string kind, string? reason)
    {
        var (tester, _) = Tester();
        var (counter, _) = Counter();
        var list = List();
        var p = new TypeParameter("P", 0, new Scope(null), null);
        var argument = kind == "storage" ? slots.Closed(cell, slots.Literal(7)) : slots.Closed(ring, slots.Literal(5));
        var owner = HandBuiltModel.Class("Api");
        var probe = new Method("Probe", slots.Int, [new Parameter("value", p), new Parameter("other", UnknownType.Instance)], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = owner,
            Scope = owner.Scope,
            Body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(kind switch
            {
                "test" => Is(HandBuiltRefinements.Local(UnknownType.Instance, "other"), Test(p)),
                "contained" => Is(HandBuiltRefinements.Local(UnknownType.Instance, "other"), Test(slots.Closed(list, p))),
                "storage" => new ArrayCreationExpression(HandBuiltModel.Node(), p, slots.Refinements.Literal(2L), slots.Closed(slots.Array, p, slots.Literal(2))),
                "static" => new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), slots.Closed(counter, p)), "hits", slots.Int),
                "type value" => new TypeLiteralExpression(HandBuiltModel.Node(), p, slots.Refinements.Built.Type("Type")),
                _ => New(slots.Closed(tester, p)),
            })),
        };
        probe.TypeParametersList.Add(p);
        p.DeclaringMethod = probe;
        owner.MethodsList.Add(probe);
        var call = new InvocationExpression(
            HandBuiltModel.Node(9), new MethodReferenceExpression(HandBuiltModel.Node(), probe, [argument]),
            [HandBuiltRefinements.Local(argument, "value"), HandBuiltRefinements.Local(UnknownType.Instance, "other")], slots.Int, probe, [argument, UnknownType.Instance]);
        slots.Refinements.Built.Model.GenericCalls.Record(probe, [argument]);

        var refused = Refuse([Body(tester), probe.Body!, call], [tester, counter]);

        if(reason is null)
        {
            Assert.Empty(refused);
            return;
        }

        var only = Assert.Single(refused);
        Assert.Equal(
            $"'Ring<5>' cannot fill 'P' of 'Probe': it mentions 'Ring<5>', {Routed} 'Ring<5>' is one C# type, and 'Probe' {reason}.",
            only.Message);
        Assert.Equal(10, only.Position().Start.Column);
    }

    [Fact]
    public void ACallThroughAnAbstractsMethodSlotIsRefusedOnlyWhenTheMethodAnsweringItReadsItsParameterAsATypeValue()
    {
        var checker = new AbstractType("IChecker", "Probe", new Scope(null), null, null);
        var s = new TypeParameter("S", 0, new Scope(null), null);
        var slot = new Method("Is", slots.Refinements.Bool, [new Parameter("x", UnknownType.Instance)], new MemberModifiers()) { DeclaringType = checker, Scope = checker.Scope };
        slot.TypeParametersList.Add(s);
        s.DeclaringMethod = slot;
        checker.MethodsList.Add(slot);
        var answering = HandBuiltModel.Class("Checker");
        var a = new TypeParameter("A", 0, new Scope(null), null);
        var tests = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(Is(HandBuiltRefinements.Local(UnknownType.Instance, "x"), Test(a))));
        var reads = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(new TypeLiteralExpression(HandBuiltModel.Node(), a, slots.Refinements.Built.Type("Type"))));
        var answer = new Method("Is", slots.Refinements.Bool, [new Parameter("x", UnknownType.Instance)], new MemberModifiers())
        {
            DeclaringType = answering,
            Scope = answering.Scope,
            Body = reads,
        };
        answer.TypeParametersList.Add(a);
        a.DeclaringMethod = answer;
        answering.MethodsList.Add(answer);
        var argument = slots.Closed(ring, slots.Literal(5));
        slots.Refinements.Built.Model.GenericCalls.Record(slot, [argument]);

        var refused = Assert.Single(Refuse([reads, Call(slot, [argument])], [], [(slot, answer)]));

        Assert.Equal(
            $"'Ring<5>' cannot fill 'S' of 'Is': it mentions 'Ring<5>', {Routed} 'Ring<5>' is one C# type, and 'Is' is answered by 'Is' of 'Checker', "
            + "which reads a type that mentions 'A' as a type value, and a CLR type cannot carry the slot's value.",
            refused.Message);
        Assert.Empty(Refuse([tests, Call(slot, [argument])], [], [(slot, answer)]));
    }

    [Fact]
    public void ADeclarationThatOnlyHoldsAndPassesItsParameterAdmitsTheClosing()
    {
        var list = HandBuiltModel.Class("List<>");
        var e = new TypeParameter("E", 0, list.Scope, list);
        list.TypeParametersList.Add(e);
        var owner = HandBuiltModel.Class("Api");
        var f = new TypeParameter("F", 0, new Scope(null), null);
        var first = new Method("First", f, [new Parameter("items", slots.Closed(list, f))], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = owner,
            Scope = owner.Scope,
            Body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Local(slots.Closed(list, f), "items"))),
        };
        first.TypeParametersList.Add(f);
        f.DeclaringMethod = first;
        var captured = slots.Closed(ring, slots.Capture(new LocalVariable("n", slots.Int)).Value);
        var call = new InvocationExpression(
            HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), first, [captured]),
            [HandBuiltRefinements.Local(slots.Closed(list, captured), "list")], captured, first, [slots.Closed(list, captured)]);
        slots.Refinements.Built.Model.GenericCalls.Record(first, [captured]);

        Assert.Empty(Refuse([first.Body!, New(slots.Closed(list, captured)), call], [list]));
    }

    [Fact]
    public void ATestOfAUnionHoldingTheParameterEmitsNoSlotComparisonInASpecializedCopy()
    {
        var box = HandBuiltModel.Class("Box<>");
        box.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var t = new TypeParameter("T", 0, box.Scope, box);
        box.TypeParametersList.Add(t);
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        var optional = Union(t, slots.Refinements.Built.Type("Void"));
        var either = Union(t, text);
        using var routes = SlotRoutes.Enter([capacity]);
        using var specializing = TypeEmitter.Specializing(slots.Closed(box, slots.Closed(ring, capture.Value)));

        var throughOptional = Emit(Is(HandBuiltRefinements.Local(optional, "x"), Test(t)));
        var throughEither = Emit(Is(HandBuiltRefinements.Local(either, "x"), new TypeTestPattern(HandBuiltModel.Node(), t, binding: null, constituent: t)));
        var throughUnknown = Emit(Is(HandBuiltRefinements.Local(UnknownType.Instance, "x"), Test(t)));

        Assert.Equal("x is Probe.Ring", throughOptional);
        Assert.DoesNotContain("__Capacity", throughEither, StringComparison.Ordinal);
        Assert.Contains("__Capacity == n", throughUnknown, StringComparison.Ordinal);
    }

    private IReadOnlyList<Diagnostic> Refuse(IEnumerable<object> roots, IEnumerable<MemberType> types, IEnumerable<(Method Slot, Method Answer)>? answers = null)
    {
        var bodies = roots.ToList();
        var declared = types.ToList();
        var model = slots.Refinements.Built.Model;
        using var routes = SlotRoutes.Enter([capacity, n]);
        var closings = model.TypeInterner.ConstructedTypes
            .Where(constructed => constructed.Definition is IHasTypeParameters)
            .Select(constructed => ((IHasTypeParameters)constructed.Definition, constructed.TypeArguments))
            .Concat(model.GenericCalls.All().Select(call => ((IHasTypeParameters)call.Method, call.TypeArguments)));
        var sensitivity = SlotSensitivity.Discover(bodies, declared, closings, answers);
        var diagnostics = new DiagnosticBag();

        sensitivity.Report(bodies.Select(root => new SourceRoot(root, null, MistAssembly.Synthetic, null)), declared, [], diagnostics);

        return diagnostics.ToImmutableArray();
    }

    private (ClassType Tester, TypeParameter T) Tester(Func<TypeParameter, IMistType>? tested = null, string name = "Tester<>")
    {
        var tester = HandBuiltModel.Class(name);
        tester.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var t = new TypeParameter("T", 0, tester.Scope, tester);
        tester.TypeParametersList.Add(t);
        tester.MethodsList.Add(new Method("Is", slots.Refinements.Bool, [new Parameter("x", UnknownType.Instance)], new MemberModifiers())
        {
            DeclaringType = tester,
            Scope = tester.Scope,
            Body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(Is(HandBuiltRefinements.Local(UnknownType.Instance, "x"), Test(tested?.Invoke(t) ?? t)))),
        });
        return (tester, t);
    }

    private static ClassType List()
    {
        var list = HandBuiltModel.Class("List<>");
        list.Modifiers = new MemberModifiers { IsIntrinsic = true };
        list.TypeParametersList.Add(new TypeParameter("E", 0, list.Scope, list));
        return list;
    }

    private (ClassType Holder, TypeParameter U) Holder(ClassType tester)
    {
        var holder = HandBuiltModel.Class("Holder<>");
        var u = new TypeParameter("U", 0, holder.Scope, holder);
        holder.TypeParametersList.Add(u);
        holder.FieldsList.Add(new Field("inner", slots.Closed(tester, u), default, new MemberModifiers()) { DeclaringType = holder });
        return (holder, u);
    }

    private (ClassType Counter, TypeParameter K) Counter()
    {
        var counter = HandBuiltModel.Class("Counter<>");
        var k = new TypeParameter("K", 0, counter.Scope, counter);
        counter.TypeParametersList.Add(k);
        counter.FieldsList.Add(new Field("hits", slots.Int, default, new MemberModifiers { IsStatic = true }) { DeclaringType = counter });
        return (counter, k);
    }

    private (Method Blank, TypeParameter B) Blank()
    {
        var owner = HandBuiltModel.Class("Api");
        var b = new TypeParameter("B", 0, new Scope(null), null);
        var sized = slots.Closed(slots.Array, b, slots.Literal(2));
        var blank = new Method("Blank", sized, [], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = owner,
            Scope = owner.Scope,
            Body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(new ArrayCreationExpression(HandBuiltModel.Node(), b, slots.Refinements.Literal(2L), sized))),
        };
        blank.TypeParametersList.Add(b);
        b.DeclaringMethod = blank;
        owner.MethodsList.Add(blank);
        return (blank, b);
    }

    private (Method Same, TypeParameter A) Same()
    {
        var owner = HandBuiltModel.Class("Api");
        var a = new TypeParameter("A", 0, new Scope(null), null);
        var same = new Method("Same", slots.Refinements.Bool, [], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = owner,
            Scope = owner.Scope,
            Body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(new TypeLiteralExpression(HandBuiltModel.Node(), a, slots.Refinements.Built.Type("Type")))),
        };
        same.TypeParametersList.Add(a);
        a.DeclaringMethod = same;
        owner.MethodsList.Add(same);
        return (same, a);
    }

    private static Block Body(ClassType type)
    {
        return type.MethodsList[0].Body!;
    }

    private static NewExpression New(IMistType type)
    {
        return new NewExpression(HandBuiltModel.Node(), type, [], null, [], null);
    }

    private static InvocationExpression Call(Method method, IReadOnlyList<IMistType> typeArguments)
    {
        return new InvocationExpression(
            HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method, typeArguments), [], method.ReturnType, method, []);
    }

    private IsPatternExpression Is(Expression scrutinee, Pattern pattern)
    {
        return new IsPatternExpression(HandBuiltModel.Node(), scrutinee, pattern, slots.Refinements.Bool);
    }

    private static TypeTestPattern Test(IMistType type)
    {
        return new TypeTestPattern(HandBuiltModel.Node(), type, binding: null);
    }

    private IMistType Union(params IMistType[] constituents)
    {
        return slots.Refinements.Built.Interner.GetOrCreateUnionType(constituents);
    }

    private static string Emit(Expression expression)
    {
        return PropertyPatternEmissionTests.Emit(expression);
    }
}
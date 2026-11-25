using Mist.Common.Diagnostics;
using Mist.Generated;
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

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ElementComparisonReachTests
{
    private const string CallsImpure = "MIST0139";
    private const string Unseen = "whose code the program cannot see";

    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MemberModifiers Pure = Public with { IsPure = true, IsReadonly = true };
    private static readonly MemberModifiers Local = Public with { IsLocal = true, IsIntrinsic = true };
    private static readonly MemberModifiers LocalReadonly = Local with { IsReadonly = true };

    private readonly HandBuiltModel model = new();
    private readonly ClassType integer;
    private readonly ClassType boolean;
    private readonly ClassType unit;
    private readonly ClassType probe = HandBuiltModel.Class("Probe");
    private readonly ClassType list;
    private readonly TypeParameter held;
    private readonly Constructor fresh;
    private readonly List<MistType> declared = [];

    public ElementComparisonReachTests()
    {
        integer = Intrinsic("Int32");
        boolean = Intrinsic("Boolean");
        unit = Intrinsic("Void");
        list = model.Type("List<>");
        list.Modifiers = Public with { IsIntrinsic = true };
        held = new TypeParameter("T", 0, list.Scope, list);
        list.TypeParametersList.Add(held);
        fresh = new Constructor("List", list, [], Pure with { IsIntrinsic = true }) { DeclaringType = list };
        list.ConstructorsList.Add(fresh);
    }

    [Theory]
    [InlineData("a union of an equatable struct and a class", null)]
    [InlineData("a struct holding an equatable struct", null)]
    [InlineData("a struct holding a CLR class", Unseen)]
    [InlineData("a class holding a CLR class", null)]
    [InlineData("a struct with a static CLR field", null)]
    [InlineData("a tuple holding an equatable class", "whose comparison calls 'Equals'")]
    [InlineData("an alias of an intrinsic tuple", null)]
    [InlineData("a newtype over an intrinsic", null)]
    [InlineData("an enum", null)]
    [InlineData("a function", null)]
    [InlineData("an equatable class answering no Equals", Unseen)]
    [InlineData("an interface", Unseen)]
    public void ContainsInAPureScopeIsJudgedByTheEqualityTheDefaultComparerRuns(string element, string? refusal)
    {
        AssertJudged(ListCall("Contains", Element(element)), refusal);
    }

    [Theory]
    [InlineData("IndexOf", "whose comparison calls 'Equals'")]
    [InlineData("Remove", "whose comparison calls 'Equals'")]
    [InlineData("RemoveAt", null)]
    [InlineData("Reverse", null)]
    public void OnlyTheListMembersThatCompareElementsReachTheirEquals(string member, string? refusal)
    {
        AssertJudged(ListCall(member, Element("an equatable class")), refusal);
    }

    [Theory]
    [InlineData("a tuple holding a comparable class", "whose comparison calls 'CompareTo'")]
    [InlineData("an alias of an intrinsic tuple", null)]
    [InlineData("an enum", null)]
    [InlineData("an equatable class", null)]
    [InlineData("a CLR class", Unseen)]
    [InlineData("a comparable class answering no CompareTo", Unseen)]
    [InlineData("an interface", Unseen)]
    public void SortInAPureScopeIsJudgedByTheOrderingTheDefaultComparerRuns(string element, string? refusal)
    {
        AssertJudged(ListCall("Sort", Element(element)), refusal);
    }

    [Theory]
    [InlineData("nested anonymous == nested anonymous", "whose comparison calls 'Equals'")]
    [InlineData("anonymous != anonymous", "whose comparison calls 'Equals'")]
    [InlineData("unknown == anonymous of an intrinsic", Unseen)]
    [InlineData("anonymous of an intrinsic == unknown", null)]
    public void AnonymousEqualityRunsTheLeftOperandsEquals(string comparison, string? refusal)
    {
        AssertJudged(AnonymousComparison(comparison), refusal);
    }

    [Theory]
    [InlineData(false, "Cannot call 'Equals' from readonly method 'Peek': it is not readonly. A non-readonly call requires a local member on an owned receiver.")]
    [InlineData(true, null)]
    public void AReadonlyMethodCallingContainsIsJudgedByTheElementsEquals(bool readonlyEquals, string? message)
    {
        var item = Item(readonlyEquals);
        var l = Input("l", model.Interner.GetOrCreateConstructedType(list, [item]));
        var x = Input("x", item);
        var contains = ListMember("Contains", boolean, LocalReadonly, new Parameter("item", held));

        var findings = Readonly(Peek([l, x], new ExpressionStatement(Call(contains, Ref(l), 37, Ref(x)))));

        Assert.Equal(message is null ? [] : [message], findings.Select(finding => finding.Message));
    }

    [Theory]
    [InlineData(false, "Cannot call 'Equals' from readonly method 'Peek': it is not readonly. A non-readonly call requires a local member on an owned receiver.")]
    [InlineData(true, null)]
    public void AReadonlyMethodComparingAnonymousValuesIsJudgedByTheirFieldsEquals(bool readonlyEquals, string? message)
    {
        var shape = new AnonymousType(["A"], [Item(readonlyEquals)]);
        var a = Input("a", shape);
        var b = Input("b", shape);
        var same = new BinaryExpression(HandBuiltModel.Node(37), Ref(a), BinaryOperatorKind.Equal, Ref(b), boolean);

        var findings = Readonly(Peek([a, b], new LocalDeclarationStatement(new LocalVariable("same", boolean), same)));

        Assert.Equal(message is null ? [] : [message], findings.Select(finding => finding.Message));
    }

    [Theory]
    [InlineData(false, "Cannot call 'Has' from readonly method 'Peek': it is not readonly. A non-readonly call requires a local member on an owned receiver.")]
    [InlineData(true, null)]
    public void AMethodCallingContainsIsInferredReadonlyOnlyWhenTheElementsEqualsIs(bool readonlyEquals, string? message)
    {
        var item = Item(readonlyEquals);
        var l = Input("l", model.Interner.GetOrCreateConstructedType(list, [item]));
        var x = Input("x", item);
        var contains = ListMember("Contains", boolean, LocalReadonly, new Parameter("item", held));
        var has = Method(probe, "Has", boolean, Public, HandBuiltModel.Block(new ReturnStatement(Call(contains, Ref(l), 0, Ref(x)), boolean)), l, x);
        var l2 = Input("l", l.Type);
        var x2 = Input("x", item);

        var findings = Readonly(Peek([l2, x2], new ExpressionStatement(Call(has, new ThisExpression(HandBuiltModel.Node(), probe), 37, Ref(l2), Ref(x2)))));

        Assert.Equal(message is null ? [] : [message], findings.Select(finding => finding.Message));
    }

    private IMistType Element(string shape)
    {
        return shape switch
        {
            "a union of an equatable struct and a class" =>
                model.Interner.GetOrCreateUnionType([Answering(DeclaredStruct("Loud"), "IEquatable<>", "Equals"), Answering(DeclaredClass("Plain"), null, null)]),
            "a struct holding an equatable struct" => Holding(DeclaredStruct("Pair"), Answering(DeclaredStruct("Loud"), "IEquatable<>", "Equals")),
            "a struct holding a CLR class" => Holding(DeclaredStruct("Pair"), HandBuiltModel.Class("Endpoint")),
            "a class holding a CLR class" => Holding(DeclaredClass("Pair"), HandBuiltModel.Class("Endpoint")),
            "a struct with a static CLR field" => Holding(DeclaredStruct("Pair"), HandBuiltModel.Class("Endpoint"), isStatic: true),
            "a tuple holding an equatable class" => model.Interner.GetOrCreateTupleType([integer, Element("an equatable class")]),
            "a tuple holding a comparable class" => model.Interner.GetOrCreateTupleType([integer, Answering(DeclaredClass("Rank"), "IComparable<>", "CompareTo")]),
            "an alias of an intrinsic tuple" =>
                new TypeAlias("Pair", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = model.Interner.GetOrCreateTupleType([integer, integer]) },
            "a newtype over an intrinsic" => HandBuiltModel.Newtype("Meters", integer),
            "an enum" => HandBuiltModel.Enum("Level", integer),
            "a function" => model.Interner.GetOrCreateFunctionType([], integer, false),
            "an equatable class" => Answering(DeclaredClass("Item"), "IEquatable<>", "Equals"),
            "an equatable class answering no Equals" => Answering(DeclaredClass("Item"), "IEquatable<>", null),
            "a comparable class answering no CompareTo" => Answering(DeclaredClass("Rank"), "IComparable<>", null),
            "a CLR class" => HandBuiltModel.Class("Endpoint"),
            "an interface" => new AbstractType("IShape", "Probe", new Scope(null), null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    private IReadOnlyList<Diagnostic> ListCall(string member, IMistType element)
    {
        var called = member switch
        {
            "Sort" or "Reverse" => ListMember(member, unit, Local),
            "RemoveAt" => ListMember(member, unit, Local, new Parameter("index", integer)),
            "Remove" => ListMember(member, boolean, Local, new Parameter("item", held)),
            _ => ListMember(member, boolean, LocalReadonly, new Parameter("item", held)),
        };
        var closed = model.Interner.GetOrCreateConstructedType(list, [element]);
        var l = new LocalVariable("l", closed);
        var probed = Input("probed", element);
        Expression[] arguments = member switch
        {
            "Sort" or "Reverse" => [],
            "RemoveAt" => [HandBuiltModel.Literal(integer, 0L)],
            "Remove" when element is ConcreteType owned => [new NewExpression(HandBuiltModel.Node(), owned, [], owned.ConstructorsList[0], [], [])],
            _ => [Ref(probed)],
        };
        var compare = Method(probe, "Compare", unit, Pure with { IsStatic = true }, HandBuiltModel.Block(
            new LocalDeclarationStatement(l, new NewExpression(HandBuiltModel.Node(), closed, [], fresh, [], [])),
            new ExpressionStatement(Call(called, Ref(l), 37, arguments))), probed);
        return model.Analyze(new PurityAnalyzer(), compare.Body, [.. declared], probe, compare);
    }

    private IReadOnlyList<Diagnostic> AnonymousComparison(string comparison)
    {
        var item = (ClassType)Element("an equatable class");
        var inner = new AnonymousType(["A"], [item]);
        var nested = new AnonymousType(["B"], [inner]);
        var counted = new AnonymousType(["N"], [integer]);
        var o = Input("o", UnknownType.Instance);

        Expression made(AnonymousType shape)
        {
            return shape == nested
                ? new AnonymousObjectExpression(HandBuiltModel.Node(), [made(inner)], nested)
                : shape == inner
                    ? new AnonymousObjectExpression(HandBuiltModel.Node(), [new NewExpression(HandBuiltModel.Node(), item, [], item.ConstructorsList[0], [], [])], inner)
                    : new AnonymousObjectExpression(HandBuiltModel.Node(), [HandBuiltModel.Literal(integer, 1L)], counted);
        }

        var (left, kind, right) = comparison switch
        {
            "nested anonymous == nested anonymous" => (made(nested), BinaryOperatorKind.Equal, made(nested)),
            "anonymous != anonymous" => (made(inner), BinaryOperatorKind.NotEqual, made(inner)),
            "unknown == anonymous of an intrinsic" => (Ref(o), BinaryOperatorKind.Equal, made(counted)),
            "anonymous of an intrinsic == unknown" => (made(counted), BinaryOperatorKind.Equal, Ref(o)),
            _ => throw new ArgumentOutOfRangeException(nameof(comparison)),
        };
        var same = Method(probe, "Same", boolean, Pure with { IsStatic = true }, HandBuiltModel.Block(
            new ReturnStatement(new BinaryExpression(HandBuiltModel.Node(37), left, kind, right, boolean), boolean)), o);
        return model.Analyze(new PurityAnalyzer(), same.Body, [.. declared], probe, same);
    }

    private static void AssertJudged(IReadOnlyList<Diagnostic> findings, string? refusal)
    {
        var reported = string.Join("; ", findings.Select(finding => $"{finding.Number.Value}: {finding.Message}"));

        if(refusal is null)
        {
            Assert.True(findings.Count == 0, reported);
            return;
        }

        Assert.True(findings.Count == 1 && findings[0].Number.Value == CallsImpure, reported);
        Assert.Contains(refusal, findings[0].Message, StringComparison.Ordinal);
    }

    private ClassType Item(bool readonlyEquals)
    {
        var item = DeclaredClass("Item");
        item.BaseAbstractTypes.Add(new AbstractType("IEquatable<>", "System", new Scope(null), null, null));
        var hits = new Field("Hits", integer, default, Public) { DeclaringType = item };
        item.FieldsList.Add(hits);
        var body = readonlyEquals
            ? HandBuiltModel.Block(new ReturnStatement(HandBuiltModel.Literal(boolean, true), boolean))
            : HandBuiltModel.Block(
                new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), hits), HandBuiltModel.Literal(integer, 1L), integer)),
                new ReturnStatement(HandBuiltModel.Literal(boolean, true), boolean));
        _ = Method(item, "Equals", boolean, Public with { IsReadonly = readonlyEquals }, body, Input("other", item));
        return item;
    }

    private ConcreteType Answering(ConcreteType type, string? contract, string? answer)
    {
        if(contract is not null)
        {
            type.BaseAbstractTypes.Add(new AbstractType(contract, "System", new Scope(null), null, null));
        }

        type.ConstructorsList.Add(new Constructor(type.Name, type, [], Pure) { DeclaringType = type, Body = HandBuiltModel.Block() });

        if(answer is not null)
        {
            var returns = answer == "Equals" ? boolean : integer;
            var clock = HandBuiltModel.Class("Clock");
            var ticks = new Field("Ticks", integer, default, Public with { IsStatic = true }) { DeclaringType = clock };
            clock.FieldsList.Add(ticks);
            var tick = new AssignmentExpression(
                HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), clock), ticks.Name, integer, member: new FieldResult(ticks, clock, [])),
                HandBuiltModel.Literal(integer, 1L), integer);
            _ = Method(type, answer, returns, Public, HandBuiltModel.Block(
                new ExpressionStatement(tick), new ReturnStatement(HandBuiltModel.Literal(returns, answer == "Equals" ? true : 0L), returns)), Input("other", type));
        }

        return type;
    }

    private static ConcreteType Holding(ConcreteType holder, IMistType field, bool isStatic = false)
    {
        holder.FieldsList.Add(new Field("Value", field, default, Public with { IsStatic = isStatic }) { DeclaringType = holder });
        return holder;
    }

    private ClassType DeclaredClass(string name)
    {
        var type = new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>(40));
        declared.Add(type);
        return type;
    }

    private StructType DeclaredStruct(string name)
    {
        var type = new StructType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>(40));
        declared.Add(type);
        return type;
    }

    private Method ListMember(string name, IMistType returns, MemberModifiers modifiers, params Parameter[] parameters)
    {
        var method = new Method(name, returns, [.. parameters], modifiers) { DeclaringType = list };
        list.MethodsList.Add(method);
        return method;
    }

    private Method Peek(LocalVariable[] inputs, params Statement[] statements)
    {
        return Method(probe, "Peek", unit, Public with { IsReadonly = true }, HandBuiltModel.Block(statements), inputs);
    }

    private IReadOnlyList<Diagnostic> Readonly(Method peek)
    {
        return model.Analyze(new ReadonlyAnalyzer(), peek.Body, [.. declared], probe, peek);
    }

    private ClassType Intrinsic(string name)
    {
        var type = model.Type(name);
        type.Modifiers = new MemberModifiers { IsReadonly = true, IsIntrinsic = true };
        return type;
    }

    private static Method Method(MemberType owner, string name, IMistType returns, MemberModifiers modifiers, Block body, params LocalVariable[] inputs)
    {
        var method = new Method(name, returns, [.. inputs.Select(input => input.Parameter!)], modifiers) { DeclaringType = owner, Body = body };
        owner.MethodsList.Add(method);
        return method;
    }

    private static LocalVariable Input(string name, IMistType type)
    {
        return new LocalVariable(name, type) { Parameter = new Parameter(name, type) };
    }

    private static LocalReferenceExpression Ref(LocalVariable local)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), local);
    }

    private static InvocationExpression Call(Method method, Expression receiver, int offset, params Expression[] arguments)
    {
        return new InvocationExpression(
            HandBuiltModel.Node(offset), new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType), arguments,
            method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }
}
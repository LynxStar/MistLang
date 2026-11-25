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

/// <summary>
/// Every owned slot takes only what nothing else holds (ownership-v2 Requirement 6.3 and 6.9, owned-fields
/// Requirements 2.4 and 8), and only an owned binding may take a value out of an owned field.
/// </summary>
[Trait("Tier", "2")]
public sealed class OwnedDestinationDecisionTests
{
    private const string ParameterNotOwned = "parameter 'ext' is not owned, so the caller may still hold its value";
    private const string SharedStatic = "field 'Shared' is storage other references reach";

    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly HandBuiltModel model = new();
    private readonly ClassType unit;
    private readonly ClassType integer;
    private readonly ClassType box = HandBuiltModel.Class("Box");
    private readonly Constructor fresh;
    private readonly ClassType holder = HandBuiltModel.Class("Holder");
    private readonly Field shared;
    private readonly List<MistType> declared = [];

    public OwnedDestinationDecisionTests()
    {
        unit = model.Type("Void");
        integer = model.Type("Int32");
        fresh = new Constructor(box.Name, box, [], Public) { DeclaringType = box, Body = HandBuiltModel.Block() };
        box.ConstructorsList.Add(fresh);
        shared = Field(holder, "Shared", box, OwnershipKind.Unrestricted, isStatic: true);
        declared.AddRange([box, holder]);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void AnOwnedInstancePropertyInitializerIsAnOwnedDestination(bool owned, bool isStatic, bool refused)
    {
        var initializer = StaticRead(shared, 37);
        var property = new Property("Inner", box, Public with { IsOwned = owned, IsStatic = isStatic }) { DeclaringType = holder, Initializer = initializer };
        holder.PropertiesList.Add(property);

        var findings = Judge(initializer, property);

        if(refused)
        {
            AssertRefused(Assert.Single(findings), "owned property 'Inner'", SharedStatic);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Theory]
    [InlineData(OwnershipKind.Owned, false, true)]
    [InlineData(OwnershipKind.Owned, true, false)]
    [InlineData(OwnershipKind.Unrestricted, false, false)]
    public void AChainArgumentIsJudgedByTheParameterItFills(OwnershipKind ownership, bool flattened, bool refused)
    {
        var inner = new Parameter("inner", box, new ParameterModifier(false, false, ownership)) { IsFlattened = flattened };
        var (chaining, argument) = Chain([inner], Ref(Input("ext"), 37));

        var findings = Judge(argument, chaining);

        if(refused)
        {
            AssertRefused(Assert.Single(findings), "owned parameter 'inner'", ParameterNotOwned);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Fact]
    public void AChainArgumentPastTheTargetsParametersFillsNoOwnedSlot()
    {
        var inner = new Parameter("inner", box, new ParameterModifier(false, false, OwnershipKind.Owned));
        var ext = Input("ext");
        var (chaining, first) = Chain([inner], Ref(ext, 37), Ref(ext, 40));
        var extra = chaining.ChainArgumentsList[1];

        AssertRefused(Assert.Single(Judge(first, chaining)), "owned parameter 'inner'", ParameterNotOwned);
        Assert.Empty(Judge(extra, chaining));
    }

    public static TheoryData<string, string> Destinations => new()
    {
        { "backing-field", "owned property 'Slot'" },
        { "property", "owned property 'Slot'" },
        { "member-property", "owned property 'Slot'" },
        { "initializer-property", "owned property 'Slot'" },
        { "initializer-field", "owned field 'Kept'" },
        { "ref-return", "the slot 'At' returns a reference to" },
    };

    [Theory]
    [MemberData(nameof(Destinations))]
    public void AStoreOutsideAPromisedScopeNamesTheOwnedSlotItFills(string target, string destination)
    {
        var (method, _) = Store(target, Public);

        AssertRefused(Assert.Single(Judge(method.Body!, method)), destination, ParameterNotOwned);
    }

    [Theory]
    [InlineData("static-member-property")]
    [InlineData("static-property")]
    [InlineData("unowned-member-property")]
    public void AStoreIntoASlotThatIsNotOwnedIsNotJudgedHere(string target)
    {
        var (method, _) = Store(target, Public);

        Assert.Empty(Judge(method.Body!, method));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ACompoundStoreIntoAnOwnedFieldIsLeftToPurityOnlyInAPromisedScope(bool local, bool refused)
    {
        var slot = Field(holder, "Slot", box, OwnershipKind.Owned);
        var plus = new Operator(OperatorKind.Plus, box, [new Parameter("left", box), new Parameter("right", integer)], Public with { IsStatic = true }) { DeclaringType = box };
        var compound = new CompoundAssignmentExpression(HandBuiltModel.Node(37), new FieldReferenceExpression(HandBuiltModel.Node(), slot), BinaryOperatorKind.Add,
            HandBuiltModel.Literal(integer, 1L), box, plus, IncrementForm.None);
        var method = Method("Bump", Public with { IsLocal = local }, HandBuiltModel.Block(new ExpressionStatement(compound)));

        var findings = Judge(method.Body!, method);

        if(refused)
        {
            Assert.Equal("MIST0264", Assert.Single(findings).Number.Value);
            Assert.StartsWith("Cannot move this value into owned field 'Slot': ", findings[0].Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Theory]
    [InlineData("pure-method")]
    [InlineData("local-method")]
    [InlineData("local-setter")]
    [InlineData("pure-property-getter")]
    public void APromisedScopeLeavesItsOwnStoresToPurityButNotALambdasStores(string scope)
    {
        var slot = Field(holder, "Kept", box, OwnershipKind.Owned);
        var ext = Input("ext");
        Statement store(int offset)
        {
            return new ExpressionStatement(Assign(new FieldReferenceExpression(HandBuiltModel.Node(), slot), Ref(ext, offset)));
        }

        var lambda = new LambdaExpression(HandBuiltModel.Node(), [], null, HandBuiltModel.Block(store(40)), false, model.Interner.GetOrCreateFunctionType([], unit, false));
        var body = HandBuiltModel.Block(store(37), new ExpressionStatement(lambda));
        object member = scope switch
        {
            "pure-method" => Method("Keep", Public with { IsPure = true }, body, ext),
            "local-method" => Method("Keep", Public with { IsLocal = true }, body, ext),
            "local-setter" => new Property("Value", box, Public) { DeclaringType = holder, Setter = new PropertyAccessor { Body = body, OwnModifiers = Public with { IsLocal = true } } },
            _ => new Property("Value", box, Public with { IsPure = true }) { DeclaringType = holder, Getter = new PropertyAccessor { Body = body } },
        };

        var finding = Assert.Single(Judge(body, member));

        AssertRefused(finding, "owned field 'Kept'", ParameterNotOwned, column: 41);
    }

    [Fact]
    public void EachOwnedBindingADeconstructionFillsIsJudgedAgainstItsOwnElement()
    {
        var ext = Input("ext");
        var first = new LocalVariable("first", box) { Ownership = OwnershipKind.Owned };
        var second = new LocalVariable("second", box) { Ownership = OwnershipKind.Owned };
        var tuple = model.Interner.GetOrCreateTupleType([box, box]);
        var targets = new TupleLiteralExpression(HandBuiltModel.Node(), [Ref(first), Ref(second)], [null, null], tuple);
        var values = new TupleLiteralExpression(HandBuiltModel.Node(), [New(), Ref(ext, 37)], [null, null], tuple);
        var method = Method("Split", Public, HandBuiltModel.Block(
            new LocalDeclarationStatement(first, New()), new LocalDeclarationStatement(second, New()),
            new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), targets, values, tuple))), ext);

        AssertRefused(Assert.Single(Judge(method.Body!, method)), "owned binding 'second'", ParameterNotOwned);
    }

    [Theory]
    [InlineData(OwnershipKind.Unrestricted, "owned-parameter", true)]
    [InlineData(OwnershipKind.Unrestricted, "owned-binding", false)]
    [InlineData(OwnershipKind.Owned, "owned-parameter", false)]
    public void OnlyAnOwnedBindingMayTakeAValueOutOfAnOwnedFieldOfASharedParent(OwnershipKind parent, string destination, bool refused)
    {
        var car = HandBuiltModel.Class("Car");
        var front = Field(car, "Front", box, OwnershipKind.Owned);
        var make = new Constructor(car.Name, car, [], Public) { DeclaringType = car, Body = HandBuiltModel.Block() };
        car.ConstructorsList.Add(make);
        declared.Add(car);
        var holding = new LocalVariable("car", car) { Ownership = parent };
        var taken = new MemberAccessExpression(HandBuiltModel.Node(37), Ref(holding), front.Name, box, member: new FieldResult(front, car, []));
        var method = Method("Run", Public, HandBuiltModel.Block(
            new LocalDeclarationStatement(holding, new NewExpression(HandBuiltModel.Node(), car, [], make, [], [])),
            destination == "owned-binding"
                ? new LocalDeclarationStatement(new LocalVariable("mine", box) { Ownership = OwnershipKind.Owned }, taken)
                : new ExpressionStatement(Call(Taker(), taken))));

        var findings = Judge(method.Body!, method);

        if(refused)
        {
            AssertRefused(Assert.Single(findings), "owned parameter 'value'", "owned field 'Front' still holds it, and only an owned binding may take a value out of one");
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Theory]
    [InlineData("bare")]
    [InlineData("implicit-conversion")]
    [InlineData("cast")]
    [InlineData("as")]
    public void APlainLocalSeenThroughATransparentConversionIsStillNotAnOwnedBinding(string wrapper)
    {
        var plain = new LocalVariable("plain", box);
        var read = Ref(plain);
        Expression value = wrapper switch
        {
            "bare" => Ref(plain, 37),
            "implicit-conversion" => new ImplicitConversionExpression(HandBuiltModel.Node(37), read, box, []),
            "cast" => new CastExpression(HandBuiltModel.Node(37), read, box, [], box),
            _ => new AsExpression(HandBuiltModel.Node(37), read, box, AsConversionKind.Identity, null, box),
        };
        var method = Method("Run", Public, HandBuiltModel.Block(new LocalDeclarationStatement(plain, New()), new ExpressionStatement(Call(Taker(), value))));

        AssertRefused(Assert.Single(Judge(method.Body!, method)), "owned parameter 'value'", "local 'plain' is not an owned binding, so nothing stops it being used after the move");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ANestedDefaultEntryIsJudgedAgainstTheFieldsOfTheTypeItBinds(bool bound, bool owned)
    {
        var part = HandBuiltModel.Class("Part");
        _ = Field(part, "Inner", box, owned ? OwnershipKind.Owned : OwnershipKind.Unrestricted);
        var partSlot = Field(holder, "Part", part, OwnershipKind.Unrestricted);
        holder.DefaultBlock = new DefaultBlock(
            [new DefaultBindingEntry(partSlot.Name, bound ? part : null, [new DefaultValueEntry("Inner", StaticRead(shared, 37))], new DefaultTypeBindingDefaultFieldInitializerNode())],
            new DefaultBlockDeclarationNode());

        var findings = JudgeDefaults(holder);

        if(owned)
        {
            AssertRefused(Assert.Single(findings), "owned field 'Inner'", SharedStatic);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Fact]
    public void ATopLevelDefaultEntryNamesTheOwnedFieldItFills()
    {
        _ = Field(holder, "Inner", box, OwnershipKind.Owned);
        holder.DefaultBlock = new DefaultBlock([new DefaultValueEntry("Inner", StaticRead(shared, 37))], new DefaultBlockDeclarationNode());

        AssertRefused(Assert.Single(JudgeDefaults(holder)), "owned field 'Inner'", SharedStatic);
    }

    private (Method Method, Property Slot) Store(string target, MemberModifiers modifiers)
    {
        var isStatic = target is "static-member-property" or "static-property";
        var slot = new Property("Slot", box, Public with { IsOwned = target != "unowned-member-property", IsStatic = isStatic }) { DeclaringType = holder };
        holder.PropertiesList.Add(slot);
        var kept = Field(holder, "Kept", box, OwnershipKind.Owned);
        var ext = Input("ext");
        var self = new ThisExpression(HandBuiltModel.Node(), holder);
        Statement statement;
        if(target is "initializer-property" or "initializer-field")
        {
            var entry = target == "initializer-field"
                ? new ObjectInitializerAssignment(HandBuiltModel.Node(), kept.Name, Ref(ext, 37), box, member: new FieldResult(kept, holder, []))
                : new ObjectInitializerAssignment(HandBuiltModel.Node(), slot.Name, Ref(ext, 37), box, member: new PropertyResult(slot, holder, []));
            var make = new Constructor(holder.Name, holder, [], Public) { DeclaringType = holder, Body = HandBuiltModel.Block() };
            holder.ConstructorsList.Add(make);
            statement = new ExpressionStatement(new NewExpression(HandBuiltModel.Node(), holder, [], make, [entry], []));
        }
        else
        {
            Expression destination = target switch
            {
                "backing-field" => new BackingFieldReferenceExpression(HandBuiltModel.Node(), slot),
                "property" or "static-property" => new PropertyReferenceExpression(HandBuiltModel.Node(), slot),
                "member-property" or "unowned-member-property" => new MemberAccessExpression(HandBuiltModel.Node(), self, slot.Name, box, member: new PropertyResult(slot, holder, [])),
                "static-member-property" => new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), holder), slot.Name, box,
                    member: new PropertyResult(slot, holder, [])),
                _ => Call(Method("At", Public with { IsRef = true }, null)),
            };
            statement = new ExpressionStatement(Assign(destination, Ref(ext, 37)));
        }

        return (Method("Take", modifiers, HandBuiltModel.Block(statement), ext), slot);
    }

    private (Constructor Chaining, Expression First) Chain(List<Parameter> targetParameters, params Expression[] arguments)
    {
        var target = new Constructor(holder.Name, holder, targetParameters, Public) { DeclaringType = holder, Body = HandBuiltModel.Block() };
        var chaining = new Constructor(holder.Name, holder, [new Parameter("ext", box)], Public) { DeclaringType = holder, Body = HandBuiltModel.Block(), ChainedConstructor = target };
        chaining.ChainArgumentsList.AddRange(arguments);
        holder.ConstructorsList.AddRange([target, chaining]);
        return (chaining, arguments[0]);
    }

    private Method Taker()
    {
        var value = new Parameter("value", box, new ParameterModifier(false, false, OwnershipKind.Owned));
        var take = new Method("Take", unit, [value], Public with { IsStatic = true }) { DeclaringType = holder };
        holder.MethodsList.Add(take);
        return take;
    }

    private Method Method(string name, MemberModifiers modifiers, Block? body, params LocalVariable[] inputs)
    {
        var method = new Method(name, body is null ? box : unit, [.. inputs.Select(input => input.Parameter!)], modifiers) { DeclaringType = holder, Body = body };
        holder.MethodsList.Add(method);
        return method;
    }

    private IReadOnlyList<Diagnostic> Judge(object root, object member)
    {
        var diagnostics = new DiagnosticBag();
        var context = model.Context(new OwnershipAnalyzer(), diagnostics, root, declared, holder, member);
        OwnedDestinations.Judge(context, context.Bodies.Single());
        return diagnostics.ToImmutableArray();
    }

    private IReadOnlyList<Diagnostic> JudgeDefaults(ConcreteType type)
    {
        var diagnostics = new DiagnosticBag();
        OwnedDestinations.JudgeDefaults(model.Context(new OwnershipAnalyzer(), diagnostics, types: declared), type);
        return diagnostics.ToImmutableArray();
    }

    private static void AssertRefused(Diagnostic finding, string destination, string reason, int column = 38)
    {
        Assert.Equal("MIST0264", finding.Number.Value);
        Assert.Equal(column, finding.Position().Start.Column);
        Assert.StartsWith($"Cannot move this value into {destination}: {reason}. An owned destination", finding.Message, StringComparison.Ordinal);
    }

    private static Field Field(ConcreteType owner, string name, IMistType type, OwnershipKind ownership, bool isStatic = false)
    {
        var field = new Field(name, type, ownership, Public with { IsStatic = isStatic, IsReadonly = isStatic }) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }

    private LocalVariable Input(string name)
    {
        return new LocalVariable(name, box) { Parameter = new Parameter(name, box) };
    }

    private NewExpression New()
    {
        return new NewExpression(HandBuiltModel.Node(), box, [], fresh, [], []);
    }

    private static LocalReferenceExpression Ref(LocalVariable local, int offset = 0)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(offset), local);
    }

    private static MemberAccessExpression StaticRead(Field field, int offset)
    {
        var owner = field.DeclaringType!;
        return new MemberAccessExpression(HandBuiltModel.Node(offset), new TypeReferenceExpression(HandBuiltModel.Node(), owner), field.Name, field.Type,
            member: new FieldResult(field, owner, []));
    }

    private static AssignmentExpression Assign(Expression target, Expression value)
    {
        return new AssignmentExpression(HandBuiltModel.Node(), target, value, target.Type);
    }

    private static InvocationExpression Call(Method method, params Expression[] arguments)
    {
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), arguments, method.ReturnType, method,
            [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }
}
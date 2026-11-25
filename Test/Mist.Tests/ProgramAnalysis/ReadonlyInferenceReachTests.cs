using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

/// <summary>
/// What <see cref="ReadonlyInference"/> infers for an unmarked member from the members an initializer
/// block, an allocation with no constructor, a slice and a foreach call on its behalf: readonly-contexts
/// Requirements 10.8 and 11.5 judge each by its status, and purity Requirement 11.1 admits a local one on
/// what the member owns while counting an initializer's setter and a store into an owned slot. Asked
/// without a purity oracle, as the emitter asks before spelling C# <c>readonly</c>.
/// </summary>
[Trait("Tier", "2")]
public sealed class ReadonlyInferenceReachTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType maker = HandBuiltModel.Class("Maker");
    private readonly ClassType box = HandBuiltModel.Class("Box");
    private readonly ClassType integer;
    private readonly ClassType boolean;
    private readonly Field count;
    private readonly Field total;

    public ReadonlyInferenceReachTests()
    {
        integer = model.Type("Int32");
        boolean = model.Type("Boolean");
        count = new Field("count", integer, default, default) { DeclaringType = box };
        total = new Field("Total", integer, default, new MemberModifiers { IsStatic = true }) { DeclaringType = box };
        box.FieldsList.Add(count);
        box.FieldsList.Add(total);
    }

    [Theory]
    [InlineData("auto", true)]
    [InlineData("body-writing-nothing", true)]
    [InlineData("body-writing-a-static", false)]
    [InlineData("body-writing-its-own-field", false)]
    [InlineData("local-body-writing-its-own-field", true)]
    [InlineData("readonly-body-writing-a-static", true)]
    [InlineData("bodiless", false)]
    public void AnInitializerEntryIsJudgedByTheSetterItRuns(string setter, bool expected)
    {
        var level = new Property("Level", integer, default)
        {
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = setter switch
            {
                "auto" => new PropertyAccessor { IsAuto = true },
                "body-writing-nothing" => new PropertyAccessor { Body = HandBuiltModel.Block() },
                "body-writing-a-static" => new PropertyAccessor { Body = Writes(total) },
                "body-writing-its-own-field" => new PropertyAccessor { Body = Writes(count) },
                "local-body-writing-its-own-field" => new PropertyAccessor { Body = Writes(count), OwnModifiers = new MemberModifiers { IsLocal = true } },
                "readonly-body-writing-a-static" => new PropertyAccessor { Body = Writes(total), OwnModifiers = new MemberModifiers { IsReadonly = true } },
                _ => new PropertyAccessor(),
            },
            DeclaringType = box,
        };
        box.PropertiesList.Add(level);
        var entry = new ObjectInitializerAssignment(HandBuiltModel.Node(), level.Name, One(), integer, member: new PropertyResult(level, box, []));

        Assert.Equal(expected, new ReadonlyInference().IsReadonly(Make(Keep(Fresh(entry)))));
    }

    [Theory]
    [InlineData("writing-nothing", true)]
    [InlineData("writing-its-own-field", false)]
    [InlineData("local-writing-its-own-field", true)]
    [InlineData("bodiless", false)]
    [InlineData("readonly-bodiless", true)]
    public void ACollectionElementIsJudgedByTheAddItCalls(string add, bool expected)
    {
        var adds = new Method("Add", integer, [new Parameter("item", integer)], add switch
        {
            "local-writing-its-own-field" => new MemberModifiers { IsLocal = true },
            "readonly-bodiless" => new MemberModifiers { IsReadonly = true },
            _ => default,
        })
        {
            Body = add switch
            {
                "writing-nothing" => HandBuiltModel.Block(),
                "writing-its-own-field" or "local-writing-its-own-field" => Writes(count),
                _ => null,
            },
            DeclaringType = box,
        };
        box.MethodsList.Add(adds);
        var element = new ObjectInitializerAssignment(HandBuiltModel.Node(), null, One(), integer, adds);

        Assert.Equal(expected, new ReadonlyInference().IsReadonly(Make(Keep(Fresh(element)))));
    }

    [Theory]
    [InlineData(true, "handed", false)]
    [InlineData(true, "fresh", true)]
    [InlineData(false, "handed", true)]
    public void AnInitializerEntryStoringWhatTheMemberDoesNotOwnIntoAnOwnedPropertyIsAWrite(bool owned, string value, bool expected)
    {
        var holder = HandBuiltModel.Class("Holder");
        var held = new Property("Held", box, new MemberModifiers { IsOwned = owned })
        {
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
            DeclaringType = holder,
        };
        holder.PropertiesList.Add(held);
        Expression stored = value == "fresh" ? new NewExpression(HandBuiltModel.Node(), box, [], null, [], []) : Parameter("b", box);
        var entry = new ObjectInitializerAssignment(HandBuiltModel.Node(), held.Name, stored, box, member: new PropertyResult(held, holder, []));

        Assert.Equal(expected, new ReadonlyInference().IsReadonly(Make(Keep(new NewExpression(HandBuiltModel.Node(), holder, [], null, [entry], [])))));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AnAllocationWithNoConstructorRunsTheTypesInitializers_UnlessTheTypeIsDeclaredReadonly(bool readonlyType, bool expected)
    {
        var needy = HandBuiltModel.Class("Needy");
        needy.ConstructorsList.Add(new Constructor("Needy", needy, [], default));
        var seeded = HandBuiltModel.Class("Seeded");
        seeded.Modifiers = new MemberModifiers { IsReadonly = readonlyType };
        seeded.FieldsList.Add(new Field("seed", integer, default, default) { Initializer = Assign(total), DeclaringType = seeded });
        seeded.FieldsList.Add(new Field("need", needy, default, default) { DeclaringType = seeded });

        Assert.Equal(expected, new ReadonlyInference().IsReadonly(Make(Keep(new NewExpression(HandBuiltModel.Node(), seeded, [], null, [], [])))));
    }

    [Theory]
    [InlineData("index", "handed", "writing-a-static", false)]
    [InlineData("index", "handed", "readonly", true)]
    [InlineData("index", "fresh", "local-writing-its-own-field", true)]
    [InlineData("index", "handed", "local-writing-its-own-field", false)]
    [InlineData("pattern", "handed", "writing-a-static", false)]
    [InlineData("pattern", "handed", "readonly", true)]
    public void ASliceIsJudgedByTheSliceMethodItCalls_UnlessALocalOneOnWhatTheMemberOwns(string form, string receiver, string slice, bool expected)
    {
        var strip = HandBuiltModel.Class("Strip");
        var cells = new Field("cells", integer, default, default) { DeclaringType = strip };
        strip.FieldsList.Add(cells);
        var count = new Property("Count", integer, default) { Getter = new PropertyAccessor { IsAuto = true }, DeclaringType = strip };
        strip.PropertiesList.Add(count);
        var cut = new Method("Slice", integer, [new Parameter("start", integer), new Parameter("length", integer)], slice switch
        {
            "readonly" => new MemberModifiers { IsReadonly = true },
            "local-writing-its-own-field" => new MemberModifiers { IsLocal = true },
            _ => default,
        })
        {
            Body = HandBuiltModel.Block(
                Run(slice == "writing-a-static" ? Assign(total) : new AssignmentExpression(HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), cells), One(), integer)),
                new ReturnStatement(One(), integer)),
            DeclaringType = strip,
        };
        strip.MethodsList.Add(cut);
        var read = new CountedSlice(count, null, cut);
        var s = new LocalVariable("s", strip);
        Expression held = receiver == "fresh" ? new LocalReferenceExpression(HandBuiltModel.Node(), s) : Parameter("p", strip);
        var middle = new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(), integer, new LocalVariable("middle", integer)), IsSlice: true, integer);
        Expression sliced = form == "index"
            ? new IndexExpression(HandBuiltModel.Node(), held, new RangeExpression(HandBuiltModel.Node(), One(), One(), model.Type("Range")), integer) { Slice = read }
            : new IsPatternExpression(HandBuiltModel.Node(), held, new ListPattern(HandBuiltModel.Node(), [middle], strip) { Slice = read }, boolean);

        Assert.Equal(expected, new ReadonlyInference().IsReadonly(Make(
            new LocalDeclarationStatement(s, new NewExpression(HandBuiltModel.Node(), strip, [], null, [], [])),
            Run(sliced))));
    }

    [Theory]
    [InlineData("unrecorded", "handed", false)]
    [InlineData("readonly-members", "handed", true)]
    [InlineData("get-enumerator-writing-a-static", "handed", false)]
    [InlineData("bodiless-get-enumerator", "handed", false)]
    [InlineData("local-get-enumerator-writing-its-own-field", "fresh", true)]
    [InlineData("local-get-enumerator-writing-its-own-field", "handed", false)]
    [InlineData("move-next-advancing-its-cursor", "handed", false)]
    [InlineData("current-writing-a-static", "handed", false)]
    public void AForEachIsJudgedByTheMembersItCalls_UnlessALocalOneOnWhatTheMemberOwns(string shape, string collection, bool expected)
    {
        var cursor = HandBuiltModel.Class("Cursor");
        var position = new Field("position", integer, default, default) { DeclaringType = cursor };
        cursor.FieldsList.Add(position);
        var moveNext = new Method("MoveNext", boolean, [], shape == "move-next-advancing-its-cursor" ? default : new MemberModifiers { IsReadonly = true })
        {
            Body = HandBuiltModel.Block(Run(new AssignmentExpression(HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), position), One(), integer))),
            DeclaringType = cursor,
        };
        var current = new Property("Current", integer, default)
        {
            Getter = shape == "current-writing-a-static"
                ? new PropertyAccessor { Body = HandBuiltModel.Block(Run(Assign(total)), new ReturnStatement(One(), integer)) }
                : new PropertyAccessor { IsAuto = true },
            DeclaringType = cursor,
        };
        var getEnumerator = new Method("GetEnumerator", cursor, [], shape switch
        {
            "get-enumerator-writing-a-static" or "bodiless-get-enumerator" => default,
            "local-get-enumerator-writing-its-own-field" => new MemberModifiers { IsLocal = true },
            _ => new MemberModifiers { IsReadonly = true },
        })
        {
            Body = shape == "bodiless-get-enumerator" ? null : HandBuiltModel.Block(
                Run(Assign(shape == "local-get-enumerator-writing-its-own-field" ? count : total)),
                new ReturnStatement(new NewExpression(HandBuiltModel.Node(), cursor, [], null, [], []), cursor)),
            DeclaringType = box,
        };
        var made = new LocalVariable("made", box);
        Expression iterated = collection == "fresh" ? new LocalReferenceExpression(HandBuiltModel.Node(), made) : Parameter("b", box);
        var loop = new ForEachStatement(new LocalVariable("item", integer), iterated, HandBuiltModel.Block())
        {
            GetEnumerator = shape == "unrecorded" ? null : getEnumerator,
            MoveNext = shape == "unrecorded" ? null : moveNext,
            Current = shape == "unrecorded" ? null : current,
        };

        Assert.Equal(expected, new ReadonlyInference().IsReadonly(Make(
            new LocalDeclarationStatement(made, new NewExpression(HandBuiltModel.Node(), box, [], null, [], [])),
            loop)));
    }

    [Fact]
    public void AForEachOverAnArrayRecordsNoMembersAndIsNoWrite()
    {
        var integers = model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [integer, model.Interner.GetOrCreateUnknownValue(integer)]);
        var loop = new ForEachStatement(new LocalVariable("item", integer), Parameter("xs", integers), HandBuiltModel.Block());

        Assert.True(new ReadonlyInference().IsReadonly(Make(loop)));
    }

    private Method Make(params Statement[] statements)
    {
        return new Method("Make", integer, [], default) { Body = HandBuiltModel.Block(statements), DeclaringType = maker };
    }

    private static LocalDeclarationStatement Keep(Expression value)
    {
        return new LocalDeclarationStatement(new LocalVariable("made", value.Type), value);
    }

    private NewExpression Fresh(ObjectInitializerAssignment entry)
    {
        return new NewExpression(HandBuiltModel.Node(), box, [], null, [entry], []);
    }

    private static LocalReferenceExpression Parameter(string name, IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, type) { Parameter = new Parameter(name, type) });
    }

    private Block Writes(Field field)
    {
        return HandBuiltModel.Block(Run(Assign(field)));
    }

    private AssignmentExpression Assign(Field field)
    {
        return new AssignmentExpression(HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), field), One(), integer);
    }

    private LiteralExpression One()
    {
        return HandBuiltModel.Literal(integer, 1L);
    }

    private static ExpressionStatement Run(Expression expression)
    {
        return new ExpressionStatement(expression);
    }
}
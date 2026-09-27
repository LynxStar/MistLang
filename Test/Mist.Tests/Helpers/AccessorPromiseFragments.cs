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
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Helpers;

internal sealed class AccessorPromiseFragments
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MemberModifiers Pure = Public with { IsPure = true, IsReadonly = true };
    private static readonly MemberModifiers PureStatic = Pure with { IsStatic = true };
    private static readonly MemberModifiers ReadonlyStatic = Public with { IsStatic = true, IsReadonly = true };
    private static readonly MemberModifiers Local = Public with { IsLocal = true };
    private static readonly MemberModifiers LocalReadonly = Local with { IsReadonly = true };

    private readonly HandBuiltModel model = new();
    private readonly ClassType integer;
    private readonly ClassType globals = HandBuiltModel.Class("Globals");
    private readonly Field counter;
    private readonly ClassType host = HandBuiltModel.Class("Host");
    private readonly ClassType bag = HandBuiltModel.Class("Bag");
    private readonly Indexer bagIndexer;
    private readonly ClassType plain = HandBuiltModel.Class("Plain");
    private readonly Indexer plainIndexer;
    private readonly ClassType jar = HandBuiltModel.Class("Jar");
    private readonly Property level;

    public AccessorPromiseFragments()
    {
        integer = model.Type("Int32");
        integer.Modifiers = new MemberModifiers { IsReadonly = true, IsIntrinsic = true };
        counter = Field(globals, "Counter", isStatic: true);
        var bagCell = Field(bag, "cell");
        bagIndexer = Indexer(bag, Local,
            new PropertyAccessor { OwnModifiers = LocalReadonly, Body = Returning(Read(bagCell)) },
            new PropertyAccessor { Body = Writing(bagCell, Value()) });
        var plainCell = Field(plain, "cell");
        plainIndexer = Indexer(plain, Local,
            new PropertyAccessor
            {
                Body = HandBuiltModel.Block(
                    new ExpressionStatement(Assign(Read(plainCell), new BinaryExpression(Node(), Read(plainCell), BinaryOperatorKind.Add, Literal(1L), integer))),
                    new ReturnStatement(Read(plainCell), integer)),
            },
            new PropertyAccessor { Body = Writing(plainCell, Value()) });
        var n = Field(jar, "n");
        level = Property(jar, "Level", Public,
            new PropertyAccessor { OwnModifiers = LocalReadonly, Body = Returning(Read(n)) },
            new PropertyAccessor { OwnModifiers = Local, Body = Writing(n, Value()) });
        PureConstructor(bag);
        PureConstructor(plain);
        PureConstructor(jar);
    }

    public IReadOnlyList<Diagnostic> Analyze(string name)
    {
        return name switch
        {
            "pure-method-reads-a-local-readonly-indexer-of-a-frozen-input" => PureMethodReadsALocalReadonlyIndexerOfAFrozenInput(),
            "pure-method-sets-a-local-indexer-of-a-frozen-input" => PureMethodSetsALocalIndexerOfAFrozenInput(),
            "pure-method-sets-and-reads-a-local-indexer-it-owns" => PureMethodSetsAndReadsALocalIndexerItOwns(),
            "pure-method-reads-a-local-readonly-indexer-through-an-unrestricted-input" => PureMethodReadsALocalReadonlyIndexerThroughAnUnrestrictedInput(),
            "pure-method-reads-a-writing-getter-of-a-frozen-input" => PureMethodReadsAWritingGetterOfAFrozenInput(),
            "pure-method-reads-a-writing-getter-it-owns" => PureMethodReadsAWritingGetterItOwns(),
            "pure-method-reads-a-local-readonly-getter-of-a-frozen-input" => PureMethodReadsALocalReadonlyGetterOfAFrozenInput(),
            "pure-method-reads-a-local-readonly-getter-through-an-unrestricted-input" => PureMethodReadsALocalReadonlyGetterThroughAnUnrestrictedInput(),
            "pure-method-sets-a-local-setter-it-owns" => PureMethodSetsALocalSetterItOwns(),
            "pure-method-sets-a-local-setter-of-a-frozen-input" => PureMethodSetsALocalSetterOfAFrozenInput(),
            "readonly-method-reads-a-local-readonly-getter-through-an-unrestricted-input" => ReadonlyMethodReadsALocalReadonlyGetterThroughAnUnrestrictedInput(),
            "readonly-method-sets-a-local-setter-through-an-unrestricted-input" => ReadonlyMethodSetsALocalSetterThroughAnUnrestrictedInput(),
            "readonly-method-sets-a-local-setter-it-owns" => ReadonlyMethodSetsALocalSetterItOwns(),
            "pure-getter-reads-a-mutable-field" => PureGetterReadsAMutableField(),
            "pure-getter-reads-a-static" => PureGetterReadsAStatic(),
            "local-readonly-getter-writes-its-instance" => LocalReadonlyGetterWritesItsInstance(),
            "local-readonly-getter-of-a-local-property-writes-its-instance" => LocalReadonlyGetterOfALocalPropertyWritesItsInstance(),
            "local-readonly-indexer-getter-writes-its-instance" => LocalReadonlyIndexerGetterWritesItsInstance(),
            "local-setter-writes-a-static" => LocalSetterWritesAStatic(),
            "local-readonly-getter-and-local-setter-keep-their-promises" => LocalReadonlyGetterAndLocalSetterKeepTheirPromises(),
            "local-property-with-a-local-readonly-getter-keeps-its-promise" => LocalPropertyWithALocalReadonlyGetterKeepsItsPromise(),
            "pure-getter-reads-a-readonly-field-its-pure-constructor-set" => PureGetterReadsAReadonlyFieldItsPureConstructorSet(),
            "local-readonly-indexer-getter-and-local-setter-keep-their-promises" => LocalReadonlyIndexerGetterAndLocalSetterKeepTheirPromises(),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private IReadOnlyList<Diagnostic> PureMethodReadsALocalReadonlyIndexerOfAFrozenInput()
    {
        var b = Input("b", bag, OwnershipKind.Frozen);
        return Judge(Caller(PureStatic, Returning(Index(Ref(b), bagIndexer)), b));
    }

    private IReadOnlyList<Diagnostic> PureMethodSetsALocalIndexerOfAFrozenInput()
    {
        var b = Input("b", bag, OwnershipKind.Frozen);
        return Judge(Caller(PureStatic, HandBuiltModel.Block(
            new ExpressionStatement(Assign(Index(Ref(b), bagIndexer), Literal(1L))),
            new ReturnStatement(Literal(0L), integer)), b));
    }

    private IReadOnlyList<Diagnostic> PureMethodSetsAndReadsALocalIndexerItOwns()
    {
        var b = new LocalVariable("b", bag) { Ownership = OwnershipKind.Owned };
        return Judge(Caller(PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(b, New(bag)),
            new ExpressionStatement(Assign(Index(Ref(b), bagIndexer), Literal(1L))),
            new ReturnStatement(Index(Ref(b), bagIndexer), integer))));
    }

    private IReadOnlyList<Diagnostic> PureMethodReadsALocalReadonlyIndexerThroughAnUnrestrictedInput()
    {
        var b = Input("b", bag);
        return Judge(Caller(PureStatic, Returning(Index(Ref(b), bagIndexer)), b));
    }

    private IReadOnlyList<Diagnostic> PureMethodReadsAWritingGetterOfAFrozenInput()
    {
        var p = Input("p", plain, OwnershipKind.Frozen);
        return Judge(Caller(PureStatic, Returning(Index(Ref(p), plainIndexer)), p));
    }

    private IReadOnlyList<Diagnostic> PureMethodReadsAWritingGetterItOwns()
    {
        var p = new LocalVariable("p", plain) { Ownership = OwnershipKind.Owned };
        return Judge(Caller(PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(p, New(plain)),
            new ReturnStatement(Index(Ref(p), plainIndexer), integer))));
    }

    private IReadOnlyList<Diagnostic> PureMethodReadsALocalReadonlyGetterOfAFrozenInput()
    {
        var j = Input("j", jar, OwnershipKind.Frozen);
        return Judge(Caller(PureStatic, Returning(Level(Ref(j))), j));
    }

    private IReadOnlyList<Diagnostic> PureMethodReadsALocalReadonlyGetterThroughAnUnrestrictedInput()
    {
        var j = Input("j", jar);
        return Judge(Caller(PureStatic, Returning(Level(Ref(j))), j));
    }

    private IReadOnlyList<Diagnostic> PureMethodSetsALocalSetterItOwns()
    {
        var j = new LocalVariable("j", jar);
        return Judge(Caller(PureStatic, SettingAFresh(j)));
    }

    private IReadOnlyList<Diagnostic> PureMethodSetsALocalSetterOfAFrozenInput()
    {
        var j = Input("j", jar, OwnershipKind.Frozen);
        return Judge(Caller(PureStatic, SettingThrough(j), j));
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodReadsALocalReadonlyGetterThroughAnUnrestrictedInput()
    {
        var j = Input("j", jar);
        return Judge(Caller(ReadonlyStatic, Returning(Level(Ref(j))), j));
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodSetsALocalSetterThroughAnUnrestrictedInput()
    {
        var j = Input("j", jar);
        return Judge(Caller(ReadonlyStatic, SettingThrough(j), j));
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodSetsALocalSetterItOwns()
    {
        var j = new LocalVariable("j", jar);
        return Judge(Caller(ReadonlyStatic, SettingAFresh(j)));
    }

    private IReadOnlyList<Diagnostic> PureGetterReadsAMutableField()
    {
        var bad = HandBuiltModel.Class("Bad");
        var n = Field(bad, "n");
        var p = Property(bad, "P", Public, new PropertyAccessor { OwnModifiers = Pure, Body = Returning(Read(n, 37)) }, null);
        return Judge(bad, Bodies(bad, p, p.Getter, p.Setter));
    }

    private IReadOnlyList<Diagnostic> PureGetterReadsAStatic()
    {
        var bad = HandBuiltModel.Class("Bad");
        _ = Field(bad, "n");
        var p = Property(bad, "P", Public, new PropertyAccessor { OwnModifiers = Pure, Body = Returning(StaticRead(counter)) }, null);
        return Judge(bad, Bodies(bad, p, p.Getter, p.Setter));
    }

    private IReadOnlyList<Diagnostic> LocalReadonlyGetterWritesItsInstance()
    {
        var bad = HandBuiltModel.Class("Bad");
        var n = Field(bad, "n");
        var p = Property(bad, "P", Public, new PropertyAccessor { OwnModifiers = LocalReadonly, Body = WritingThenReturning(n) }, null);
        return Judge(bad, Bodies(bad, p, p.Getter, p.Setter));
    }

    private IReadOnlyList<Diagnostic> LocalReadonlyGetterOfALocalPropertyWritesItsInstance()
    {
        var bad = HandBuiltModel.Class("Bad");
        var n = Field(bad, "n");
        var p = Property(bad, "P", Local,
            new PropertyAccessor { OwnModifiers = LocalReadonly, Body = WritingThenReturning(n) },
            new PropertyAccessor { Body = Writing(n, Value()) });
        return Judge(bad, Bodies(bad, p, p.Getter, p.Setter));
    }

    private IReadOnlyList<Diagnostic> LocalReadonlyIndexerGetterWritesItsInstance()
    {
        var bad = HandBuiltModel.Class("Bad");
        var n = Field(bad, "n");
        var indexer = Indexer(bad, Public, new PropertyAccessor { OwnModifiers = LocalReadonly, Body = WritingThenReturning(n) }, null);
        return Judge(bad, Bodies(bad, indexer, indexer.Getter, indexer.Setter));
    }

    private IReadOnlyList<Diagnostic> LocalSetterWritesAStatic()
    {
        var bad = HandBuiltModel.Class("Bad");
        var n = Field(bad, "n");
        var p = Property(bad, "P", Public,
            new PropertyAccessor { Body = Returning(Read(n)) },
            new PropertyAccessor { OwnModifiers = Local, Body = HandBuiltModel.Block(new ExpressionStatement(Assign(StaticRead(counter, 37), Value()))) });
        return Judge(bad, Bodies(bad, p, p.Getter, p.Setter));
    }

    private IReadOnlyList<Diagnostic> LocalReadonlyGetterAndLocalSetterKeepTheirPromises()
    {
        var good = HandBuiltModel.Class("Good");
        var n = Field(good, "n");
        var p = Property(good, "P", Public,
            new PropertyAccessor { OwnModifiers = LocalReadonly, Body = Returning(Read(n)) },
            new PropertyAccessor { OwnModifiers = Local, Body = Writing(n, Value()) });
        return Judge(good, Bodies(good, p, p.Getter, p.Setter));
    }

    private IReadOnlyList<Diagnostic> LocalPropertyWithALocalReadonlyGetterKeepsItsPromise()
    {
        var good = HandBuiltModel.Class("Good");
        var n = Field(good, "n");
        var p = Property(good, "P", Local,
            new PropertyAccessor { OwnModifiers = LocalReadonly, Body = Returning(Read(n)) },
            new PropertyAccessor { Body = Writing(n, Value()) });
        return Judge(good, Bodies(good, p, p.Getter, p.Setter));
    }

    private IReadOnlyList<Diagnostic> PureGetterReadsAReadonlyFieldItsPureConstructorSet()
    {
        var good = HandBuiltModel.Class("Good");
        var n = Field(good, "n", isReadonly: true);
        var k = Input("k", integer);
        var constructing = Writing(n, Ref(k));
        var constructor = new Constructor("Good", good, [k.Parameter!], Pure) { DeclaringType = good, Body = constructing };
        good.ConstructorsList.Add(constructor);
        var doubled = new BinaryExpression(Node(), Read(n), BinaryOperatorKind.Multiply, Literal(2L), integer);
        var p = Property(good, "P", Public, new PropertyAccessor { OwnModifiers = Pure, Body = Returning(doubled) }, null);
        return Judge(good, [new Root(constructing, good, constructor), .. Bodies(good, p, p.Getter, p.Setter)]);
    }

    private IReadOnlyList<Diagnostic> LocalReadonlyIndexerGetterAndLocalSetterKeepTheirPromises()
    {
        var good = HandBuiltModel.Class("Good");
        var n = Field(good, "n");
        var indexer = Indexer(good, Public,
            new PropertyAccessor { OwnModifiers = LocalReadonly, Body = Returning(Read(n)) },
            new PropertyAccessor { OwnModifiers = Local, Body = Writing(n, Value()) });
        return Judge(good, Bodies(good, indexer, indexer.Getter, indexer.Setter));
    }

    private Block SettingAFresh(LocalVariable j)
    {
        return HandBuiltModel.Block(
            new LocalDeclarationStatement(j, New(jar)),
            new ExpressionStatement(Assign(Level(Ref(j)), Literal(2L))),
            new ReturnStatement(Level(Ref(j)), integer));
    }

    private Block SettingThrough(LocalVariable j)
    {
        return HandBuiltModel.Block(
            new ExpressionStatement(Assign(Level(Ref(j)), Literal(2L))),
            new ReturnStatement(Literal(0L), integer));
    }

    private Root Caller(MemberModifiers modifiers, Block body, params LocalVariable[] inputs)
    {
        var method = new Method("M", integer, [.. inputs.Select(input => input.Parameter!)], modifiers) { DeclaringType = host, Body = body };
        host.MethodsList.Add(method);
        return new Root(body, host, method);
    }

    private IReadOnlyList<Diagnostic> Judge(Root root)
    {
        return Judge(null, [root]);
    }

    private IReadOnlyList<Diagnostic> Judge(MistType? declared, IReadOnlyList<Root> roots)
    {
        var types = new List<MistType> { globals, bag, plain, jar, host };

        if(declared is not null)
        {
            types.Add(declared);
        }

        var reported = new List<Diagnostic>();

        foreach(var root in roots)
        {
            reported.AddRange(model.Analyze(new PurityAnalyzer(), root.Body, types, root.Owner, root.Member));
            reported.AddRange(model.Analyze(new ReadonlyAnalyzer(), root.Body, types, root.Owner, root.Member));
        }

        return reported;
    }

    private static List<Root> Bodies(MistType owner, object member, PropertyAccessor? getter, PropertyAccessor? setter)
    {
        var roots = new List<Root>();

        foreach(var accessor in new[] { getter, setter })
        {
            if(accessor?.Body is { } body)
            {
                roots.Add(new Root(body, owner, member));
            }
        }

        return roots;
    }

    private Field Field(ConcreteType owner, string name, bool isStatic = false, bool isReadonly = false)
    {
        var field = new Field(name, integer, OwnershipKind.Unrestricted, Public with { IsStatic = isStatic, IsReadonly = isReadonly }) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }

    private Indexer Indexer(ConcreteType owner, MemberModifiers modifiers, PropertyAccessor? getter, PropertyAccessor? setter)
    {
        var indexer = new Indexer(integer, [new Parameter("i", integer)], modifiers) { DeclaringType = owner, Getter = getter, Setter = setter };
        owner.IndexersList.Add(indexer);
        return indexer;
    }

    private Property Property(ConcreteType owner, string name, MemberModifiers modifiers, PropertyAccessor? getter, PropertyAccessor? setter)
    {
        var property = new Property(name, integer, modifiers) { DeclaringType = owner, Getter = getter, Setter = setter };
        owner.PropertiesList.Add(property);
        return property;
    }

    private static void PureConstructor(ConcreteType owner)
    {
        owner.ConstructorsList.Add(new Constructor(owner.Name, owner, [], Pure) { DeclaringType = owner, Body = HandBuiltModel.Block() });
    }

    private static NewExpression New(ConcreteType type)
    {
        return new NewExpression(Node(), type, [], type.ConstructorsList[0], [], []);
    }

    private IndexExpression Index(Expression receiver, Indexer indexer)
    {
        return new IndexExpression(Node(37), receiver, Literal(0L), integer) { Indexer = indexer };
    }

    private MemberAccessExpression Level(Expression receiver)
    {
        return new MemberAccessExpression(Node(37), receiver, "Level", integer, member: new PropertyResult(level, jar, []));
    }

    private static FieldReferenceExpression Read(Field field, int offset = 0)
    {
        return new FieldReferenceExpression(Node(offset), field);
    }

    private MemberAccessExpression StaticRead(Field field, int offset = 0)
    {
        var owner = field.DeclaringType!;
        return new MemberAccessExpression(Node(offset), new TypeReferenceExpression(Node(), owner), field.Name, integer, member: new FieldResult(field, owner, []));
    }

    private static Block Writing(Field field, Expression value)
    {
        return HandBuiltModel.Block(new ExpressionStatement(Assign(Read(field), value)));
    }

    private Block WritingThenReturning(Field field)
    {
        return HandBuiltModel.Block(
            new ExpressionStatement(Assign(Read(field, 37), Literal(1L))),
            new ReturnStatement(Read(field), integer));
    }

    private static AssignmentExpression Assign(Expression target, Expression value)
    {
        return new AssignmentExpression(Node(), target, value, target.Type);
    }

    private LocalReferenceExpression Value()
    {
        return new LocalReferenceExpression(Node(), new LocalVariable("value", integer));
    }

    private static LocalReferenceExpression Ref(LocalVariable local)
    {
        return new LocalReferenceExpression(Node(), local);
    }

    private LiteralExpression Literal(long value)
    {
        return HandBuiltModel.Literal(integer, value);
    }

    private static Block Returning(Expression value)
    {
        return HandBuiltModel.Block(new ReturnStatement(value, value.Type));
    }

    private static LocalVariable Input(string name, IMistType type, OwnershipKind ownership = OwnershipKind.Unrestricted)
    {
        return new LocalVariable(name, type)
        {
            Ownership = ownership,
            Parameter = new Parameter(name, type, new ParameterModifier(false, false, ownership)),
        };
    }

    private static SyntaxNode Node(int offset = 0)
    {
        return HandBuiltModel.Node(offset);
    }

    private readonly record struct Root(object Body, MistType Owner, object Member);
}
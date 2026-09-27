using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Helpers;

internal sealed class ReadonlyContextFragments
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MemberModifiers Pure = Public with { IsPure = true, IsReadonly = true };
    private static readonly MemberModifiers Local = Public with { IsLocal = true };
    private static readonly MemberModifiers LocalReadonly = Local with { IsReadonly = true };
    private static readonly MemberModifiers Readonly = Public with { IsReadonly = true };
    private static readonly MemberModifiers Static = Public with { IsStatic = true };

    private readonly HandBuiltModel model = new();
    private readonly ClassType integer;
    private readonly ClassType boolean;
    private readonly ClassType unit;
    private readonly ConstructedType integers;
    private readonly Operator plus;
    private readonly ClassType host = HandBuiltModel.Class("Host");
    private readonly Field n;
    private readonly ClassType globals = HandBuiltModel.Class("Globals");
    private readonly Field counter;
    private readonly ClassType bag = HandBuiltModel.Class("Bag");
    private readonly Field count;
    private readonly Constructor bagConstructor;
    private readonly Method add;
    private readonly Method size;
    private readonly Method poke;
    private readonly ClassType cell = HandBuiltModel.Class("Cell");
    private readonly Property level;

    public ReadonlyContextFragments()
    {
        integer = Primitive("Int32");
        _ = Primitive("String");
        boolean = Primitive("Boolean");
        unit = Primitive("Void");
        integers = model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [integer, model.Interner.GetOrCreateUnknownValue(integer)]);
        plus = new Operator(OperatorKind.Plus, integer, [new Parameter("a", integer), new Parameter("b", integer)], Pure with { IsStatic = true, IsIntrinsic = true })
        {
            DeclaringType = integer,
        };
        integer.OperatorsList.Add(plus);
        n = Field(host, "n", integer, Public);
        counter = Field(globals, "Counter", integer, Static);
        count = Field(bag, "Count", integer, Public);
        bagConstructor = Constructor(bag, Pure);
        var amount = Input("x", integer);
        add = Method(bag, "Add", unit, Local, HandBuiltModel.Block(Set(Bare(count), Sum(Bare(count), Lit(1)))), amount);
        size = Method(bag, "Size", integer, LocalReadonly, Returning(Bare(count)));
        _ = Method(bag, "Peek", integer, Readonly, Returning(Sum(Bare(count), StaticRead(counter))));
        poke = Method(bag, "Poke", unit, Public, HandBuiltModel.Block(Set(Bare(count), Lit(0)), Set(StaticRead(counter), Lit(1))));
        level = new Property("Level", integer, Public) { DeclaringType = cell, Getter = new PropertyAccessor { IsAuto = true }, Setter = new PropertyAccessor { IsAuto = true } };
        cell.PropertiesList.Add(level);
    }

    public IReadOnlyList<Diagnostic> Analyze(string name)
    {
        return name switch
        {
            "readonly-method-writes-through-a-ref-local-aliasing-a-field" => ReadonlyMethodWritesThroughARefLocalAliasingAField(),
            "readonly-method-fills-an-array-it-allocated" => ReadonlyMethodFillsAnArrayItAllocated(),
            "readonly-method-runs-an-auto-setter-through-an-object-initializer" => ReadonlyMethodRunsAnAutoSetterThroughAnObjectInitializer(),
            "readonly-method-sets-an-auto-property-on-what-it-owns" => ReadonlyMethodSetsAnAutoPropertyOnWhatItOwns(),
            "readonly-method-matches-an-auto-property-in-a-pattern" => ReadonlyMethodMatchesAnAutoPropertyInAPattern(),
            "readonly-method-reads-an-extension-getter-that-writes-a-static" => ReadonlyMethodReadsAnExtensionGetter("Ext", _ => HandBuiltModel.Block(Set(StaticRead(counter), Lit(1)), Return(StaticRead(counter)))),
            "readonly-method-reads-an-extension-getter-that-only-reads" => ReadonlyMethodReadsAnExtensionGetter("Plain", self => Returning(Access(Ref(self), count))),
            "readonly-method-calls-an-implicit-conversion-that-writes-a-static" => ReadonlyMethodCallsAnImplicitConversion(writes: true),
            "readonly-method-calls-an-explicit-conversion-that-writes-nothing" => ReadonlyMethodCallsAnExplicitConversionThatWritesNothing(),
            "readonly-method-calls-an-implicit-conversion-that-writes-nothing" => ReadonlyMethodCallsAnImplicitConversion(writes: false),
            "readonly-method-clones-through-a-clone-that-writes-a-static" => ReadonlyMethodClones(chatty: true),
            "readonly-method-clones-through-a-clone-that-reads-only-its-source" => ReadonlyMethodClones(chatty: false),
            "constructor-assigns-its-own-readonly-field" => ConstructorAssignsItsOwnReadonlyField(throughThis: false),
            "constructor-assigns-its-own-readonly-field-through-this" => ConstructorAssignsItsOwnReadonlyField(throughThis: true),
            "readonly-method-sets-a-property-whose-setter-writes-a-static-on-what-it-owns" => ReadonlyMethodSetsAPropertyWhoseSetterWritesAStaticOnWhatItOwns(),
            "readonly-method-sets-an-indexer-whose-setter-writes-a-static-on-what-it-owns" => ReadonlyMethodSetsAnIndexerOnWhatItOwns(loud: true),
            "readonly-method-sets-a-local-indexer-on-a-fresh-local" => ReadonlyMethodSetsAnIndexerOnWhatItOwns(loud: false),
            "readonly-method-writes-and-locally-calls-an-owned-local" => ReadonlyMethodWritesAndLocallyCallsAFreshLocal(owned: true),
            "readonly-method-writes-and-locally-calls-a-fresh-local" => ReadonlyMethodWritesAndLocallyCallsAFreshLocal(owned: false),
            "readonly-method-calls-a-local-readonly-member-on-a-fresh-local" => ReadonlyMethodCallsALocalReadonlyMemberOnAFreshLocal(),
            "readonly-method-writes-and-locally-calls-an-owned-parameter" => ReadonlyMethodWritesAndLocallyCallsAnOwnedParameter(),
            "readonly-method-writes-a-field-of-an-unrestricted-parameter" => ReadonlyMethodWritesAFieldOfAParameter(OwnershipKind.Unrestricted),
            "readonly-method-writes-a-field-of-a-frozen-parameter" => ReadonlyMethodWritesAFieldOfAParameter(OwnershipKind.Frozen),
            "readonly-method-calls-an-unmarked-member-on-a-fresh-local" => ReadonlyMethodCallsAnUnmarkedMemberOnAFreshLocal(),
            "readonly-method-calls-a-local-member-on-an-unrestricted-parameter" => ReadonlyMethodCallsOnAnUnrestrictedParameter(add, Lit(1)),
            "readonly-method-calls-an-unmarked-member-on-an-unrestricted-parameter" => ReadonlyMethodCallsOnAnUnrestrictedParameter(poke),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodWritesThroughARefLocalAliasingAField()
    {
        var r = new LocalVariable("r", integer) { IsRef = true };
        var twist = Method(host, "Twist", integer, Readonly, HandBuiltModel.Block(Declare(r, Bare(n)), Set(Ref(r), Lit(7)), Return(Bare(n))));
        return Judge(twist);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodFillsAnArrayItAllocated()
    {
        var xs = new LocalVariable("xs", integers);
        var elements = new ArrayLiteralExpression(Node(), integer, [Lit(0), Lit(0)], integers);
        var build = Method(host, "Build", integer, Readonly, HandBuiltModel.Block(Declare(xs, elements), Set(Index(xs, null), Lit(1)), Return(Index(xs, null))));
        return Judge(build);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodRunsAnAutoSetterThroughAnObjectInitializer()
    {
        var c = new LocalVariable("c", cell);
        var entry = new ObjectInitializerAssignment(Node(), "Level", Lit(3), integer, member: new PropertyResult(level, cell, []));
        var build = Method(host, "Build", integer, Readonly, HandBuiltModel.Block(
            Declare(c, new NewExpression(Node(), cell, [], null, [entry], null)),
            Return(AccessProperty(Ref(c), level))));
        return Judge(build);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodSetsAnAutoPropertyOnWhatItOwns()
    {
        var c = new LocalVariable("c", cell);
        var build = Method(host, "Build", integer, Readonly, HandBuiltModel.Block(
            Declare(c, New(cell, null)),
            Set(AccessProperty(Ref(c), level), Lit(2)),
            Return(AccessProperty(Ref(c), level))));
        return Judge(build);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodMatchesAnAutoPropertyInAPattern()
    {
        var c = Input("c", cell);
        var pattern = new PropertyPattern(Node(), cell,
            [new PropertySubpattern(Node(), "Level", new ComparisonPattern(Node(), integer, ComparisonKind.Equal, Lit(1)), integer, new PropertyResult(level, cell, []))]);
        var m = Method(host, "M", integer, Readonly, HandBuiltModel.Block(Statement(new IsPatternExpression(Node(), Ref(c), pattern, boolean)), Return(Lit(0))), c);
        return Judge(m);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodReadsAnExtensionGetter(string name, Func<LocalVariable, Block> getter)
    {
        var self = new LocalVariable("self", bag);
        var extended = new Property(name, integer, Public) { Getter = new PropertyAccessor { Body = getter(self) } };
        var b = Input("b", bag);
        var m = Method(host, "M", integer, Readonly, Returning(new ExtensionPropertyAccessExpression(Node(), Ref(b), extended, "BagExtensions", integer)), b);
        return Judge(m);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodCallsAnImplicitConversion(bool writes)
    {
        var wrapped = HandBuiltModel.Class(writes ? "Wrapped" : "Calm");
        var v = Field(wrapped, "V", integer, Public);
        var source = Input("w", wrapped);
        var toInt = new CastOperator(integer, source.Parameter!, true, Static)
        {
            DeclaringType = wrapped,
            Body = writes
                ? HandBuiltModel.Block(Set(StaticRead(counter), Lit(10)), Return(Access(Ref(source), v)))
                : Returning(Access(Ref(source), v)),
        };
        wrapped.CastOperatorsList.Add(toInt);
        var w = Input("w", wrapped);
        var k = new LocalVariable("k", integer);
        var m = Method(host, "M", integer, Readonly, HandBuiltModel.Block(
            Declare(k, new ImplicitConversionExpression(Node(), Ref(w), integer, [new ConversionFunction(toInt, wrapped, integer)])),
            Return(Ref(k))), w);
        return Judge(m);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodCallsAnExplicitConversionThatWritesNothing()
    {
        var calm = HandBuiltModel.Class("Calm");
        var v = Field(calm, "V", integer, Public);
        var source = Input("b", bag);
        var made = new LocalVariable("c", calm);
        var toCalm = new CastOperator(calm, source.Parameter!, false, Static)
        {
            DeclaringType = calm,
            Body = HandBuiltModel.Block(Declare(made, New(calm, null)), Set(Access(Ref(made), v), Access(Ref(source), count)), Return(Ref(made))),
        };
        calm.CastOperatorsList.Add(toCalm);
        var b = Input("b", bag);
        var c = new LocalVariable("c", calm);
        var m = Method(host, "M", integer, Readonly, HandBuiltModel.Block(
            Declare(c, new CastExpression(Node(), Ref(b), calm, [new ConversionFunction(toCalm, bag, calm)], calm)),
            Return(Access(Ref(c), v))), b);
        return Judge(m);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodClones(bool chatty)
    {
        var cloned = HandBuiltModel.Class(chatty ? "Chatty" : "Quiet");
        var stored = Field(cloned, "N", integer, Public);
        var made = Constructor(cloned, Pure);
        var copy = new LocalVariable("c", cloned);
        var cloning = new List<Statement>();

        if(chatty)
        {
            var tally = Field(cloned, "Made", integer, Static);
            cloning.Add(Set(StaticRead(tally), Sum(StaticRead(tally), Lit(1))));
        }

        cloning.Add(Declare(copy, New(cloned, made)));
        cloning.Add(Set(Access(Ref(copy), stored), Bare(stored)));
        cloning.Add(Return(Ref(copy)));
        cloned.MethodsList.Add(new Method("Clone", cloned, [], Public)
        {
            DeclaringType = cloned,
            ReturnOwnership = OwnershipKind.Owned,
            Body = HandBuiltModel.Block([.. cloning]),
        });
        var b = Input("b", cloned);
        var c = new LocalVariable("c", cloned) { Ownership = OwnershipKind.Owned };
        var take = Method(host, "Take", integer, Readonly, HandBuiltModel.Block(Declare(c, new CloneExpression(Node(), Ref(b))), Return(Access(Ref(c), stored))), b);
        return Judge(take);
    }

    private IReadOnlyList<Diagnostic> ConstructorAssignsItsOwnReadonlyField(bool throughThis)
    {
        var twin = HandBuiltModel.Class("Twin");
        var k = Field(twin, "K", integer, Readonly);
        var given = Input("k", integer);
        Expression target = throughThis ? Access(new ThisExpression(Node(), twin), k) : Bare(k);
        var constructor = Constructor(twin, Public, HandBuiltModel.Block(Set(target, Ref(given))), given);
        return model.Analyze(new ReadonlyAnalyzer(), constructor.Body, [], twin, constructor);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodSetsAPropertyWhoseSetterWritesAStaticOnWhatItOwns()
    {
        var loud = HandBuiltModel.Class("Loud");
        var lvl = Field(loud, "lvl", integer, Public);
        var value = new LocalVariable("value", integer);
        var loudLevel = new Property("Level", integer, Public)
        {
            DeclaringType = loud,
            Getter = new PropertyAccessor { Body = Returning(Bare(lvl)) },
            Setter = new PropertyAccessor { Body = HandBuiltModel.Block(Set(Bare(lvl), Ref(value)), Set(StaticRead(counter), Lit(1))) },
        };
        loud.PropertiesList.Add(loudLevel);
        var l = new LocalVariable("l", loud);
        var build = Method(host, "Build", integer, Readonly, HandBuiltModel.Block(
            Declare(l, New(loud, null)),
            Set(AccessProperty(Ref(l), loudLevel), Lit(2)),
            Return(AccessProperty(Ref(l), loudLevel))));
        return Judge(build);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodSetsAnIndexerOnWhatItOwns(bool loud)
    {
        var grid = HandBuiltModel.Class(loud ? "LoudGrid" : "Grid");
        var stored = Field(grid, "cell", integer, Public);
        var value = new LocalVariable("value", integer);
        var setting = new List<Statement> { Set(Bare(stored), Ref(value)) };

        if(loud)
        {
            setting.Add(Set(StaticRead(counter), Lit(1)));
        }

        var indexer = new Indexer(integer, [new Parameter("i", integer)], Public)
        {
            DeclaringType = grid,
            Getter = new PropertyAccessor { Body = Returning(Bare(stored)) },
            Setter = new PropertyAccessor { Body = HandBuiltModel.Block([.. setting]) },
        };
        grid.IndexersList.Add(indexer);
        var g = new LocalVariable("g", grid);
        var build = Method(host, "Build", integer, Readonly, HandBuiltModel.Block(Declare(g, New(grid, null)), Set(Index(g, indexer), Lit(1)), Return(Index(g, indexer))));
        return Judge(build);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodWritesAndLocallyCallsAFreshLocal(bool owned)
    {
        var b = new LocalVariable("b", bag) { Ownership = owned ? OwnershipKind.Owned : OwnershipKind.Unrestricted };
        var build = Method(host, "Build", integer, Readonly, HandBuiltModel.Block(
            FreshBag(b),
            Statement(Call(add, Ref(b), Lit(1))),
            Set(Access(Ref(b), count), Lit(5)),
            Return(Access(Ref(b), count))));
        return Judge(build);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodCallsALocalReadonlyMemberOnAFreshLocal()
    {
        var b = new LocalVariable("b", bag);
        var build = Method(host, "Build", integer, Readonly, HandBuiltModel.Block(FreshBag(b), Return(Call(size, Ref(b)))));
        return Judge(build);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodWritesAndLocallyCallsAnOwnedParameter()
    {
        var b = Input("b", bag, OwnershipKind.Owned);
        var build = Method(host, "Build", integer, Readonly, HandBuiltModel.Block(
            Statement(Call(add, Ref(b), Lit(1))),
            Set(Access(Ref(b), count), Lit(5)),
            Return(Access(Ref(b), count))), b);
        return Judge(build);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodWritesAFieldOfAParameter(OwnershipKind ownership)
    {
        var x = Input("x", bag, ownership);
        var poking = Method(host, "Poke", integer, Readonly, HandBuiltModel.Block(Set(Access(Ref(x), count), Lit(1)), Return(Lit(1))), x);
        return Judge(poking);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodCallsAnUnmarkedMemberOnAFreshLocal()
    {
        var b = new LocalVariable("b", bag);
        var poking = Method(host, "Poke", integer, Readonly, HandBuiltModel.Block(FreshBag(b), Statement(Call(poke, Ref(b))), Return(Lit(1))));
        return Judge(poking);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodCallsOnAnUnrestrictedParameter(Method callee, params Expression[] arguments)
    {
        var x = Input("x", bag);
        var poking = Method(host, "Poke", integer, Readonly, HandBuiltModel.Block(Statement(Call(callee, Ref(x), arguments)), Return(Lit(1))), x);
        return Judge(poking);
    }

    private IReadOnlyList<Diagnostic> Judge(Method member)
    {
        return model.Analyze(new ReadonlyAnalyzer(), member.Body, [], member.DeclaringType, member);
    }

    private ClassType Primitive(string name)
    {
        var type = model.Type(name);
        type.Modifiers = new MemberModifiers { IsReadonly = true, IsIntrinsic = true };
        return type;
    }

    private static Field Field(ConcreteType owner, string name, IMistType type, MemberModifiers modifiers)
    {
        var field = new Field(name, type, OwnershipKind.Unrestricted, modifiers) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }

    private static Constructor Constructor(ConcreteType owner, MemberModifiers modifiers, Block? body = null, params LocalVariable[] inputs)
    {
        var constructor = new Constructor(owner.Name, owner, [.. inputs.Select(input => input.Parameter!)], modifiers)
        {
            DeclaringType = owner,
            Body = body ?? HandBuiltModel.Block(),
        };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private static Method Method(MemberType owner, string name, IMistType returns, MemberModifiers modifiers, Block? body, params LocalVariable[] inputs)
    {
        var method = new Method(name, returns, [.. inputs.Select(input => input.Parameter!)], modifiers) { DeclaringType = owner, Body = body };
        owner.MethodsList.Add(method);
        return method;
    }

    private static LocalVariable Input(string name, IMistType type, OwnershipKind ownership = OwnershipKind.Unrestricted)
    {
        return new LocalVariable(name, type) { Ownership = ownership, Parameter = new Parameter(name, type, new ParameterModifier(false, false, ownership)) };
    }

    private static SyntaxNode Node()
    {
        return HandBuiltModel.Node();
    }

    private LiteralExpression Lit(long value)
    {
        return HandBuiltModel.Literal(integer, value);
    }

    private static LocalReferenceExpression Ref(LocalVariable local)
    {
        return new LocalReferenceExpression(Node(), local);
    }

    private static FieldReferenceExpression Bare(Field field)
    {
        return new FieldReferenceExpression(Node(), field);
    }

    private static MemberAccessExpression Access(Expression receiver, Field field)
    {
        return new MemberAccessExpression(Node(), receiver, field.Name, field.Type, member: new FieldResult(field, receiver.Type, []));
    }

    private static MemberAccessExpression AccessProperty(Expression receiver, Property property)
    {
        return new MemberAccessExpression(Node(), receiver, property.Name, property.Type, member: new PropertyResult(property, receiver.Type, []));
    }

    private static MemberAccessExpression StaticRead(Field field)
    {
        var owner = field.DeclaringType!;
        return new MemberAccessExpression(Node(), new TypeReferenceExpression(Node(), owner), field.Name, field.Type, member: new FieldResult(field, owner, []));
    }

    private static ExpressionStatement Set(Expression target, Expression value)
    {
        return new ExpressionStatement(new AssignmentExpression(Node(), target, value, target.Type));
    }

    private static ExpressionStatement Statement(Expression expression)
    {
        return new ExpressionStatement(expression);
    }

    private static LocalDeclarationStatement Declare(LocalVariable local, Expression initializer)
    {
        return new LocalDeclarationStatement(local, initializer);
    }

    private LocalDeclarationStatement FreshBag(LocalVariable local)
    {
        return Declare(local, New(bag, bagConstructor));
    }

    private static ReturnStatement Return(Expression value)
    {
        return new ReturnStatement(value, value.Type);
    }

    private static Block Returning(Expression value)
    {
        return HandBuiltModel.Block(Return(value));
    }

    private BinaryExpression Sum(Expression left, Expression right)
    {
        return new BinaryExpression(Node(), left, BinaryOperatorKind.Add, right, integer) { Operator = plus };
    }

    private static NewExpression New(ConcreteType type, Constructor? constructor, params Expression[] arguments)
    {
        return new NewExpression(Node(), type, arguments, constructor, [], constructor is null ? null : [.. constructor.ParametersList.Select(parameter => parameter.Type)]);
    }

    private static InvocationExpression Call(Method method, Expression receiver, params Expression[] arguments)
    {
        var callee = new MemberAccessExpression(Node(), receiver, method.Name, method.ReturnType);
        return new InvocationExpression(Node(), callee, arguments, method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    private IndexExpression Index(LocalVariable receiver, Indexer? indexer)
    {
        return new IndexExpression(Node(), Ref(receiver), Lit(0), integer) { Indexer = indexer };
    }
}
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis;
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

internal sealed partial class PurityFragments
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MemberModifiers Pure = Public with { IsPure = true, IsReadonly = true };
    private static readonly MemberModifiers PureStatic = Pure with { IsStatic = true };
    private static readonly MemberModifiers Local = Public with { IsLocal = true };
    private static readonly MemberModifiers LocalReadonly = Local with { IsReadonly = true };
    private static readonly MemberModifiers Static = Public with { IsStatic = true };
    private static readonly MemberModifiers ReadonlyStatic = Static with { IsReadonly = true };

    private readonly HandBuiltModel model = new();
    private readonly ClassType integer;
    private readonly ClassType text;
    private readonly ClassType boolean;
    private readonly ClassType unit;
    private readonly ClassType probe = HandBuiltModel.Class("Probe");
    private readonly ClassType clock = HandBuiltModel.Class("Clock");
    private readonly Field ticks;
    private readonly ClassType box = HandBuiltModel.Class("Box");
    private readonly Field n;
    private readonly ClassType bag = HandBuiltModel.Class("Bag");
    private readonly Field count;
    private readonly StructType cell = HandBuiltModel.Struct("Cell");
    private readonly Field inner;
    private readonly ClassType handle = HandBuiltModel.Class("Handle");
    private readonly Field fd;

    public PurityFragments()
    {
        integer = Primitive("Int32");
        text = Primitive("String");
        boolean = Primitive("Boolean");
        unit = Primitive("Void");
        ticks = Field(clock, "Ticks", integer, isStatic: true);
        _ = Field(clock, "Limit", integer, isStatic: true, isReadonly: true);
        n = Field(box, "N", integer);
        _ = PureConstructor(box);
        count = Field(bag, "Count", integer);
        _ = PureConstructor(bag);
        var item = Input("item", integer);
        _ = Method(bag, "Add", unit, Local, HandBuiltModel.Block(new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), count), Ref(item)))), item);
        _ = Method(bag, "Size", integer, LocalReadonly, Returning(new FieldReferenceExpression(Node(), count)));
        _ = Method(bag, "Poke", unit, Public, HandBuiltModel.Block(WriteStatic(ticks)));
        inner = Field(cell, "Inner", box, ownership: OwnershipKind.Owned);
        _ = PureConstructor(cell);
        handle.Modifiers = Public with { Discipline = SubstructuralDiscipline.Linear };
        fd = Field(handle, "Fd", integer);
        var opened = Input("fd", integer);
        handle.ConstructorsList.Add(new Constructor("Handle", handle, [opened.Parameter!], Public)
        {
            DeclaringType = handle,
            Body = HandBuiltModel.Block(new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), fd), Ref(opened)))),
        });
        _ = Method(handle, "Drop", unit, Public, HandBuiltModel.Block(WriteStatic(ticks)));
    }

    public IReadOnlyList<Diagnostic> Analyze(string name)
    {
        return name switch
        {
            "abstract-auto-getter-answered-by-an-impure-implementation" => AbstractAutoGetterAnsweredByAnImpureImplementation(),
            "abstract-default-body-replaced-by-an-impure-implementation" => AbstractDefaultBodyReplacedByAnImpureImplementation(),
            "abstract-pure-indexer-implemented-impurely" => AbstractPureIndexerImplementedImpurely(),
            "clr-delegate-to-pure-func" => ClrDelegateToPureFunc(),
            "constructor-assigns-another-instances-readonly-field" => ConstructorAssignsAnotherInstancesReadonlyField(),
            "constructor-of-another-type-assigns-a-readonly-field" => ConstructorOfAnotherTypeAssignsAReadonlyField(),
            "default-entry-not-consistent" => DefaultEntryNotConsistent(),
            "default-initializer-not-consistent" => DefaultInitializerNotConsistent(),
            "frozen-method-group-to-pure-func" => FrozenMethodGroupToPureFunc(),
            "generic-type-test-instantiated-with-a-pure-function-type" => GenericTypeTestInstantiatedWithAPureFunctionType(),
            "implicit-clone-of-a-frozen-input-through-an-impure-clone" => ImplicitCloneOfAFrozenInputThroughAnImpureClone(),
            "impure-method-group-to-pure-func" => ImpureMethodGroupToPureFunc(),
            "local-abstract-member-implemented-impurely" => LocalAbstractMemberImplementedImpurely(),
            "local-method-reads-through-an-argument" => LocalMethodReadsThroughAnArgument(),
            "local-readonly-method-writes-its-instance" => LocalReadonlyMethodWritesItsInstance(),
            "local-type-member-reads-an-argument" => LocalTypeMemberReadsAnArgument(),
            "owned-return-of-this" => OwnedReturnOfThis(),
            "owned-slot-answered-by-an-unowned-return" => OwnedSlotAnsweredByAnUnownedReturn(),
            "plain-func-to-pure-func" => PlainFuncToPureFunc(),
            "pure-abstract-member-implemented-impurely" => PureAbstractMemberImplementedImpurely(),
            "pure-constructor-chains-to-an-unmarked-one" => PureConstructorChainsToAnUnmarkedOne(),
            "pure-function-type-tested-at-runtime" => PureFunctionTypeTestedAtRuntime(),
            "pure-lambda-writes-a-captured-local" => PureLambdaWritesACapturedLocal(),
            "pure-method-allocates-through-an-implicit-constructor-with-an-impure-initializer" => PureMethodAllocatesThroughAnImplicitConstructorWithAnImpureInitializer(),
            "pure-method-awaits-a-task" => PureMethodAwaitsATask(),
            "pure-method-calls-an-impure-conversion" => PureMethodCallsAnImpureConversion(),
            "pure-method-calls-an-impure-operator" => PureMethodCallsAnImpureOperator(),
            "pure-method-calls-an-unmarked-method" => PureMethodCallsAnUnmarkedMethod(),
            "pure-method-calls-console" => PureMethodCallsConsole(),
            "pure-method-holds-a-linear-with-an-impure-drop" => PureMethodHoldsALinearWithAnImpureDrop(),
            "pure-method-invokes-a-plain-function-value" => PureMethodInvokesAPlainFunctionValue(),
            "pure-method-iterates-an-impure-collection" => PureMethodIteratesAnImpureCollection(),
            "pure-method-launders-a-write-through-nested-lambdas" => PureMethodLaundersAWriteThroughNestedLambdas(),
            "pure-method-reads-a-mutable-field" => PureMethodReadsAMutableField(),
            "pure-method-reads-a-span-parameter" => PureMethodReadsASpanParameter(),
            "pure-method-reads-an-impure-indexer" => PureMethodReadsAnImpureIndexer(),
            "pure-method-reads-the-count-of-an-unrestricted-list" => PureMethodReadsTheCountOfAnUnrestrictedList(),
            "pure-method-runs-an-async-block" => PureMethodRunsAnAsyncBlock(),
            "pure-method-stores-a-frozen-reference" => PureMethodStoresAFrozenReference(),
            "pure-method-writes-a-span-parameter" => PureMethodWritesASpanParameter(),
            "pure-method-writes-a-static" => PureMethodWritesAStatic(),
            "pure-method-writes-an-element-of-a-frozen-list" => PureMethodWritesAnElementOfAFrozenList(),
            "pure-type-member-writes" => PureTypeMemberWrites(),
            "readonly-field-passed-by-ref" => ReadonlyFieldPassedByRef(),
            "readonly-method-calls-a-mutable-conversion-operator" => ReadonlyMethodCallsAMutableConversionOperator(),
            "readonly-method-iterates-an-impure-collection" => ReadonlyMethodIteratesAnImpureCollection(),
            "readonly-method-reads-a-mutable-getter-in-a-property-pattern" => ReadonlyMethodReadsAMutableGetterInAPropertyPattern(),
            "readonly-method-runs-a-mutable-setter-through-an-object-initializer" => ReadonlyMethodRunsAMutableSetterThroughAnObjectInitializer(),
            "readonly-method-writes-an-element-of-an-array-parameter" => ReadonlyMethodWritesAnElementOfAnArrayParameter(),
            "readonly-method-writes-through-a-ref-parameter" => ReadonlyMethodWritesThroughARefParameter(),
            "readonly-type-settable-property-read-through-an-unrestricted-reference" => ReadonlyTypeSettablePropertyReadThroughAnUnrestrictedReference(),
            "static-initializer-lambda-converting-to-a-pure-function-type" => StaticInitializerLambdaConvertingToAPureFunctionType(),
            "struct-local-member-writes-through-an-owned-field-of-a-copy" => StructLocalMemberWritesThroughAnOwnedFieldOfACopy(),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private IReadOnlyList<Diagnostic> AbstractAutoGetterAnsweredByAnImpureImplementation()
    {
        var shape = Contract("IShape");
        var area = new Property("Area", integer, Public) { DeclaringType = shape, Getter = new PropertyAccessor() };
        shape.PropertiesList.Add(area);
        var square = Implementer("Sq", shape);
        square.PropertiesList.Add(new Property("Area", integer, Public)
        {
            DeclaringType = square,
            Getter = new PropertyAccessor { Body = HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(StaticRead(ticks), integer)) },
        });
        var s = Input("s", shape);
        var measure = Method(probe, "Measure", integer, PureStatic,
            Returning(new MemberAccessExpression(Node(37), Ref(s), "Area", integer, member: new PropertyResult(area, shape, []))), s);
        return Judge(new PurityAnalyzer(), measure, shape, square);
    }

    private IReadOnlyList<Diagnostic> AbstractDefaultBodyReplacedByAnImpureImplementation()
    {
        var shape = Contract("IShape");
        var slot = Method(shape, "Area", integer, Public, Returning(Literal(integer, 1L)));
        var square = Implementer("Sq", shape);
        _ = Method(square, "Area", integer, Public, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(StaticRead(ticks), integer)));
        var s = Input("s", shape);
        var measure = Method(probe, "Measure", integer, PureStatic, Returning(Call(slot, Ref(s), 37)), s);
        return Judge(new PurityAnalyzer(), measure, shape, square);
    }

    private IReadOnlyList<Diagnostic> FrozenMethodGroupToPureFunc()
    {
        var cold = HandBuiltModel.Class("Cold");
        var held = Field(cold, "Count", integer);
        var peek = Method(cold, "Peek", integer, Pure, Returning(new FieldReferenceExpression(Node(), held)));
        peek.DeclaredThisTier = ThisTier.Frozen;
        var c = new LocalVariable("c", cold) { Ownership = OwnershipKind.Owned };
        var f = new LocalVariable("f", Function(pure: true, integer));
        var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block(
            new LocalDeclarationStatement(c, new NewExpression(Node(), cold, [], PureConstructor(cold), [], [])),
            new LocalDeclarationStatement(f, new FunctionReferenceExpression(Node(37), Ref(c), peek, f.Type))));
        return Judge(new PurityAnalyzer(), main);
    }

    private IReadOnlyList<Diagnostic> ImplicitCloneOfAFrozenInputThroughAnImpureClone()
    {
        box.MethodsList.Add(new Method("Clone", box, [], Public)
        {
            DeclaringType = box,
            ReturnOwnership = OwnershipKind.Owned,
            Body = HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(box), box)),
        });
        var b = Input("b", box, OwnershipKind.Frozen);
        var c = new LocalVariable("c", box) { Ownership = OwnershipKind.Owned };
        var take = Method(probe, "Take", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(c, new CloneExpression(Node(37), Ref(b))),
            new ExpressionStatement(Assign(Access(Ref(c), n), Literal(integer, 5L))),
            new ReturnStatement(Access(Ref(c), n), integer)), b);
        return Judge(new PurityAnalyzer(), take);
    }

    private IReadOnlyList<Diagnostic> ImpureMethodGroupToPureFunc()
    {
        var ops = HandBuiltModel.Class("Ops");
        var argument = Input("n", integer);
        var bump = Method(ops, "Bump", integer, Static, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Ref(argument), integer)), argument);
        var f = new LocalVariable("f", Function(pure: true, integer, integer));
        var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block(
            new LocalDeclarationStatement(f, new FunctionReferenceExpression(Node(37), new TypeReferenceExpression(Node(), ops), bump, f.Type))));
        return Judge(new PurityAnalyzer(), main);
    }

    private IReadOnlyList<Diagnostic> LocalMethodReadsThroughAnArgument()
    {
        var other = Input("other", bag);
        var peek = Method(bag, "Peek", integer, Local, Returning(Access(Ref(other), count, 37)), other);
        return Judge(new PurityAnalyzer(), peek);
    }

    private IReadOnlyList<Diagnostic> LocalReadonlyMethodWritesItsInstance()
    {
        var reset = Method(bag, "Reset", unit, LocalReadonly,
            HandBuiltModel.Block(new ExpressionStatement(Assign(new FieldReferenceExpression(Node(37), count), Literal(integer, 0L)))));
        return Judge(new PurityAnalyzer(), reset);
    }

    private IReadOnlyList<Diagnostic> LocalTypeMemberReadsAnArgument()
    {
        var acc = HandBuiltModel.Class("Acc");
        acc.Modifiers = Local;
        var total = Field(acc, "Total", integer);
        var other = Input("other", acc);
        var peek = Method(acc, "Peek", integer, Local, Returning(Access(Ref(other), total, 37)), other);
        return Judge(new PurityAnalyzer(), peek);
    }

    private IReadOnlyList<Diagnostic> PureConstructorChainsToAnUnmarkedOne()
    {
        var holder = HandBuiltModel.Class("Box");
        var seeded = Field(holder, "N", integer);
        var unmarked = new Constructor("Box", holder, [], Public)
        {
            DeclaringType = holder,
            Body = HandBuiltModel.Block(new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), seeded), StaticRead(ticks)))),
        };
        var chaining = new Constructor("Box", holder, [new Parameter("k", integer)], Pure)
        {
            DeclaringType = holder,
            ChainKind = ConstructorChainKind.This,
            ChainedConstructor = unmarked,
            DeclarationNode = Node(37),
            Body = HandBuiltModel.Block(),
        };
        holder.ConstructorsList.Add(unmarked);
        holder.ConstructorsList.Add(chaining);
        return Judge(new PurityAnalyzer(), chaining);
    }

    private IReadOnlyList<Diagnostic> PureLambdaWritesACapturedLocal()
    {
        var total = new LocalVariable("total", integer);
        var argument = new LocalVariable("n", integer);
        var promised = Function(pure: true, integer, integer);
        var lambda = new LambdaExpression(Node(), [argument], null, HandBuiltModel.Block(
            new ExpressionStatement(Assign(Ref(total, 37), Ref(argument))),
            new ReturnStatement(Ref(argument), integer)), false, promised)
        { IsPure = true };
        var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block(
            new LocalDeclarationStatement(total, Literal(integer, 0L)),
            new LocalDeclarationStatement(new LocalVariable("f", promised), lambda)));
        return Judge(new PurityAnalyzer(), main);
    }

    private IReadOnlyList<Diagnostic> PureMethodAllocatesThroughAnImplicitConstructorWithAnImpureInitializer()
    {
        var pair = HandBuiltModel.Struct("Pair");
        _ = Field(pair, "A", integer);
        pair.ConstructorsList.Add(new Constructor("Pair", pair, [new Parameter("a", integer)], Public) { DeclaringType = pair, Body = HandBuiltModel.Block() });
        var bare = HandBuiltModel.Class("Bare");
        var seeded = Field(bare, "N", integer);
        seeded.Initializer = StaticRead(ticks);
        _ = Field(bare, "P", pair);
        var b = new LocalVariable("b", bare);
        var direct = Method(probe, "Direct", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(b, new NewExpression(Node(37), bare, [], null, [], null)),
            new ReturnStatement(Access(Ref(b), seeded), integer)));
        return Judge(new PurityAnalyzer(), direct);
    }

    private IReadOnlyList<Diagnostic> PureMethodAwaitsATask()
    {
        var task = model.Type("Task<>");
        var drop = Method(task, "Drop", integer, Public with { IsIntrinsic = true }, null);
        var t = Input("t", model.Interner.GetOrCreateConstructedType(task, [integer]));
        var wait = Method(probe, "Wait", integer, PureStatic,
            Returning(new InvocationExpression(Node(37), new MemberAccessExpression(Node(), Ref(t), "Drop", integer), [], integer, drop, [])), t);
        return Judge(new PurityAnalyzer(), wait);
    }

    private IReadOnlyList<Diagnostic> PureMethodCallsAnImpureConversion()
    {
        var meters = HandBuiltModel.Struct("Meters");
        var made = Field(meters, "made", integer, isStatic: true);
        var conversion = new CastOperator(integer, new Parameter("m", meters), false, Static)
        {
            DeclaringType = meters,
            Body = HandBuiltModel.Block(WriteStatic(made), new ReturnStatement(Literal(integer, 1L), integer)),
        };
        meters.CastOperatorsList.Add(conversion);
        var m = Input("m", meters);
        var read = Method(meters, "Read", integer, PureStatic,
            Returning(new CastExpression(Node(37), Ref(m), integer, [new ConversionFunction(conversion, meters, integer)], integer)), m);
        return Judge(new PurityAnalyzer(), read);
    }

    private IReadOnlyList<Diagnostic> PureMethodCallsAnImpureOperator()
    {
        var tally = HandBuiltModel.Struct("Tally");
        var made = Field(tally, "made", integer, isStatic: true);
        var v = Field(tally, "v", integer);
        var plus = new Operator(OperatorKind.Plus, tally, [new Parameter("a", tally), new Parameter("b", tally)], Static)
        {
            DeclaringType = tally,
            Body = HandBuiltModel.Block(WriteStatic(made), new ReturnStatement(Literal(tally), tally)),
        };
        tally.OperatorsList.Add(plus);
        var t = Input("t", tally);
        var twice = Method(tally, "Twice", integer, PureStatic,
            Returning(Access(new BinaryExpression(Node(37), Ref(t), BinaryOperatorKind.Add, Ref(t), tally) { Operator = plus }, v)), t);
        return Judge(new PurityAnalyzer(), twice);
    }

    private IReadOnlyList<Diagnostic> PureMethodCallsAnUnmarkedMethod()
    {
        var now = Method(clock, "Now", integer, Static, Returning(StaticRead(ticks)));
        var sample = Method(clock, "Sample", integer, PureStatic, Returning(Call(now, null, 37)));
        return Judge(new PurityAnalyzer(), sample);
    }

    private IReadOnlyList<Diagnostic> PureMethodCallsConsole()
    {
        var console = HandBuiltModel.Class("Console");
        var writeLine = Method(console, "WriteLine", unit, Static with { IsClrProjected = true }, null, Input("value", text));
        var say = Method(probe, "Say", integer, PureStatic, HandBuiltModel.Block(
            new ExpressionStatement(Call(writeLine, null, 37, Literal(text, "said"))),
            new ReturnStatement(Literal(integer, 1L), integer)));
        return Judge(new PurityAnalyzer(), say);
    }

    private IReadOnlyList<Diagnostic> PureMethodHoldsALinearWithAnImpureDrop()
    {
        var h = new LocalVariable("h", handle);
        var open = Method(probe, "Open", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(h, new NewExpression(Node(37), handle, [Literal(integer, 1L)], handle.ConstructorsList[0], [], [integer])),
            new ReturnStatement(Access(Ref(h), fd), integer)));
        return Judge(new PurityAnalyzer(), open);
    }

    private IReadOnlyList<Diagnostic> PureMethodInvokesAPlainFunctionValue()
    {
        var f = Input("f", Function(pure: false, integer, integer));
        var apply = Method(probe, "Apply", integer, PureStatic,
            Returning(new InvocationExpression(Node(37), Ref(f), [Literal(integer, 1L)], integer, null, [integer])), f);
        return Judge(new PurityAnalyzer(), apply);
    }

    private IReadOnlyList<Diagnostic> PureMethodIteratesAnImpureCollection()
    {
        var (declared, loop) = ImpureLoop();
        var sum = Method(probe, "Sum", integer, PureStatic, HandBuiltModel.Block(declared, loop, new ReturnStatement(Literal(integer, 0L), integer)));
        return Judge(new PurityAnalyzer(), sum);
    }

    private IReadOnlyList<Diagnostic> PureMethodLaundersAWriteThroughNestedLambdas()
    {
        var b = Input("b", box);
        var action = Function(pure: false, unit);
        var inner = new LocalVariable("inner", action);
        var outer = new LocalVariable("outer", action);
        var writing = new LambdaExpression(Node(), [], null,
            HandBuiltModel.Block(new ExpressionStatement(Assign(Access(Ref(b), n), Literal(integer, 1L)))), false, action);
        var forwarding = new LambdaExpression(Node(), [], null,
            HandBuiltModel.Block(new ExpressionStatement(new InvocationExpression(Node(), Ref(inner), [], unit, null, []))), false, action);
        var launder = Method(probe, "Launder", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(inner, writing),
            new LocalDeclarationStatement(outer, forwarding),
            new ExpressionStatement(new InvocationExpression(Node(37), Ref(outer), [], unit, null, [])),
            new ReturnStatement(Literal(integer, 1L), integer)), b);
        return Judge(new PurityAnalyzer(), launder);
    }

    private IReadOnlyList<Diagnostic> PureMethodReadsAMutableField()
    {
        var peek = Method(bag, "Peek", integer, Pure, Returning(new FieldReferenceExpression(Node(37), count)));
        return Judge(new PurityAnalyzer(), peek);
    }

    private IReadOnlyList<Diagnostic> PureMethodReadsASpanParameter()
    {
        var s = Input("s", SpanOfTwo());
        var first = Method(probe, "First", integer, PureStatic, Returning(new IndexExpression(Node(37), Ref(s), Literal(integer, 0L), integer)), s);
        return Judge(new PurityAnalyzer(), first);
    }

    private IReadOnlyList<Diagnostic> PureMethodReadsAnImpureIndexer()
    {
        var log = HandBuiltModel.Class("Log");
        var reads = Field(log, "reads", integer, isStatic: true);
        var stored = Field(log, "cell", integer);
        var indexer = new Indexer(integer, [new Parameter("i", integer)], Public)
        {
            DeclaringType = log,
            Getter = new PropertyAccessor
            {
                Body = HandBuiltModel.Block(
                    new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), reads), Literal(integer, 1L))),
                    new ReturnStatement(new FieldReferenceExpression(Node(), stored), integer)),
            },
        };
        log.IndexersList.Add(indexer);
        var peek = Method(log, "Peek", integer, Pure,
            Returning(new IndexExpression(Node(37), new ThisExpression(Node(), log), Literal(integer, 0L), integer) { Indexer = indexer }));
        return Judge(new PurityAnalyzer(), peek);
    }

    private IReadOnlyList<Diagnostic> PureMethodReadsTheCountOfAnUnrestrictedList()
    {
        var (list, listOfInt) = IntrinsicList();
        var countOf = new Property("Count", integer, LocalReadonly with { IsIntrinsic = true }) { DeclaringType = list, Getter = new PropertyAccessor() };
        list.PropertiesList.Add(countOf);
        var xs = Input("xs", listOfInt);
        var bad = Method(probe, "Bad", integer, PureStatic,
            Returning(new MemberAccessExpression(Node(37), Ref(xs), "Count", integer, member: new PropertyResult(countOf, listOfInt, []))), xs);
        return Judge(new PurityAnalyzer(), bad);
    }

    private IReadOnlyList<Diagnostic> PureMethodRunsAnAsyncBlock()
    {
        var taskOfInt = model.Interner.GetOrCreateConstructedType(model.Type("Task<>"), [integer]);
        var run = Method(probe, "Run", taskOfInt, PureStatic,
            Returning(new AsyncBlockExpression(Node(37), Returning(Literal(integer, 1L)), taskOfInt, false)));
        return Judge(new PurityAnalyzer(), run);
    }

    private IReadOnlyList<Diagnostic> PureMethodStoresAFrozenReference()
    {
        var holder = HandBuiltModel.Class("Holder");
        var item = Field(holder, "Item", bag);
        var b = Input("b", bag, OwnershipKind.Frozen);
        var h = new LocalVariable("h", holder);
        var keep = Method(probe, "Keep", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(h, new NewExpression(Node(), holder, [], PureConstructor(holder), [], [])),
            new ExpressionStatement(Assign(Access(Ref(h), item), Ref(b, 37))),
            new ReturnStatement(Literal(integer, 1L), integer)), b);
        return Judge(new PurityAnalyzer(), keep);
    }

    private IReadOnlyList<Diagnostic> PureMethodWritesASpanParameter()
    {
        var s = Input("s", SpanOfTwo());
        var fill = Method(probe, "Fill", integer, PureStatic, HandBuiltModel.Block(
            new ExpressionStatement(Assign(new IndexExpression(Node(37), Ref(s), Literal(integer, 0L), integer), Literal(integer, 1L))),
            new ReturnStatement(Literal(integer, 1L), integer)), s);
        return Judge(new PurityAnalyzer(), fill);
    }

    private IReadOnlyList<Diagnostic> PureMethodWritesAStatic()
    {
        var bump = Method(clock, "Bump", integer, PureStatic, HandBuiltModel.Block(
            new ExpressionStatement(Assign(new FieldReferenceExpression(Node(37), ticks), Literal(integer, 1L))),
            new ReturnStatement(Literal(integer, 1L), integer)));
        return Judge(new PurityAnalyzer(), bump);
    }

    private IReadOnlyList<Diagnostic> PureMethodWritesAnElementOfAFrozenList()
    {
        var (list, listOfInt) = IntrinsicList();
        var indexer = new Indexer(integer, [new Parameter("index", integer)], Public with { IsIntrinsic = true })
        {
            DeclaringType = list,
            Getter = new PropertyAccessor { OwnModifiers = LocalReadonly },
            Setter = new PropertyAccessor { OwnModifiers = Local },
        };
        list.IndexersList.Add(indexer);
        var xs = Input("xs", listOfInt, OwnershipKind.Frozen);
        var poke = Method(probe, "Poke", integer, PureStatic, HandBuiltModel.Block(
            new ExpressionStatement(Assign(new IndexExpression(Node(37), Ref(xs), Literal(integer, 0L), integer) { Indexer = indexer }, Literal(integer, 1L))),
            new ReturnStatement(Literal(integer, 0L), integer)), xs);
        return Judge(new PurityAnalyzer(), poke);
    }

    private IReadOnlyList<Diagnostic> PureTypeMemberWrites()
    {
        var acc = HandBuiltModel.Class("Acc");
        acc.Modifiers = Pure;
        var total = Field(acc, "Total", integer);
        var argument = Input("n", integer);
        var set = Method(acc, "Set", unit, Pure,
            HandBuiltModel.Block(new ExpressionStatement(Assign(new FieldReferenceExpression(Node(37), total), Ref(argument)))), argument);
        return Judge(new PurityAnalyzer(), set);
    }

    private IReadOnlyList<Diagnostic> ReadonlyTypeSettablePropertyReadThroughAnUnrestrictedReference()
    {
        var frozenType = HandBuiltModel.Class("R");
        frozenType.Modifiers = Public with { IsReadonly = true };
        var x = new Property("X", integer, Public with { IsReadonly = true })
        {
            DeclaringType = frozenType,
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
        };
        frozenType.PropertiesList.Add(x);
        var r = Input("r", frozenType);
        var peek = Method(probe, "Peek", integer, PureStatic,
            Returning(new MemberAccessExpression(Node(37), Ref(r), "X", integer, member: new PropertyResult(x, frozenType, []))), r);
        return Judge(new PurityAnalyzer(), peek);
    }

    private IReadOnlyList<Diagnostic> StaticInitializerLambdaConvertingToAPureFunctionType()
    {
        var holder = HandBuiltModel.Class("Holder");
        var reader = Function(pure: true, integer);
        var lambda = new LambdaExpression(Node(), [], null, HandBuiltModel.Block(
            new ExpressionStatement(Assign(StaticRead(ticks, 37), Literal(integer, 1L))),
            new ReturnStatement(Literal(integer, 1L), integer)), false, reader);
        var stored = Field(holder, "R", reader, isStatic: true, isReadonly: true);
        stored.Initializer = lambda;
        return model.Analyze(new PurityAnalyzer(), lambda, owner: holder, member: stored);
    }

    private IReadOnlyList<Diagnostic> StructLocalMemberWritesThroughAnOwnedFieldOfACopy()
    {
        var target = new MemberAccessExpression(Node(37), new FieldReferenceExpression(Node(), inner), "N", integer, member: new FieldResult(n, box, []));
        var bump = Method(cell, "Bump", unit, Local, HandBuiltModel.Block(new ExpressionStatement(Assign(target, Literal(integer, 42L)))));
        return Judge(new PurityAnalyzer(), bump);
    }

    private (LocalDeclarationStatement Declared, ForEachStatement Loop) ImpureLoop()
    {
        var sequence = HandBuiltModel.Class("Seq");
        var enumerator = HandBuiltModel.Struct("Enumerator");
        var getEnumerator = Method(sequence, "GetEnumerator", enumerator, Public,
            HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(enumerator), enumerator)));
        var s = new LocalVariable("s", sequence);
        var declared = new LocalDeclarationStatement(s, new NewExpression(Node(), sequence, [], null, [], null));
        var loop = new ForEachStatement(new LocalVariable("x", integer), Ref(s, 37), HandBuiltModel.Block()) { GetEnumerator = getEnumerator };
        return (declared, loop);
    }

    private (ClassType List, ConstructedType OfInt) IntrinsicList()
    {
        var list = model.Type("List<>");
        list.Modifiers = Public with { IsIntrinsic = true };
        return (list, model.Interner.GetOrCreateConstructedType(list, [integer]));
    }

    private ConstructedType SpanOfTwo()
    {
        var span = new StructType("Span<,>", "Standard", new Scope(null), null, null) { Modifiers = new MemberModifiers { IsRef = true, IsIntrinsic = true } };
        return model.Interner.GetOrCreateConstructedType(span, [integer, model.Interner.LiftLiteral(integer, 2L)]);
    }

    private ClassType Primitive(string name)
    {
        var type = model.Type(name);
        type.Modifiers = new MemberModifiers { IsReadonly = true, IsIntrinsic = true };
        return type;
    }

    private FunctionType Function(bool pure, IMistType returns, params IMistType[] parameters)
    {
        return model.Interner.GetOrCreateFunctionType(parameters, returns, pure);
    }

    private IReadOnlyList<Diagnostic> Judge(IProgramAnalyzer analyzer, Method member, params MistType[] types)
    {
        return model.Analyze(analyzer, member.Body, types, member.DeclaringType, member);
    }

    private static AbstractType Contract(string name)
    {
        return new AbstractType(name, "Probe", new Scope(null), null, null);
    }

    private static ClassType Implementer(string name, AbstractType contract, bool declared = false)
    {
        var type = declared
            ? new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>(37))
            : HandBuiltModel.Class(name);
        type.BaseAbstractTypes.Add(contract);
        return type;
    }

    private static Field Field(
        ConcreteType owner, string name, IMistType type, bool isStatic = false, bool isReadonly = false, OwnershipKind ownership = OwnershipKind.Unrestricted)
    {
        var field = new Field(name, type, ownership, Public with { IsStatic = isStatic, IsReadonly = isReadonly }) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }

    private static Constructor PureConstructor(ConcreteType owner)
    {
        var constructor = new Constructor(owner.Name, owner, [], Pure) { DeclaringType = owner, Body = HandBuiltModel.Block() };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private static Method Method(MemberType owner, string name, IMistType returns, MemberModifiers modifiers, Block? body, params LocalVariable[] inputs)
    {
        var method = new Method(name, returns, [.. inputs.Select(input => input.Parameter!)], modifiers) { DeclaringType = owner, Body = body };
        owner.MethodsList.Add(method);
        return method;
    }

    private static LocalVariable Input(string name, IMistType type, OwnershipKind ownership = OwnershipKind.Unrestricted, bool isRef = false)
    {
        return new LocalVariable(name, type)
        {
            Ownership = ownership,
            IsRef = isRef,
            Parameter = new Parameter(name, type, new ParameterModifier(false, isRef, ownership)),
        };
    }

    private static SyntaxNode Node(int offset = 0)
    {
        return HandBuiltModel.Node(offset);
    }

    private static LiteralExpression Literal(IMistType type, object? value = null)
    {
        return HandBuiltModel.Literal(type, value);
    }

    private static LocalReferenceExpression Ref(LocalVariable local, int offset = 0)
    {
        return new LocalReferenceExpression(Node(offset), local);
    }

    private static MemberAccessExpression Access(Expression receiver, Field field, int offset = 0)
    {
        return new MemberAccessExpression(Node(offset), receiver, field.Name, field.Type, member: new FieldResult(field, receiver.Type, []));
    }

    private static MemberAccessExpression StaticRead(Field field, int offset = 0)
    {
        var owner = field.DeclaringType!;
        return new MemberAccessExpression(Node(offset), new TypeReferenceExpression(Node(), owner), field.Name, field.Type, member: new FieldResult(field, owner, []));
    }

    private static AssignmentExpression Assign(Expression target, Expression value)
    {
        return new AssignmentExpression(Node(), target, value, target.Type);
    }

    private ExpressionStatement WriteStatic(Field field)
    {
        return new ExpressionStatement(Assign(StaticRead(field), Literal(integer, 1L)));
    }

    private static Block Returning(Expression value)
    {
        return HandBuiltModel.Block(new ReturnStatement(value, value.Type));
    }

    private static InvocationExpression Call(Method method, Expression? receiver, int offset, params Expression[] arguments)
    {
        Expression callee = receiver is null
            ? new MethodReferenceExpression(Node(), method)
            : new MemberAccessExpression(Node(), receiver, method.Name, method.ReturnType);
        return new InvocationExpression(Node(offset), callee, arguments, method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }
}
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed partial class PurityFragments
{
    private IReadOnlyList<Diagnostic> AbstractPureIndexerImplementedImpurely()
    {
        var contract = Contract("IGrid");
        contract.IndexersList.Add(new Indexer(integer, [new Parameter("i", integer)], Pure) { DeclaringType = contract, Getter = new PropertyAccessor() });
        var grid = Implementer("Grid", contract, declared: true);
        grid.IndexersList.Add(new Indexer(integer, [new Parameter("i", integer)], Public)
        {
            DeclaringType = grid,
            Getter = new PropertyAccessor { Body = Returning(StaticRead(ticks)) },
        });
        return model.Analyze(new ConformanceAnalyzer(), types: [contract, grid]);
    }

    private IReadOnlyList<Diagnostic> LocalAbstractMemberImplementedImpurely()
    {
        var sink = Contract("Sink");
        _ = Method(sink, "Add", unit, Local, null, Input("n", integer));
        var loud = Implementer("Loud", sink, declared: true);
        _ = Method(loud, "Add", unit, Public, HandBuiltModel.Block(WriteStatic(ticks)), Input("n", integer));
        return model.Analyze(new ConformanceAnalyzer(), types: [sink, loud]);
    }

    private IReadOnlyList<Diagnostic> PureAbstractMemberImplementedImpurely()
    {
        var shape = Contract("Shape");
        _ = Method(shape, "Area", integer, Pure, null);
        var noisy = Implementer("Noisy", shape, declared: true);
        _ = Method(noisy, "Area", integer, Public, Returning(StaticRead(ticks)));
        return model.Analyze(new ConformanceAnalyzer(), types: [shape, noisy]);
    }

    private IReadOnlyList<Diagnostic> OwnedSlotAnsweredByAnUnownedReturn()
    {
        var contract = Contract("IMaker");
        contract.MethodsList.Add(new Method("Make", box, [], Pure) { DeclaringType = contract, ReturnOwnership = OwnershipKind.Owned });
        var maker = Implementer("Maker", contract, declared: true);
        _ = Method(maker, "Make", box, Pure, Returning(Literal(box)));
        return model.Analyze(new ConformanceAnalyzer(), types: [contract, maker]);
    }

    private IReadOnlyList<Diagnostic> DefaultEntryNotConsistent()
    {
        var holder = HandBuiltModel.Class("Box");
        _ = Field(holder, "N", integer);
        holder.ConstructorsList.Add(new Constructor("Box", holder, [new Parameter("n", integer)], Public) { DeclaringType = holder, Body = HandBuiltModel.Block() });
        holder.DefaultBlock = new DefaultBlock([new DefaultValueEntry("N", StaticRead(ticks, 37))], new DefaultBlockDeclarationNode());
        return model.Analyze(new PurityAnalyzer(), types: [holder]);
    }

    private IReadOnlyList<Diagnostic> DefaultInitializerNotConsistent()
    {
        var holder = HandBuiltModel.Class("Box");
        var seeded = Field(holder, "N", integer);
        seeded.Initializer = StaticRead(ticks, 37);
        return model.Analyze(new PurityAnalyzer(), types: [holder]);
    }

    private IReadOnlyList<Diagnostic> GenericTypeTestInstantiatedWithAPureFunctionType()
    {
        var coerce = Method(probe, "Coerce", UnknownType.Instance, Static, null);
        var parameter = new TypeParameter("T", 0, probe.Scope, probe);
        coerce.TypeParametersList.Add(parameter);
        var u = Input("u", UnknownType.Instance);
        var test = new IsPatternExpression(Node(), Ref(u), new TypeTestPattern(Node(), parameter, new LocalVariable("t", parameter)), boolean);
        model.Model.ConstructorConstraintClosures.RecordMethod(coerce, [Function(pure: true, integer)], TypeSubstitution.Empty, Node(37));
        return model.Analyze(new PatternAnalyzer(), test, owner: probe, member: coerce);
    }

    private IReadOnlyList<Diagnostic> PureFunctionTypeTestedAtRuntime()
    {
        var o = new LocalVariable("o", model.Type("Object"));
        var reader = Function(pure: true, integer);
        var test = new IsPatternExpression(Node(), Ref(o), new TypeTestPattern(Node(37), reader, new LocalVariable("r", reader)), boolean);
        return model.Analyze(new PatternAnalyzer(), test);
    }

    private IReadOnlyList<Diagnostic> PlainFuncToPureFunc()
    {
        var plain = new LocalVariable("plain", Function(pure: false, integer, integer));
        var promised = new LocalVariable("promised", Function(pure: true, integer, integer));
        return model.Analyze(new AssignabilityAnalyzer(), new LocalDeclarationStatement(promised, Ref(plain, 37)));
    }

    private IReadOnlyList<Diagnostic> ClrDelegateToPureFunc()
    {
        var multicast = new ClassType("MulticastDelegate", "System", new Scope(null), null, null);
        var predicate = new ClassType("Predicate<>", "System", new Scope(null), null, null) { BaseType = multicast };
        var element = new TypeParameter("T", 0, predicate.Scope, predicate);
        predicate.TypeParametersList.Add(element);
        predicate.MethodsList.Add(new Method("Invoke", boolean, [new Parameter("obj", element)], Public with { IsClrProjected = true }) { DeclaringType = predicate });
        var positive = new LocalVariable("positive", model.Interner.GetOrCreateConstructedType(predicate, [integer]));
        var promised = new LocalVariable("promised", Function(pure: true, boolean, integer));
        return model.Analyze(new AssignabilityAnalyzer(), new LocalDeclarationStatement(promised, Ref(positive, 37)));
    }

    private IReadOnlyList<Diagnostic> ConstructorAssignsAnotherInstancesReadonlyField()
    {
        var (locked, k) = Locked();
        var other = Input("other", locked);
        var v = Input("v", integer);
        var constructor = new Constructor("Box", locked, [other.Parameter!, v.Parameter!], Public)
        {
            DeclaringType = locked,
            Body = HandBuiltModel.Block(new ExpressionStatement(Assign(Access(Ref(other), k, 37), Ref(v)))),
        };
        locked.ConstructorsList.Add(constructor);
        return Judge(new ReadonlyAnalyzer(), constructor);
    }

    private IReadOnlyList<Diagnostic> ConstructorOfAnotherTypeAssignsAReadonlyField()
    {
        var (locked, k) = Locked();
        var hacker = HandBuiltModel.Class("Hacker");
        var b = Input("b", locked);
        var constructor = new Constructor("Hacker", hacker, [b.Parameter!], Public)
        {
            DeclaringType = hacker,
            Body = HandBuiltModel.Block(new ExpressionStatement(Assign(Access(Ref(b), k, 37), Literal(integer, 99L)))),
        };
        hacker.ConstructorsList.Add(constructor);
        return Judge(new ReadonlyAnalyzer(), constructor);
    }

    private IReadOnlyList<Diagnostic> OwnedReturnOfThis()
    {
        var same = new Method("Same", box, [], Public)
        {
            DeclaringType = box,
            ReturnOwnership = OwnershipKind.Owned,
            Body = HandBuiltModel.Block(new ReturnStatement(new ThisExpression(Node(37), box), box)),
        };
        box.MethodsList.Add(same);
        return Judge(new OwnershipAnalyzer(), same);
    }

    private IReadOnlyList<Diagnostic> ReadonlyFieldPassedByRef()
    {
        var (locked, k) = Locked();
        var slot = Input("slot", integer, isRef: true);
        var set = Method(locked, "Set", unit, Static, HandBuiltModel.Block(), slot);
        var twist = Method(locked, "Twist", unit, Public,
            HandBuiltModel.Block(new ExpressionStatement(Call(set, null, 0, new FieldReferenceExpression(Node(37), k)))));
        return Judge(new ReadonlyAnalyzer(), twist);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodCallsAMutableConversionOperator()
    {
        var wrapped = HandBuiltModel.Class("Wrapped");
        var conversion = new CastOperator(wrapped, new Parameter("b", box), false, Static)
        {
            DeclaringType = wrapped,
            Body = HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(wrapped), wrapped)),
        };
        wrapped.CastOperatorsList.Add(conversion);
        var b = Input("b", box);
        var m = Method(probe, "M", integer, ReadonlyStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(new LocalVariable("w", wrapped),
                new CastExpression(Node(37), Ref(b), wrapped, [new ConversionFunction(conversion, box, wrapped)], wrapped)),
            new ReturnStatement(Literal(integer, 1L), integer)), b);
        return Judge(new ReadonlyAnalyzer(), m);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodIteratesAnImpureCollection()
    {
        var (declared, loop) = ImpureLoop();
        var sum = Method(probe, "Sum", integer, Public with { IsReadonly = true },
            HandBuiltModel.Block(declared, loop, new ReturnStatement(Literal(integer, 0L), integer)));
        return Judge(new ReadonlyAnalyzer(), sum);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodReadsAMutableGetterInAPropertyPattern()
    {
        var sensor = HandBuiltModel.Class("Cell");
        var level = new Property("Level", integer, Public)
        {
            DeclaringType = sensor,
            Getter = new PropertyAccessor { Body = HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(StaticRead(ticks), integer)) },
        };
        sensor.PropertiesList.Add(level);
        var c = Input("c", sensor);
        var pattern = new PropertyPattern(Node(), sensor,
            [new PropertySubpattern(Node(37), "Level", new ComparisonPattern(Node(), integer, ComparisonKind.Equal, Literal(integer, 1L)), integer, new PropertyResult(level, sensor, []))]);
        var m = Method(probe, "M", integer, ReadonlyStatic, HandBuiltModel.Block(
            new ExpressionStatement(new IsPatternExpression(Node(), Ref(c), pattern, boolean)),
            new ReturnStatement(Literal(integer, 0L), integer)), c);
        return Judge(new ReadonlyAnalyzer(), m);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodRunsAMutableSetterThroughAnObjectInitializer()
    {
        var sensor = HandBuiltModel.Class("Cell");
        var stored = Field(sensor, "level", integer);
        var level = new Property("Level", integer, Public)
        {
            DeclaringType = sensor,
            Getter = new PropertyAccessor { Body = Returning(new FieldReferenceExpression(Node(), stored)) },
            Setter = new PropertyAccessor
            {
                Body = HandBuiltModel.Block(new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), stored), Literal(integer, 1L))), WriteStatic(ticks)),
            },
        };
        sensor.PropertiesList.Add(level);
        var entry = new ObjectInitializerAssignment(Node(37), "Level", Literal(integer, 3L), integer, member: new PropertyResult(level, sensor, []));
        var m = Method(probe, "M", integer, ReadonlyStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(new LocalVariable("c", sensor), new NewExpression(Node(), sensor, [], PureConstructor(sensor), [entry], [])),
            new ReturnStatement(Literal(integer, 0L), integer)));
        return Judge(new ReadonlyAnalyzer(), m);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodWritesAnElementOfAnArrayParameter()
    {
        var array = model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [integer, model.Interner.GetOrCreateUnknownValue(integer)]);
        var a = Input("a", array);
        var fill = Method(probe, "Fill", integer, ReadonlyStatic, HandBuiltModel.Block(
            new ExpressionStatement(Assign(new IndexExpression(Node(37), Ref(a), Literal(integer, 0L), integer), Literal(integer, 5L))),
            new ReturnStatement(Literal(integer, 0L), integer)), a);
        return Judge(new ReadonlyAnalyzer(), fill);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodWritesThroughARefParameter()
    {
        var x = Input("x", integer, isRef: true);
        var set = Method(probe, "Set", integer, ReadonlyStatic, HandBuiltModel.Block(
            new ExpressionStatement(Assign(Ref(x, 37), Literal(integer, 5L))),
            new ReturnStatement(Ref(x), integer)), x);
        return Judge(new ReadonlyAnalyzer(), set);
    }

    private (ClassType Box, Field K) Locked()
    {
        var locked = HandBuiltModel.Class("Box");
        return (locked, Field(locked, "K", integer, isReadonly: true));
    }
}
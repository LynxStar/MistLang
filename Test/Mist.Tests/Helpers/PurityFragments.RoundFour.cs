using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Rules;
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
    public IReadOnlyList<Diagnostic> RoundFour(string name)
    {
        return name switch
        {
            "frozen-local-declared-over-an-unrestricted-input" => FrozenLocalDeclaredOver(owned: false),
            "frozen-local-declared-over-an-owned-binding" => FrozenLocalDeclaredOver(owned: true),
            "frozen-parameter-lent-a-field-of-an-unrestricted-input" => FrozenParameterLent(LentFrom.Field),
            "frozen-parameter-lent-a-call-result" => FrozenParameterLent(LentFrom.Call),
            "frozen-parameter-lent-an-owned-binding" => FrozenParameterLent(LentFrom.Owned),
            "with-frozen-receiver-through-a-plain-local" => WithFrozenReceiver(OwnershipKind.Unrestricted),
            "with-frozen-receiver-through-a-frozen-parameter" => WithFrozenReceiver(OwnershipKind.Frozen),
            "frozen-parameter-erased-by-pure-function-type-conversion" => TieredParameterErasedByPureFunctionTypeConversion(OwnershipKind.Frozen),
            "owned-parameter-erased-by-pure-function-type-conversion" => TieredParameterErasedByPureFunctionTypeConversion(OwnershipKind.Owned),
            "params-array-passed-directly-read-as-frozen-without-a-frozen-lend" => ParamsArrayPassed(expanded: false),
            "params-array-filled-from-expanded-arguments" => ParamsArrayPassed(expanded: true),
            "pattern-binding-reassigned-keeps-its-scrutinee-origin" => PatternBindingReassignedKeepsItsScrutineeOrigin(),
            "ref-return-on-an-owned-receiver-points-into-a-frozen-argument" => RefReturnOnAnOwnedReceiverPointsIntoAFrozenArgument(),
            "newtype-copy-conversion-reads-mutable-storage-unjudged" => NewtypeCopyConversionReadsMutableStorage(),
            "local-readonly-object-initializer-stores-unowned-into-owned-field" => LocalReadonlyObjectInitializer(fromThis: false),
            "local-readonly-object-initializer-moves-its-own-owned-field" => LocalReadonlyObjectInitializer(fromThis: true),
            "type-pattern-unboxed-struct-copied-into-an-own-local" => TypePatternUnboxedStructCopiedIntoAnOwnLocal(),
            "readonly-struct-local-captured-by-a-pure-lambda-mutated-through-a-local-method" => ReadonlyStructLocalCapturedByAPureLambda(mutates: true),
            "readonly-struct-local-captured-by-a-pure-lambda-read-through-a-local-readonly-method" => ReadonlyStructLocalCapturedByAPureLambda(mutates: false),
            "static-constructor-body-runs-unjudged" => StaticConstructorBody(writesOwnStatic: false),
            "static-constructor-writing-its-own-statics" => StaticConstructorBody(writesOwnStatic: true),
            "implicit-chain-to-params-only-base-constructor" => ImplicitChainToParamsOnlyBaseConstructor(),
            "index-from-end-runs-count-getter-unjudged" => IndexFromEndRunsCountGetterUnjudged(),
            "list-pattern-runs-count-and-indexer-getters-unjudged" => ListPatternRunsCountAndIndexerGettersUnjudged(),
            "foreach-calls-enumerator-dispose-unjudged" => ForeachCallsEnumeratorDispose(),
            "foreach-over-an-extension-getenumerator-is-unjudged" => ForeachOverAnUnrecordedGetEnumerator(),
            "interpolation-dispatches-to-iformattable-tostring" => InterpolationDispatchesToIFormattableToString(),
            "string-concatenation-formats-a-type-parameter-through-runtime-tostring" => StringConcatenationFormatsATypeParameter(),
            "constructor-context-clause-read-without-declaring-it" => ConstructorContextClause(chained: false),
            "chained-constructor-context-clause-read-without-declaring-it" => ConstructorContextClause(chained: true),
            "enum-variant-read-runs-member-value-initializers" => EnumVariantReadRunsMemberValueInitializers(),
            "owned-field-of-an-owned-holder-copied-into-another-owned-slot-without-a-move" => OwnedFieldOfAnOwnedHolderMoved(intoBinding: false),
            "owned-field-of-an-owned-holder-extracted-into-an-owned-binding" => OwnedFieldOfAnOwnedHolderMoved(intoBinding: true),
            _ => RoundFourDispatch(name),
        };
    }

    public bool LinearLocalMovedIntoAnAssignedCallIsDropped()
    {
        var keep = Input("k", handle);
        var kept = Method(probe, "Keep", handle, PureStatic, Returning(Ref(keep)), keep);
        var h = new LocalVariable("h", handle);
        var r = new LocalVariable("r", handle);
        var declaration = new LocalDeclarationStatement(h, new NewExpression(Node(), handle, [Literal(integer, 1L)], handle.ConstructorsList[0], [], [integer]));

        Statement[] remainder = [
            new LocalDeclarationStatement(r, null),
            new ExpressionStatement(Assign(Ref(r), Call(kept, null, 0, Ref(h))))];
        var body = HandBuiltModel.Block([declaration, .. remainder]);

        return DropInsertion.ReleaseOf(declaration, remainder, LocalLifetimes.Plan(body)) != LifetimeRelease.None;
    }

    private enum LentFrom
    {
        Field,
        Call,
        Owned,
    }

    private IReadOnlyList<Diagnostic> FrozenLocalDeclaredOver(bool owned)
    {
        var ext = Input("ext", box);
        var mine = new LocalVariable("b", box) { Ownership = OwnershipKind.Owned };
        var f = new LocalVariable("f", box) { Ownership = OwnershipKind.Frozen };
        var statements = new List<Statement>();

        if(owned)
        {
            statements.Add(new LocalDeclarationStatement(mine, Fresh(box)));
        }

        statements.Add(new LocalDeclarationStatement(f, Ref(owned ? mine : ext)));
        statements.Add(new ReturnStatement(Access(Ref(f), n, 37), integer));
        var count = Method(probe, "Count", integer, PureStatic, HandBuiltModel.Block([.. statements]), owned ? [] : [ext]);
        return Judge(new PurityAnalyzer(), count);
    }

    private IReadOnlyList<Diagnostic> FrozenParameterLent(LentFrom from)
    {
        var holder = HandBuiltModel.Class("Holder");
        var items = Field(holder, "Items", box, isReadonly: true);
        var xs = Input("xs", box, OwnershipKind.Frozen);
        var size = Method(probe, "Size", integer, PureStatic, Returning(Access(Ref(xs), n)), xs);
        var x = Input("x", box);
        var id = Method(probe, "Id", box, PureStatic, Returning(Ref(x)), x);
        var h = Input("h", holder);
        var mine = new LocalVariable("mine", box) { Ownership = OwnershipKind.Owned };

        var body = from switch
        {
            LentFrom.Field => Returning(Call(size, null, 0, Access(Ref(h), items, 37))),
            LentFrom.Call => Returning(Call(size, null, 0, Call(id, null, 37, Access(Ref(h), items)))),
            _ => HandBuiltModel.Block(
                new LocalDeclarationStatement(mine, Fresh(box)),
                new ReturnStatement(Call(size, null, 0, Ref(mine, 37)), integer)),
        };

        var count = Method(probe, "Count3", integer, PureStatic, body, h);
        return Judge(new PurityAnalyzer(), count);
    }

    private IReadOnlyList<Diagnostic> WithFrozenReceiver(OwnershipKind tier)
    {
        var get = Method(box, "Get", integer, Pure, Returning(new FieldReferenceExpression(Node(), n)));
        get.DeclaredThisTier = ThisTier.Frozen;
        var b = Input("b", box, tier);
        var c = new LocalVariable("c", box);

        var body = tier is OwnershipKind.Frozen
            ? Returning(Call(get, Ref(b), 37))
            : HandBuiltModel.Block(new LocalDeclarationStatement(c, Ref(b)), new ReturnStatement(Call(get, Ref(c), 37), integer));

        var read = Method(probe, "Read", integer, PureStatic, body, b);
        return Judge(new PurityAnalyzer(), read);
    }

    private IReadOnlyList<Diagnostic> TieredParameterErasedByPureFunctionTypeConversion(OwnershipKind tier)
    {
        var xs = Input("xs", box, tier);
        var size = Method(probe, "Size", integer, PureStatic, Returning(Access(Ref(xs), n)), xs);
        var f = new LocalVariable("f", Function(pure: true, integer, box));
        var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block(
            new LocalDeclarationStatement(f, new FunctionReferenceExpression(Node(37), new TypeReferenceExpression(Node(), probe), size, f.Type))));
        return Judge(new PurityAnalyzer(), main);
    }

    private IReadOnlyList<Diagnostic> ParamsArrayPassed(bool expanded)
    {
        var xs = Input("xs", ArrayOf(integer));
        xs.Parameter!.IsFlattened = true;
        var sum = Method(probe, "Sum", integer, PureStatic, Returning(Literal(integer, 0L)), xs);
        var ext = Input("ext", ArrayOf(integer));
        var call = expanded ? Call(sum, null, 0, Literal(integer, 1L), Literal(integer, 2L)) : Call(sum, null, 0, Ref(ext, 37));
        var forward = Method(probe, "Forward", integer, PureStatic, Returning(call), ext);
        return Judge(new PurityAnalyzer(), forward);
    }

    private IReadOnlyList<Diagnostic> PatternBindingReassignedKeepsItsScrutineeOrigin()
    {
        var ext = Input("ext", box);
        var mine = new LocalVariable("mine", box);
        var b = new LocalVariable("b", box);
        var poke = Method(probe, "Poke", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(mine, Fresh(box)),
            new ExpressionStatement(new IsPatternExpression(Node(), Ref(mine), new TypeTestPattern(Node(), box, b), boolean)),
            new ExpressionStatement(Assign(Ref(b), Ref(ext))),
            new ExpressionStatement(Assign(Access(Ref(b), n, 37), Literal(integer, 1L))),
            new ReturnStatement(Literal(integer, 0L), integer)), ext);
        return Judge(new PurityAnalyzer(), poke);
    }

    private IReadOnlyList<Diagnostic> RefReturnOnAnOwnedReceiverPointsIntoAFrozenArgument()
    {
        var cellular = HandBuiltModel.Class("Slot");
        var fresh = PureConstructor(cellular);
        var lent = Input("b", box, OwnershipKind.Frozen);
        var at = Method(cellular, "At", integer, Public with { IsRef = true }, Returning(Access(Ref(lent), n)), lent);
        var src = Input("src", box, OwnershipKind.Frozen);
        var c = new LocalVariable("c", cellular);
        var r = new LocalVariable("r", integer) { IsRef = true };
        var poke = Method(probe, "Poke", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(c, new NewExpression(Node(), cellular, [], fresh, [], [])),
            new LocalDeclarationStatement(r, Call(at, Ref(c), 0, Ref(src))),
            new ExpressionStatement(Assign(Ref(r, 37), Literal(integer, 1L))),
            new ReturnStatement(Literal(integer, 1L), integer)), src);
        return Judge(new PurityAnalyzer(), poke);
    }

    private IReadOnlyList<Diagnostic> NewtypeCopyConversionReadsMutableStorage()
    {
        var gauge = HandBuiltModel.Class("R");
        gauge.Modifiers = Public with { IsReadonly = true };
        gauge.PropertiesList.Add(new Property("X", integer, Public)
        {
            DeclaringType = gauge,
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
        });
        var copy = HandBuiltModel.Newtype("NR", gauge);
        var conversion = new CastOperator(copy, new Parameter("value", gauge), false, Static) { CopiesClone = true };
        var r = Input("r", gauge);
        var cast = new CastExpression(Node(37), Ref(r), copy, [new ConversionFunction(conversion, gauge, copy)], copy);
        var snap = Method(probe, "Snap", copy, PureStatic, Returning(cast), r);
        return Judge(new PurityAnalyzer(), snap);
    }

    private IReadOnlyList<Diagnostic> LocalReadonlyObjectInitializer(bool fromThis)
    {
        var holder = HandBuiltModel.Class("Holder");
        var items = Field(holder, "Items", box, ownership: OwnershipKind.Owned);
        var fresh = PureConstructor(holder);
        var ext = Input("ext", box);
        Expression source = fromThis ? Access(new ThisExpression(Node(), holder), items, 37) : Ref(ext, 37);
        var entry = new ObjectInitializerAssignment(Node(), "Items", source, box, member: new FieldResult(items, holder, []));
        var wrap = Method(holder, "Wrap", holder, LocalReadonly, Returning(new NewExpression(Node(), holder, [], fresh, [entry], [])), ext);
        return Judge(new PurityAnalyzer(), wrap);
    }

    private IReadOnlyList<Diagnostic> TypePatternUnboxedStructCopiedIntoAnOwnLocal()
    {
        var bump = Contract("IBump");
        var shape = HandBuiltModel.Struct("S");
        shape.BaseAbstractTypes.Add(bump);
        var x = Field(shape, "X", integer);
        var b = Input("b", bump);
        var t = new LocalVariable("t", shape);
        var copy = new LocalVariable("s", shape);
        var read = Method(probe, "Read", integer, PureStatic, HandBuiltModel.Block(
            new ExpressionStatement(new IsPatternExpression(Node(), Ref(b), new TypeTestPattern(Node(37), shape, t), boolean)),
            new LocalDeclarationStatement(copy, Ref(t)),
            new ReturnStatement(Access(Ref(copy), x), integer)), b);
        return Judge(new PurityAnalyzer(), read);
    }

    private IReadOnlyList<Diagnostic> ReadonlyStructLocalCapturedByAPureLambda(bool mutates)
    {
        var counter = HandBuiltModel.Struct("S");
        var x = Field(counter, "X", integer);
        var fresh = PureConstructor(counter);
        var bump = Method(counter, "Bump", integer, Local, HandBuiltModel.Block(
            new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), x), Literal(integer, 1L))),
            new ReturnStatement(new FieldReferenceExpression(Node(), x), integer)));
        var peek = Method(counter, "Peek", integer, LocalReadonly, Returning(new FieldReferenceExpression(Node(), x)));
        var s = new LocalVariable("s", counter) { IsReadonly = true };
        var step = Function(pure: true, integer);
        var f = new LocalVariable("f", step);
        var lambda = new LambdaExpression(Node(), [], Call(mutates ? bump : peek, Ref(s), 37), null, false, step);
        var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block(
            new LocalDeclarationStatement(s, new NewExpression(Node(), counter, [], fresh, [], [])),
            new LocalDeclarationStatement(f, lambda)));
        return Judge(new PurityAnalyzer(), main);
    }

    private IReadOnlyList<Diagnostic> StaticConstructorBody(bool writesOwnStatic)
    {
        var util = HandBuiltModel.Class("Util");
        var runs = Field(util, "Runs", integer, isStatic: true);
        util.ConstructorsList.Add(new Constructor("Util", util, [], Static)
        {
            DeclaringType = util,
            Body = HandBuiltModel.Block(writesOwnStatic ? new ExpressionStatement(Assign(StaticRead(runs), Literal(integer, 1L))) : WriteStatic(ticks)),
        });
        var twice = Method(util, "Twice", integer, PureStatic, Returning(Literal(integer, 2L)));
        var use = Method(probe, "Use", integer, PureStatic, Returning(Call(twice, null, 37)));
        return Judge(new PurityAnalyzer(), use);
    }

    private IReadOnlyList<Diagnostic> ImplicitChainToParamsOnlyBaseConstructor()
    {
        var root = HandBuiltModel.Class("Base");
        var ns = Input("ns", ArrayOf(integer));
        ns.Parameter!.IsFlattened = true;
        root.ConstructorsList.Add(new Constructor("Base", root, [ns.Parameter], Public) { DeclaringType = root, Body = HandBuiltModel.Block(WriteStatic(ticks)) });
        var derived = HandBuiltModel.Class("Derived", root);
        var constructor = new Constructor("Derived", derived, [], Pure) { DeclaringType = derived, DeclarationNode = Node(37), Body = HandBuiltModel.Block() };
        derived.ConstructorsList.Add(constructor);
        return model.Analyze(new PurityAnalyzer(), constructor.Body, [], derived, constructor);
    }

    private (ClassType Row, Indexer Indexer, Property Count) Row()
    {
        var row = HandBuiltModel.Class("Row");
        _ = PureConstructor(row);
        var count = new Property("Count", integer, Public)
        {
            DeclaringType = row,
            Getter = new PropertyAccessor { Body = HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 2L), integer)) },
        };
        row.PropertiesList.Add(count);
        var indexer = new Indexer(integer, [new Parameter("i", integer)], Pure) { DeclaringType = row, Getter = new PropertyAccessor { Body = Returning(Literal(integer, 1L)) } };
        row.IndexersList.Add(indexer);
        return (row, indexer, count);
    }

    private IReadOnlyList<Diagnostic> IndexFromEndRunsCountGetterUnjudged()
    {
        var (row, indexer, count) = Row();
        var r = new LocalVariable("r", row);
        var index = new IndexExpression(Node(37), Ref(r), Literal(integer, 1L), integer) { Indexer = indexer, FromEnd = new CountedIndexer(indexer, integer, count) };
        var last = Method(probe, "Last", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(r, Fresh(row)),
            new ReturnStatement(index, integer)));
        return Judge(new PurityAnalyzer(), last);
    }

    private IReadOnlyList<Diagnostic> ListPatternRunsCountAndIndexerGettersUnjudged()
    {
        var (row, indexer, count) = Row();
        var r = new LocalVariable("r", row);
        var pattern = new ListPattern(Node(37), [], row) { Reads = new CountedIndexer(indexer, integer, count) };
        var starts = Method(probe, "Starts", boolean, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(r, Fresh(row)),
            new ReturnStatement(new IsPatternExpression(Node(), Ref(r), pattern, boolean), boolean)));
        return Judge(new PurityAnalyzer(), starts);
    }

    private IReadOnlyList<Diagnostic> ForeachCallsEnumeratorDispose()
    {
        var disposable = new AbstractType("IDisposable", "System", new Scope(null), null, null);
        var enumerator = HandBuiltModel.Class("Enumerator");
        enumerator.BaseAbstractTypes.Add(disposable);
        _ = PureConstructor(enumerator);
        var moveNext = Method(enumerator, "MoveNext", boolean, Local, Returning(Literal(boolean, false)));
        var current = new Property("Current", integer, LocalReadonly) { DeclaringType = enumerator, Getter = new PropertyAccessor { IsAuto = true } };
        enumerator.PropertiesList.Add(current);
        _ = Method(enumerator, "Dispose", unit, Public, HandBuiltModel.Block(WriteStatic(ticks)));
        var sequence = HandBuiltModel.Class("Seq");
        _ = PureConstructor(sequence);
        var getEnumerator = Method(sequence, "GetEnumerator", enumerator, Pure, Returning(Fresh(enumerator)));
        var s = new LocalVariable("s", sequence);
        var loop = new ForEachStatement(new LocalVariable("x", integer), Ref(s, 37), HandBuiltModel.Block())
        {
            GetEnumerator = getEnumerator,
            MoveNext = moveNext,
            Current = current,
        };
        var sum = Method(probe, "Sum", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(s, Fresh(sequence)),
            loop,
            new ReturnStatement(Literal(integer, 0L), integer)));
        return Judge(new PurityAnalyzer(), sum, sequence, enumerator);
    }

    private IReadOnlyList<Diagnostic> ForeachOverAnUnrecordedGetEnumerator()
    {
        var sequence = HandBuiltModel.Class("Bag2");
        _ = PureConstructor(sequence);
        var b = new LocalVariable("b", sequence);
        var sum = Method(probe, "Sum", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(b, Fresh(sequence)),
            new ForEachStatement(new LocalVariable("x", integer), Ref(b, 37), HandBuiltModel.Block()),
            new ReturnStatement(Literal(integer, 0L), integer)));
        return Judge(new PurityAnalyzer(), sum);
    }

    private IReadOnlyList<Diagnostic> InterpolationDispatchesToIFormattableToString()
    {
        var formattable = new AbstractType("IFormattable", "System", new Scope(null), null, null);
        var formatted = new ClassType("Boxed", "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>(37));
        formatted.BaseAbstractTypes.Add(formattable);
        return PureMethodInterpolates(formatted);
    }

    private IReadOnlyList<Diagnostic> StringConcatenationFormatsATypeParameter()
    {
        var parameter = new TypeParameter("T", 0, probe.Scope, probe);
        var o = Input("o", parameter);
        var concatenation = new BinaryExpression(Node(), Literal(text, "v="), BinaryOperatorKind.Add, Ref(o, 37), text);
        var show = Method(probe, "Show", text, PureStatic, Returning(concatenation), o);
        show.TypeParametersList.Add(parameter);
        return Judge(new PurityAnalyzer(), show);
    }

    private IReadOnlyList<Diagnostic> ConstructorContextClause(bool chained)
    {
        var seeded = HandBuiltModel.Class("Seeded");
        var reading = new Constructor("Seeded", seeded, [], Pure) { DeclaringType = seeded, Body = HandBuiltModel.Block() };
        reading.ContextBindingsList.Add(new ContextBinding(integer, "n"));
        seeded.ConstructorsList.Add(reading);

        if(!chained)
        {
            var direct = Method(probe, "Direct", seeded, PureStatic, Returning(new NewExpression(Node(37), seeded, [], reading, [], [])));
            return Judge(new PurityAnalyzer(), direct);
        }

        var derived = HandBuiltModel.Class("Derived", seeded);
        var chaining = new Constructor("Derived", derived, [], Pure)
        {
            DeclaringType = derived,
            DeclarationNode = Node(37),
            Body = HandBuiltModel.Block(),
            ChainKind = ConstructorChainKind.Base,
            ChainedConstructor = reading,
        };
        derived.ConstructorsList.Add(chaining);
        return model.Analyze(new PurityAnalyzer(), chaining.Body, [], derived, chaining);
    }

    private IReadOnlyList<Diagnostic> EnumVariantReadRunsMemberValueInitializers()
    {
        var seeds = HandBuiltModel.Class("Seeds");
        var loud = Method(seeds, "Loud", integer, Static, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 7L), integer)));
        var level = HandBuiltModel.Enum("Level", integer);
        var low = new EnumMember("Low", false, Call(loud, null, 0));
        level.MembersList.Add(low);
        var read = new MemberAccessExpression(Node(37), new TypeReferenceExpression(Node(), level), "Low", level, member: new EnumMemberResult(low, level));
        var check = Method(probe, "Check", level, PureStatic, Returning(read));
        return Judge(new PurityAnalyzer(), check);
    }

    private IReadOnlyList<Diagnostic> OwnedFieldOfAnOwnedHolderMoved(bool intoBinding)
    {
        var holder = HandBuiltModel.Class("Holder");
        var items = Field(holder, "Items", box, ownership: OwnershipKind.Owned);
        _ = PureConstructor(holder);
        var h1 = new LocalVariable("h1", holder) { Ownership = OwnershipKind.Owned };
        var h2 = new LocalVariable("h2", holder) { Ownership = OwnershipKind.Owned };
        var x = new LocalVariable("x", box) { Ownership = OwnershipKind.Owned };

        Statement moved = intoBinding
            ? new LocalDeclarationStatement(x, Access(Ref(h1), items, 37))
            : new ExpressionStatement(Assign(Access(Ref(h2), items), Access(Ref(h1), items, 37)));

        var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block(
            new LocalDeclarationStatement(h1, Fresh(holder)),
            new LocalDeclarationStatement(h2, Fresh(holder)),
            moved));
        return Judge(new OwnershipAnalyzer(), main);
    }

    private static NewExpression Fresh(ConcreteType type)
    {
        var constructor = type.ConstructorsList.Find(candidate => !candidate.Modifiers.IsStatic) ?? PureConstructor(type);
        return new NewExpression(Node(), type, [], constructor, [], []);
    }
}
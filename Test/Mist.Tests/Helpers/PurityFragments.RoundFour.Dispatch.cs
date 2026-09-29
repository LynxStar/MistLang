using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed partial class PurityFragments
{
    public IReadOnlyList<Indexer> IndexersOfAConstrainedTypeParameter()
    {
        var slots = Contract("ISlots");
        slots.IndexersList.Add(new Indexer(integer, [new Parameter("i", integer)], Public) { DeclaringType = slots, Getter = new PropertyAccessor() });
        var parameter = new TypeParameter("T", 0, probe.Scope, probe);
        parameter.ConstraintsList.Add(new TypeConstraint(slots));
        return Indexability.Indexers(parameter);
    }

    public string EmittedOverloadedCall(string argument)
    {
        var shape = Contract("Shape");
        var point = HandBuiltModel.Struct("Point");
        var helper = HandBuiltModel.Class("Helper");
        var parameter = new TypeParameter("K", 0, probe.Scope, probe);

        var (bound, other, passed) = argument switch
        {
            "this of an abstract" => ((IMistType)shape, (IMistType)HandBuiltModel.Class("Square"), (Expression)new ThisExpression(Node(), shape)),
            "this of a struct" => (point, HandBuiltModel.Struct("Point3"), new ThisExpression(Node(), point)),
            _ => (UnknownType.Instance, integer, Ref(Input("x", parameter))),
        };

        var measure = Method(helper, "Measure", integer, PureStatic, Returning(Literal(integer, 1L)), Input("s", bound));
        _ = Method(helper, "Measure", integer, Static, Returning(Literal(integer, 2L)), Input("s", other));
        var writer = new CSharpWriter();
        BodyEmitter.EmitBlock(HandBuiltModel.Block(new ExpressionStatement(Call(measure, null, 0, passed))), writer, new DiagnosticBag());
        return writer.ToString();
    }

    private IReadOnlyList<Diagnostic> RoundFourDispatch(string name)
    {
        return name switch
        {
            "list-contains-dispatches-to-an-impure-iequatable-equals" => ListComparison("Contains", Element(ElementKind.Equatable)),
            "list-sort-dispatches-to-an-impure-icomparable-compareto" => ListComparison("Sort", Element(ElementKind.Comparable)),
            "list-contains-compares-clr-elements-through-unseen-equals" => ListComparison("Contains", Element(ElementKind.Clr)),
            "list-contains-compares-intrinsic-elements" => ListComparison("Contains", integer),
            "anonymous-type-equality-dispatches-to-iequatable" => AnonymousTypeEqualityDispatchesToIEquatable(),
            "slot-implementation-context-clause-hidden-by-abstract-dispatch" => SlotImplementationContextClauseHiddenByAbstractDispatch(),
            "default-of-a-derived-type-runs-the-base-constructor" => DefaultOfADerivedTypeRunsTheBaseConstructor(),
            "pure-get-on-a-settable-auto-property-is-trusted-without-being-judged" => PureGetOnASettableAutoProperty(),
            "abstract-slot-attributed-to-the-first-overload-a-type-parameter-agrees-with" => AbstractSlotAttributedToTheFirstOverloadATypeParameterAgreesWith(),
            "abstract-slot-attributed-to-another-conformances-with-mapping" => AbstractSlotAttributedToAnotherConformancesWithMapping(),
            "abstract-indexer-implemented-in-a-generic-base-class-is-never-found-so-the-slot-meets-to-pure" => AbstractIndexerImplementedInAGenericBaseClass(),
            "pure-non-defaultable-types-implicit-constructor-runs-unjudged-field-initializers" => PureNonDefaultableTypesImplicitConstructor(),
            "generic-extension-declaring-pure-tostring-is-trusted-while-the-runtime-objects-tostring-runs" => GenericExtensionDeclaringPureToString(),
            "abstract-default-methods-declared-promise-joins-the-defaults-inference-and-skips-the-implementations-meet" => AbstractDefaultMethodsDeclaredPromise(),
            "readonly-default-methods-replacing-member-is-never-held-to-readonly" => ReadonlyDefaultMethodsReplacingMember(),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private enum ElementKind
    {
        Equatable,
        Comparable,
        Clr,
    }

    private ClassType Element(ElementKind kind)
    {
        if(kind is ElementKind.Clr)
        {
            return HandBuiltModel.Class("Endpoint");
        }

        var contract = kind is ElementKind.Equatable ? "IEquatable<>" : "IComparable<>";
        var item = new ClassType("Item", "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>(40));
        item.BaseAbstractTypes.Add(new AbstractType(contract, "System", new Scope(null), null, null));
        _ = PureConstructor(item);
        var other = Input("other", item);
        _ = kind is ElementKind.Equatable
            ? Method(item, "Equals", boolean, Public, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(boolean, true), boolean)), other)
            : Method(item, "CompareTo", integer, Public, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 0L), integer)), other);
        return item;
    }

    private IReadOnlyList<Diagnostic> ListComparison(string member, ClassType element)
    {
        var list = model.Type("List<>");
        list.Modifiers = Public with { IsIntrinsic = true };
        var held = new TypeParameter("T", 0, list.Scope, list);
        list.TypeParametersList.Add(held);
        var fresh = new Constructor("List", list, [], Pure with { IsIntrinsic = true }) { DeclaringType = list };
        list.ConstructorsList.Add(fresh);
        var item = Input("item", held);
        var called = member == "Sort"
            ? Method(list, "Sort", unit, Local with { IsIntrinsic = true }, null)
            : Method(list, member, boolean, LocalReadonly with { IsIntrinsic = true }, null, item);
        var closed = model.Interner.GetOrCreateConstructedType(list, [element]);
        var l = new LocalVariable("l", closed);
        var probed = Input("probed", element);
        Expression[] arguments = member == "Sort" ? [] : [Ref(probed)];
        var compare = Method(probe, "Compare", unit, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(l, new NewExpression(Node(), closed, [], fresh, [], [])),
            new ExpressionStatement(Call(called, Ref(l), 37, arguments))), probed);
        return Judge(new PurityAnalyzer(), compare, element);
    }

    private IReadOnlyList<Diagnostic> AnonymousTypeEqualityDispatchesToIEquatable()
    {
        var item = Element(ElementKind.Equatable);
        var shape = new AnonymousType(["A"], [item]);
        var a = new LocalVariable("a", shape);
        var b = new LocalVariable("b", shape);
        var fresh = item.ConstructorsList[0];
        var same = Method(probe, "Same", boolean, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(a, new AnonymousObjectExpression(Node(), [new NewExpression(Node(), item, [], fresh, [], [])], shape)),
            new LocalDeclarationStatement(b, new AnonymousObjectExpression(Node(), [new NewExpression(Node(), item, [], fresh, [], [])], shape)),
            new ReturnStatement(new BinaryExpression(Node(37), Ref(a), BinaryOperatorKind.Equal, Ref(b), boolean), boolean)));
        return Judge(new PurityAnalyzer(), same, item);
    }

    private IReadOnlyList<Diagnostic> SlotImplementationContextClauseHiddenByAbstractDispatch()
    {
        var shape = Contract("IShape");
        var slot = Method(shape, "Area", integer, Public, null);
        var square = Implementer("Sq", shape);
        var area = Method(square, "Area", integer, Public, Returning(Literal(integer, 1L)));
        area.ContextBindingsList.Add(new ContextBinding(integer, "n"));
        var s = Input("s", shape);
        var measure = Method(probe, "Measure", integer, PureStatic, Returning(Call(slot, Ref(s), 37)), s);
        return Judge(new PurityAnalyzer(), measure, shape, square);
    }

    private IReadOnlyList<Diagnostic> DefaultOfADerivedTypeRunsTheBaseConstructor()
    {
        var root = HandBuiltModel.Class("Base");
        root.ConstructorsList.Add(new Constructor("Base", root, [], Public) { DeclaringType = root, Body = HandBuiltModel.Block(WriteStatic(ticks)) });
        root.DefaultBlock = new DefaultBlock([], DeclarationFragments.At<DefaultBlockDeclarationNode>());
        var derived = HandBuiltModel.Class("Derived", root);
        var d = new LocalVariable("d", derived);
        var make = Method(probe, "Make", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(d, new DefaultExpression(Node(37), derived)),
            new ReturnStatement(Literal(integer, 1L), integer)));
        return Judge(new PurityAnalyzer(), make);
    }

    private IReadOnlyList<Diagnostic> PureGetOnASettableAutoProperty()
    {
        var tally = HandBuiltModel.Class("Tally");
        tally.PropertiesList.Add(new Property("V", integer, Public)
        {
            DeclaringType = tally,
            DeclarationNode = Node(37),
            Getter = new PropertyAccessor { IsAuto = true, OwnModifiers = Pure },
            Setter = new PropertyAccessor { IsAuto = true },
        });
        return model.Analyze(new PurityAnalyzer(), types: [tally]);
    }

    private IReadOnlyList<Diagnostic> AbstractSlotAttributedToTheFirstOverloadATypeParameterAgreesWith()
    {
        var getter = Contract("IGet");
        var slot = Method(getter, "Get", integer, Public, null, Input("x", integer));
        var pair = Implementer("Pair<>", getter);
        var parameter = new TypeParameter("T", 0, pair.Scope, pair);
        pair.TypeParametersList.Add(parameter);
        _ = Method(pair, "Get", integer, Public, Returning(Literal(integer, 0L)), Input("x", parameter));
        _ = Method(pair, "Get", integer, Public, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 1L), integer)), Input("x", integer));
        var g = Input("g", getter);
        var call = Method(probe, "Call", integer, PureStatic, Returning(Call(slot, Ref(g), 37, Literal(integer, 5L))), g);
        return Judge(new PurityAnalyzer(), call, getter, pair);
    }

    private IReadOnlyList<Diagnostic> AbstractSlotAttributedToAnotherConformancesWithMapping()
    {
        var quiet = Contract("IQuiet");
        var quietSlot = Method(quiet, "Get", integer, Public, null);
        var loud = Contract("ILoud");
        var loudSlot = Method(loud, "Get", integer, Public, null);
        var thing = HandBuiltModel.Class("Thing");
        var calm = Method(thing, "Calm", integer, Public, Returning(Literal(integer, 1L)));
        var shout = Method(thing, "Shout", integer, Public, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 2L), integer)));
        thing.ConformancesList.Add(new ConformanceDeclaration(thing, quiet, "Probe", [new MethodMapping(calm, quietSlot, quiet)], [], Node()));
        thing.ConformancesList.Add(new ConformanceDeclaration(thing, loud, "Probe", [new MethodMapping(shout, loudSlot, loud)], [], Node()));
        var g = Input("g", loud);
        var call = Method(probe, "Call", integer, PureStatic, Returning(Call(loudSlot, Ref(g), 37)), g);
        return Judge(new PurityAnalyzer(), call, quiet, loud, thing);
    }

    private IReadOnlyList<Diagnostic> AbstractIndexerImplementedInAGenericBaseClass()
    {
        var indexed = Contract("IIdx");
        var slot = new Indexer(integer, [new Parameter("i", integer)], Public) { DeclaringType = indexed, Getter = new PropertyAccessor() };
        indexed.IndexersList.Add(slot);
        var root = HandBuiltModel.Class("Base<>");
        root.TypeParametersList.Add(new TypeParameter("T", 0, root.Scope, root));
        root.IndexersList.Add(new Indexer(integer, [new Parameter("i", integer)], Public)
        {
            DeclaringType = root,
            Getter = new PropertyAccessor { Body = HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 7L), integer)) },
        });
        var derived = HandBuiltModel.Class("Derived", model.Interner.GetOrCreateConstructedType(root, [text]));
        derived.BaseAbstractTypes.Add(indexed);
        var x = Input("x", indexed);
        var at = Method(probe, "At", integer, PureStatic,
            Returning(new IndexExpression(Node(37), Ref(x), Literal(integer, 7L), integer) { Indexer = slot }), x);
        return Judge(new PurityAnalyzer(), at, indexed, root, derived);
    }

    private IReadOnlyList<Diagnostic> PureNonDefaultableTypesImplicitConstructor()
    {
        var log = HandBuiltModel.Class("Log");
        var note = Method(log, "Note", integer, Static, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 1L), integer)));
        var holder = HandBuiltModel.Class("PBox<>");
        holder.Modifiers = Pure;
        var parameter = new TypeParameter("T", 0, holder.Scope, holder);
        holder.TypeParametersList.Add(parameter);
        _ = Field(holder, "Item", parameter);
        var stamp = Field(holder, "Stamp", integer);
        stamp.Initializer = Call(note, null, 37);
        return model.Analyze(new PurityAnalyzer(), types: [holder]);
    }

    private IReadOnlyList<Diagnostic> GenericExtensionDeclaringPureToString()
    {
        var parameter = new TypeParameter("T", 0, probe.Scope, probe);
        var block = new ExtensionBlock("self", "Probe", new Scope(null), null) { ResolvedReceiverType = parameter };
        var closed = new Method("ToString", text, [], Pure) { ClosedBlock = block };
        var x = Input("x", parameter);
        var show = Method(probe, "Text", text, PureStatic,
            Returning(new InvocationExpression(Node(37), new MemberAccessExpression(Node(), Ref(x), "ToString", text), [], text, closed, [])), x);
        show.TypeParametersList.Add(parameter);
        return Judge(new PurityAnalyzer(), show);
    }

    private IReadOnlyList<Diagnostic> AbstractDefaultMethodsDeclaredPromise()
    {
        var bump = Contract("IBump");
        var slot = Method(bump, "Bump", integer, Local, Returning(Literal(integer, 0L)));
        var live = Implementer("Live", bump);
        var count = Field(live, "Count", integer);
        _ = Method(live, "Bump", integer, Public, HandBuiltModel.Block(
            new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), count), Literal(integer, 1L))),
            new ReturnStatement(new FieldReferenceExpression(Node(), count), integer)));
        var p = Input("p", bump);
        var poke = Method(probe, "Poke", integer, PureStatic, Returning(Call(slot, Ref(p), 37)), p);
        return Judge(new PurityAnalyzer(), poke, bump, live);
    }

    private IReadOnlyList<Diagnostic> ReadonlyDefaultMethodsReplacingMember()
    {
        var bump = Contract("IBump");
        _ = Method(bump, "Bump", integer, Public with { IsReadonly = true }, Returning(Literal(integer, 0L)));
        var live = Implementer("Live", bump, declared: true);
        var count = Field(live, "Count", integer);
        _ = Method(live, "Bump", integer, Public, HandBuiltModel.Block(
            new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), count), Literal(integer, 1L))),
            new ReturnStatement(new FieldReferenceExpression(Node(), count), integer)));
        return model.Analyze(new ConformanceAnalyzer(), types: [bump, live]);
    }
}
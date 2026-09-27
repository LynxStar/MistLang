using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed partial class PurityFragments
{
    public IReadOnlyList<Diagnostic> RoundThree(string name)
    {
        return name switch
        {
            "owned-binding-initialized-from-an-unrestricted-reference" => OwnedBindingInitializedFrom(OwnershipKind.Unrestricted),
            "owned-binding-initialized-from-a-frozen-reference" => OwnedBindingInitializedFrom(OwnershipKind.Frozen),
            "owned-binding-reassigned-from-an-external" => OwnedBindingReassignedFromAnExternal(),
            "owned-binding-moved-from-an-owned-binding" => OwnedBindingMovedFromAnOwnedBinding(),
            "owned-method-parameter-fed-an-unrestricted-reference" => OwnedMethodParameterFedAnUnrestrictedReference(),
            "owned-constructor-parameter-fed-an-unrestricted-reference" => OwnedConstructorParameterFed(Input("ext", box)),
            "owned-constructor-parameter-fed-a-frozen-reference" => OwnedConstructorParameterFed(Input("f", box, OwnershipKind.Frozen)),
            "owned-constructor-parameter-fed-a-plain-local" => OwnedConstructorParameterFedALocal(OwnershipKind.Unrestricted),
            "owned-constructor-parameter-fed-an-owned-local" => OwnedConstructorParameterFedALocal(OwnershipKind.Owned),
            "owned-field-initializer-aliases-a-static" => OwnedFieldInitializer(unownedCall: false),
            "owned-field-initializer-aliases-an-unowned-return" => OwnedFieldInitializer(unownedCall: true),
            "default-entry-aliases-a-static-into-an-owned-field" => DefaultEntryAliasesAStaticIntoAnOwnedField(),
            "unmarked-method-stores-an-unrestricted-parameter-in-an-owned-field" => UnmarkedMethodStoresAnUnrestrictedParameterInAnOwnedField(),
            "ref-local-over-a-ref-returning-call-on-a-frozen-receiver" => RefReturningCall(OwnershipKind.Frozen, throughLocal: true),
            "write-through-a-ref-returning-call-on-a-frozen-receiver" => RefReturningCall(OwnershipKind.Frozen, throughLocal: false),
            "ref-local-over-a-ref-returning-call-on-an-owned-receiver" => RefReturningCall(OwnershipKind.Owned, throughLocal: true),
            "pure-method-advances-an-intrinsic-enumerator-copy" => PureMethodAdvancesAnIntrinsicEnumeratorCopy(),
            "pure-method-writes-its-params-array" => PureMethodWritesItsParamsArray(),
            "pure-method-stores-a-readonly-class-holding-an-owned-mutable-field" => PureMethodStoresAReadonlyClass(Holding(ownedBox: true)),
            "pure-method-stores-a-readonly-class-with-a-settable-property" => PureMethodStoresAReadonlyClass(Settable()),
            "pure-method-stores-a-readonly-class-of-stable-values" => PureMethodStoresAReadonlyClass(Holding(ownedBox: false)),
            "owned-return-of-a-static-readonly-class-holding-an-owned-mutable-field" => OwnedReturnOfAStaticReadonlyClass(),
            "pure-method-allocates-a-defaultable-type-chaining-to-an-impure-base" => AllocatesADefaultableTypeChainingToAnImpureBase(new PurityAnalyzer(), PureStatic),
            "readonly-method-allocates-a-defaultable-type-chaining-to-an-impure-base" => AllocatesADefaultableTypeChainingToAnImpureBase(new ReadonlyAnalyzer(), ReadonlyStatic),
            "pure-method-reads-a-static-whose-initializer-is-impure" => PureMethodReadsAStaticInitializedBy(impure: true),
            "pure-method-reads-a-static-whose-initializer-is-pure" => PureMethodReadsAStaticInitializedBy(impure: false),
            "pure-method-calls-a-member-reading-an-undeclared-context" => PureMethodCallsAContextReader(declared: false),
            "pure-method-declaring-the-context-calls-a-member-reading-it" => PureMethodCallsAContextReader(declared: true),
            "plain-function-made-ambient-under-a-pure-function-key" => PlainFunctionMadeAmbientUnderAPureFunctionKey(),
            "pure-method-passes-params-elements-through-an-impure-conversion" => PureMethodPassesParamsElementsThroughAnImpureConversion(),
            "pure-method-holds-a-linear-whose-drop-hands-back-an-impure-linear" => PureMethodHoldsALinearWhoseDropHandsBackAnImpureLinear(),
            "declared-pure-drop-owes-an-impure-field-drop" => DeclaredPureDropOwesAnImpureFieldDrop(OwnershipKind.Unrestricted),
            "declared-pure-drop-owes-an-impure-owned-field-drop" => DeclaredPureDropOwesAnImpureFieldDrop(OwnershipKind.Owned),
            "pure-method-drops-a-generic-container-of-an-impure-linear" => PureMethodDropsAGenericContainerOfAnImpureLinear(),
            "pure-type-settable-auto-property" => PureTypeAutoProperty(new PropertyAccessor { IsAuto = true }),
            "pure-type-init-only-auto-property" => PureTypeAutoProperty(new PropertyAccessor { IsAuto = true, IsInit = true }),
            "pure-method-calls-a-universal-tostring" => PureMethodCallsAUniversalToString(),
            "pure-method-interpolates-a-clr-reference" => PureMethodInterpolates(new ClassType("StringBuilder", "System.Text", new Scope(null), null, null)),
            "pure-method-interpolates-a-type-declared-in-mist" => PureMethodInterpolates(
                new ClassType("Plain", "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>(37))),
            "pure-generic-allocates-its-type-parameter" => GenericAllocatesItsTypeParameter(new PurityAnalyzer(), PureStatic),
            "readonly-generic-allocates-its-type-parameter" => GenericAllocatesItsTypeParameter(new ReadonlyAnalyzer(), ReadonlyStatic),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    public string EmittedContextKey(bool pure)
    {
        var plain = new LocalVariable("g", Function(pure: false, integer, integer));
        var ambient = new WithBlockStatement(Ref(plain), HandBuiltModel.Block(), binding: null, keyType: Function(pure, integer, integer));
        var writer = new CSharpWriter();
        BodyEmitter.EmitBlock(HandBuiltModel.Block(ambient), writer, new DiagnosticBag());
        return writer.ToString();
    }

    private IReadOnlyList<Diagnostic> OwnedBindingInitializedFrom(OwnershipKind tier)
    {
        var ext = Input("ext", box, tier);
        var b = new LocalVariable("b", box) { Ownership = OwnershipKind.Owned };
        var bump = Method(probe, "Bump", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(b, Ref(ext, 37)),
            new ExpressionStatement(Assign(Access(Ref(b), n), Literal(integer, 1L))),
            new ReturnStatement(Access(Ref(b), n), integer)), ext);
        return Judge(new OwnershipAnalyzer(), bump);
    }

    private IReadOnlyList<Diagnostic> OwnedBindingReassignedFromAnExternal()
    {
        var ext = Input("ext", box);
        var b = new LocalVariable("b", box) { Ownership = OwnershipKind.Owned };
        var bump = Method(probe, "Bump", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(b, new NewExpression(Node(), box, [], box.ConstructorsList[0], [], [])),
            new ExpressionStatement(Assign(Ref(b), Ref(ext, 37))),
            new ReturnStatement(Access(Ref(b), n), integer)), ext);
        return Judge(new OwnershipAnalyzer(), bump);
    }

    private IReadOnlyList<Diagnostic> OwnedBindingMovedFromAnOwnedBinding()
    {
        var a = new LocalVariable("a", box) { Ownership = OwnershipKind.Owned };
        var b = new LocalVariable("b", box) { Ownership = OwnershipKind.Owned };
        var make = Method(probe, "Make", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(a, new NewExpression(Node(), box, [], box.ConstructorsList[0], [], [])),
            new LocalDeclarationStatement(b, Ref(a)),
            new ReturnStatement(Access(Ref(b), n), integer)));
        return Judge(new OwnershipAnalyzer(), make);
    }

    private IReadOnlyList<Diagnostic> OwnedMethodParameterFedAnUnrestrictedReference()
    {
        var kept = Input("b", box, OwnershipKind.Owned);
        var keep = Method(probe, "Keep", integer, PureStatic, Returning(Access(Ref(kept), n)), kept);
        var ext = Input("ext", box);
        var bump = Method(probe, "Bump", integer, PureStatic, Returning(Call(keep, null, 0, Ref(ext, 37))), ext);
        return Judge(new OwnershipAnalyzer(), bump);
    }

    private IReadOnlyList<Diagnostic> OwnedConstructorParameterFed(LocalVariable source)
    {
        var (holder, constructor) = OwningHolder();
        var h = new LocalVariable("h", holder);
        var bump = Method(probe, "Bump", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(h, new NewExpression(Node(), holder, [Ref(source, 37)], constructor, [], [box])),
            new ReturnStatement(Literal(integer, 1L), integer)), source);
        return Judge(new OwnershipAnalyzer(), bump);
    }

    private IReadOnlyList<Diagnostic> OwnedConstructorParameterFedALocal(OwnershipKind tier)
    {
        var (holder, constructor) = OwningHolder();
        var b = new LocalVariable("b", box) { Ownership = tier };
        var h = new LocalVariable("h", holder);
        var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block(
            new LocalDeclarationStatement(b, new NewExpression(Node(), box, [], box.ConstructorsList[0], [], [])),
            new LocalDeclarationStatement(h, new NewExpression(Node(), holder, [Ref(b, 37)], constructor, [], [box]))));
        return Judge(new OwnershipAnalyzer(), main);
    }

    private IReadOnlyList<Diagnostic> OwnedFieldInitializer(bool unownedCall)
    {
        var holder = HandBuiltModel.Class("Holder");
        var shared = Field(holder, "Shared", box, isStatic: true, isReadonly: true);
        var peek = Method(holder, "Peek", box, PureStatic, Returning(StaticRead(shared)));
        var inner = Field(holder, "Inner", box, ownership: OwnershipKind.Owned);
        inner.Initializer = unownedCall ? Call(peek, null, 37) : StaticRead(shared, 37);
        _ = PureConstructor(holder);
        return model.Analyze(new OwnershipAnalyzer(), inner.Initializer, [holder], holder, inner);
    }

    private IReadOnlyList<Diagnostic> DefaultEntryAliasesAStaticIntoAnOwnedField()
    {
        var holder = HandBuiltModel.Class("Holder");
        var shared = Field(holder, "Shared", box, isStatic: true, isReadonly: true);
        _ = Field(holder, "Inner", box, ownership: OwnershipKind.Owned);
        holder.DefaultBlock = new DefaultBlock([new DefaultValueEntry("Inner", StaticRead(shared, 37))], new DefaultBlockDeclarationNode());
        return model.Analyze(new OwnershipAnalyzer(), types: [holder]);
    }

    private IReadOnlyList<Diagnostic> UnmarkedMethodStoresAnUnrestrictedParameterInAnOwnedField()
    {
        var node = HandBuiltModel.Class("Node");
        var slot = Field(node, "B", box, ownership: OwnershipKind.Owned);
        var x = Input("x", box);
        var take = Method(node, "Take", unit, Public, HandBuiltModel.Block(new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), slot), Ref(x, 37)))), x);
        return Judge(new OwnershipAnalyzer(), take);
    }

    private IReadOnlyList<Diagnostic> RefReturningCall(OwnershipKind tier, bool throughLocal)
    {
        var slotted = HandBuiltModel.Class("Slotted");
        var number = HandBuiltModel.Struct("Num");
        var stored = Field(slotted, "N", number);
        var fresh = PureConstructor(slotted);
        var slot = Method(slotted, "Slot", number, LocalReadonly with { IsRef = true }, Returning(new FieldReferenceExpression(Node(), stored)));
        var c = tier is OwnershipKind.Owned ? new LocalVariable("c", slotted) { Ownership = tier } : Input("c", slotted, tier);
        var r = new LocalVariable("r", number) { IsRef = true };
        var statements = new List<Statement>();

        if(tier is OwnershipKind.Owned)
        {
            statements.Add(new LocalDeclarationStatement(c, new NewExpression(Node(), slotted, [], fresh, [], [])));
        }

        if(throughLocal)
        {
            statements.Add(new LocalDeclarationStatement(r, Call(slot, Ref(c), 0)));
            statements.Add(new ExpressionStatement(Assign(Ref(r, 37), Literal(number))));
        }
        else
        {
            statements.Add(new ExpressionStatement(Assign(Call(slot, Ref(c), 37), Literal(number))));
        }

        statements.Add(new ReturnStatement(Literal(integer, 1L), integer));
        LocalVariable[] inputs = tier is OwnershipKind.Owned ? [] : [c];
        var poke = Method(probe, "Poke", integer, PureStatic, HandBuiltModel.Block([.. statements]), inputs);
        return Judge(new PurityAnalyzer(), poke);
    }

    private IReadOnlyList<Diagnostic> PureMethodAdvancesAnIntrinsicEnumeratorCopy()
    {
        var enumerator = new StructType("Enumerator", "Standard", new Scope(null), null, null) { Modifiers = Public with { IsIntrinsic = true } };
        var moveNext = Method(enumerator, "MoveNext", boolean, Local with { IsIntrinsic = true }, null);
        var e = Input("e", enumerator);
        var peek = Method(probe, "Peek", boolean, PureStatic, Returning(Call(moveNext, Ref(e), 37)), e);
        return Judge(new PurityAnalyzer(), peek);
    }

    private IReadOnlyList<Diagnostic> PureMethodWritesItsParamsArray()
    {
        var xs = Input("xs", ArrayOf(integer));
        xs.Parameter!.IsFlattened = true;
        var fill = Method(probe, "Fill", integer, PureStatic, HandBuiltModel.Block(
            new ExpressionStatement(Assign(new IndexExpression(Node(37), Ref(xs), Literal(integer, 0L), integer), Literal(integer, 1L))),
            new ReturnStatement(Literal(integer, 1L), integer)), xs);
        return Judge(new PurityAnalyzer(), fill);
    }

    private ClassType Holding(bool ownedBox)
    {
        var wrap = HandBuiltModel.Class("Wrap");
        wrap.Modifiers = Public with { IsReadonly = true };
        _ = ownedBox ? Field(wrap, "Inner", box, ownership: OwnershipKind.Owned) : Field(wrap, "Size", integer, isReadonly: true);
        return wrap;
    }

    private ClassType Settable()
    {
        var gauge = HandBuiltModel.Class("Gauge");
        gauge.Modifiers = Public with { IsReadonly = true };
        gauge.PropertiesList.Add(new Property("Level", integer, Public)
        {
            DeclaringType = gauge,
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
        });
        return gauge;
    }

    private IReadOnlyList<Diagnostic> PureMethodStoresAReadonlyClass(ClassType stored)
    {
        var holder = HandBuiltModel.Class("Holder");
        var slot = Field(holder, "W", stored, ownership: OwnershipKind.Owned);
        var ext = Input("ext", stored);
        var h = new LocalVariable("h", holder);
        var bump = Method(probe, "Bump", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(h, new NewExpression(Node(), holder, [], PureConstructor(holder), [], [])),
            new ExpressionStatement(Assign(Access(Ref(h), slot), Ref(ext, 37))),
            new ReturnStatement(Literal(integer, 1L), integer)), ext);
        return Judge(new PurityAnalyzer(), bump);
    }

    private IReadOnlyList<Diagnostic> OwnedReturnOfAStaticReadonlyClass()
    {
        var wrap = Holding(ownedBox: true);
        var registry = HandBuiltModel.Class("Registry");
        var shared = Field(registry, "Shared", wrap, isStatic: true, isReadonly: true);
        var get = new Method("Get", wrap, [], PureStatic)
        {
            DeclaringType = registry,
            ReturnOwnership = OwnershipKind.Owned,
            Body = Returning(StaticRead(shared, 37)),
        };
        registry.MethodsList.Add(get);
        return Judge(new OwnershipAnalyzer(), get);
    }

    private IReadOnlyList<Diagnostic> AllocatesADefaultableTypeChainingToAnImpureBase(IProgramAnalyzer analyzer, MemberModifiers modifiers)
    {
        var loud = HandBuiltModel.Class("Base");
        loud.ConstructorsList.Add(new Constructor("Base", loud, [], Public) { DeclaringType = loud, Body = HandBuiltModel.Block(WriteStatic(ticks)) });
        var derived = HandBuiltModel.Class("Derived", loud);
        var d = new LocalVariable("d", derived);
        var make = Method(probe, "Make", integer, modifiers, HandBuiltModel.Block(
            new LocalDeclarationStatement(d, new NewExpression(Node(37), derived, [], null, [], null)),
            new ReturnStatement(Literal(integer, 1L), integer)));
        return Judge(analyzer, make);
    }

    private IReadOnlyList<Diagnostic> PureMethodReadsAStaticInitializedBy(bool impure)
    {
        var seed = HandBuiltModel.Class("Seed");
        var compute = Method(seed, "Compute", integer, Static, HandBuiltModel.Block(
            impure ? WriteStatic(ticks) : new ExpressionStatement(Literal(integer, 0L)),
            new ReturnStatement(Literal(integer, 7L), integer)));
        var value = Field(seed, "Value", integer, isStatic: true, isReadonly: true);
        value.Initializer = Call(compute, null, 0);
        var read = Method(probe, "Read", integer, PureStatic, Returning(StaticRead(value, 37)));
        return Judge(new PurityAnalyzer(), read);
    }

    private IReadOnlyList<Diagnostic> PureMethodCallsAContextReader(bool declared)
    {
        var inner = Method(probe, "Inner", integer, Static, Returning(Literal(integer, 1L)));
        inner.ContextBindingsList.Add(new ContextBinding(integer, "n"));
        var outer = Method(probe, "Outer", integer, PureStatic, Returning(Call(inner, null, 37)));

        if(declared)
        {
            outer.ContextBindingsList.Add(new ContextBinding(integer, "m"));
        }

        return Judge(new PurityAnalyzer(), outer);
    }

    private IReadOnlyList<Diagnostic> PlainFunctionMadeAmbientUnderAPureFunctionKey()
    {
        var promised = Function(pure: true, integer, integer);
        var plain = new LocalVariable("g", Function(pure: false, integer, integer));
        var ambient = new WithBlockStatement(Ref(plain, 37), HandBuiltModel.Block(), new LocalVariable("s", promised), promised);
        return model.Analyze(new AssignabilityAnalyzer(), ambient);
    }

    private IReadOnlyList<Diagnostic> PureMethodPassesParamsElementsThroughAnImpureConversion()
    {
        var fahrenheit = HandBuiltModel.Class("Fahrenheit");
        var celsius = HandBuiltModel.Class("Celsius");
        celsius.CastOperatorsList.Add(new CastOperator(fahrenheit, new Parameter("c", celsius), true, Static)
        {
            DeclaringType = celsius,
            Body = HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(fahrenheit), fahrenheit)),
        });
        var fs = Input("fs", ArrayOf(fahrenheit));
        fs.Parameter!.IsFlattened = true;
        var count = Method(probe, "Count", integer, PureStatic, Returning(Literal(integer, 2L)), fs);
        var c = Input("c", celsius);
        var arguments = ImplicitConversions.AtParameters([Ref(c, 37), Ref(c, 38)], count, [fs.Type], model.Interner);
        var use = Method(probe, "Use", integer, PureStatic,
            Returning(new InvocationExpression(Node(), new MethodReferenceExpression(Node(), count), arguments, integer, count, [fs.Type])), c);
        return Judge(new PurityAnalyzer(), use);
    }

    private IReadOnlyList<Diagnostic> PureMethodHoldsALinearWhoseDropHandsBackAnImpureLinear()
    {
        var boxed = HandBuiltModel.Class("Boxed");
        boxed.Modifiers = Public with { Discipline = SubstructuralDiscipline.Linear };
        var held = Field(boxed, "Inner", handle, isReadonly: true);
        var fresh = PureConstructor(boxed);
        boxed.MethodsList.Add(new Method("Drop", handle, [], Pure) { DeclaringType = boxed, Body = Returning(new FieldReferenceExpression(Node(), held)) });
        var b = new LocalVariable("b", boxed);
        var use = Method(probe, "Use", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(b, new NewExpression(Node(37), boxed, [], fresh, [], [])),
            new ReturnStatement(Literal(integer, 1L), integer)));
        return Judge(new PurityAnalyzer(), use);
    }

    private IReadOnlyList<Diagnostic> DeclaredPureDropOwesAnImpureFieldDrop(OwnershipKind tier)
    {
        var session = HandBuiltModel.Class("Session");
        session.Modifiers = Public with { Discipline = SubstructuralDiscipline.Linear };
        _ = Field(session, "H", handle, ownership: tier);
        _ = PureConstructor(session);
        var drop = new Method("Drop", unit, [], Pure) { DeclaringType = session, DeclarationNode = Node(37), Body = HandBuiltModel.Block() };
        session.MethodsList.Add(drop);
        return Judge(new PurityAnalyzer(), drop);
    }

    private IReadOnlyList<Diagnostic> PureMethodDropsAGenericContainerOfAnImpureLinear()
    {
        var container = HandBuiltModel.Class("Gen<>");
        container.Modifiers = Public with { Discipline = SubstructuralDiscipline.Linear };
        var element = new TypeParameter("T", 0, container.Scope, container);
        element.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Linear));
        container.TypeParametersList.Add(element);
        _ = Field(container, "Item", element);
        var g = Input("g", model.Interner.GetOrCreateConstructedType(container, [handle]));
        var use = new Method("Use", integer, [g.Parameter!], PureStatic) { DeclaringType = probe, DeclarationNode = Node(37), Body = Returning(Literal(integer, 1L)) };
        probe.MethodsList.Add(use);
        return Judge(new PurityAnalyzer(), use);
    }

    private IReadOnlyList<Diagnostic> PureTypeAutoProperty(PropertyAccessor setter)
    {
        var stamped = HandBuiltModel.Class("P");
        stamped.Modifiers = Pure;
        stamped.PropertiesList.Add(new Property("X", integer, Pure)
        {
            DeclaringType = stamped,
            DeclarationNode = Node(37),
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = setter,
        });
        return model.Analyze(new PurityAnalyzer(), types: [stamped]);
    }

    private IReadOnlyList<Diagnostic> PureMethodCallsAUniversalToString()
    {
        var universal = new ExtensionBlock("self", "Standard", new Scope(null), null) { ResolvedReceiverType = UnknownType.Instance };
        var toString = new Method("ToString", text, [], Public) { Body = Returning(Literal(text, "")) };
        universal.MethodsList.Add(toString);
        var v = Input("v", UnknownType.Instance);
        var show = Method(probe, "Show", text, PureStatic,
            Returning(new InvocationExpression(Node(37), new MemberAccessExpression(Node(), Ref(v), "ToString", text), [], text, toString, [])), v);
        return model.Analyze(new PurityAnalyzer(), show.Body, [], probe, show, [universal]);
    }

    private IReadOnlyList<Diagnostic> PureMethodInterpolates(ClassType held)
    {
        var value = Input("value", held);
        var count = Input("count", integer);
        var interpolated = new InterpolatedStringExpression(Node(),
            [new InterpolatedStringText("<"), new InterpolatedStringHole(Ref(value, 37)), new InterpolatedStringHole(Ref(count)), new InterpolatedStringText(">")], text);
        var show = Method(probe, "Show", text, PureStatic, Returning(interpolated), value, count);
        return Judge(new PurityAnalyzer(), show);
    }

    private IReadOnlyList<Diagnostic> GenericAllocatesItsTypeParameter(IProgramAnalyzer analyzer, MemberModifiers modifiers)
    {
        var parameter = new TypeParameter("T", 0, probe.Scope, probe);
        var make = new Method("Make", parameter, [], modifiers)
        {
            DeclaringType = probe,
            Body = Returning(new NewExpression(Node(37), parameter, [], null, [], null)),
        };
        make.TypeParametersList.Add(parameter);
        probe.MethodsList.Add(make);
        return Judge(analyzer, make);
    }

    private (ClassType Holder, Constructor Constructor) OwningHolder()
    {
        var holder = HandBuiltModel.Class("Holder");
        var inner = Field(holder, "Inner", box, ownership: OwnershipKind.Owned);
        var taken = Input("inner", box, OwnershipKind.Owned);
        var constructor = new Constructor("Holder", holder, [taken.Parameter!], Pure)
        {
            DeclaringType = holder,
            Body = HandBuiltModel.Block(new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), inner), Ref(taken)))),
        };
        holder.ConstructorsList.Add(constructor);
        return (holder, constructor);
    }

    private ConstructedType ArrayOf(IMistType element)
    {
        return model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [element, model.Interner.GetOrCreateUnknownValue(integer)]);
    }
}
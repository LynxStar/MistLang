using Mist.Common.Diagnostics;
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

internal sealed partial class PuritySoundnessFragments
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MemberModifiers Pure = Public with { IsPure = true, IsReadonly = true };
    private static readonly MemberModifiers PureStatic = Pure with { IsStatic = true };
    private static readonly MemberModifiers Local = Public with { IsLocal = true };
    private static readonly MemberModifiers LocalReadonly = Local with { IsReadonly = true };
    private static readonly MemberModifiers Readonly = Public with { IsReadonly = true };
    private static readonly MemberModifiers ReadonlyStatic = Readonly with { IsStatic = true };
    private static readonly MemberModifiers Static = Public with { IsStatic = true };

    private readonly HandBuiltModel model = new();
    private readonly ClassType integer;
    private readonly ClassType text;
    private readonly ClassType boolean;
    private readonly ClassType unit;
    private readonly Operator plus;
    private readonly ClassType probe = HandBuiltModel.Class("Probe");
    private readonly ClassType clock = HandBuiltModel.Class("Clock");
    private readonly Field ticks;
    private readonly ClassType bag = HandBuiltModel.Class("Bag");
    private readonly Field count;
    private readonly Constructor bagConstructor;
    private readonly ClassType box = HandBuiltModel.Class("Box");
    private readonly Field n;
    private readonly Constructor boxConstructor;
    private readonly StructType pt = HandBuiltModel.Struct("Pt");
    private readonly Field x;
    private readonly Constructor ptConstructor;

    public PuritySoundnessFragments()
    {
        integer = Primitive("Int32");
        text = Primitive("String");
        boolean = Primitive("Boolean");
        unit = Primitive("Void");
        plus = new Operator(OperatorKind.Plus, integer, [new Parameter("a", integer), new Parameter("b", integer)], Pure with { IsStatic = true, IsIntrinsic = true })
        {
            DeclaringType = integer,
        };
        integer.OperatorsList.Add(plus);
        ticks = Field(clock, "Ticks", integer, Static);
        count = Field(bag, "Count", integer, Public);
        bagConstructor = Constructor(bag, Pure);
        n = Field(box, "N", integer, Public);
        boxConstructor = Constructor(box, Pure);
        x = Field(pt, "X", integer, Public);
        var start = Input("x", integer);
        ptConstructor = Constructor(pt, Public, HandBuiltModel.Block(Set(Bare(x), Ref(start))), start);
    }

    public IReadOnlyList<Diagnostic> Analyze(string name)
    {
        return name switch
        {
            "pure-method-writes-through-an-owned-field-of-a-struct-parameter" => PureMethodWritesThroughAnOwnedFieldOfAStructParameter(),
            "pure-method-writes-the-result-of-a-clone-returning-this" => PureMethodWritesTheResultOfACloneReturningThis(),
            "field-initializer-judged-under-a-pure-constructor" => FieldInitializerJudgedUnderAPureConstructor(),
            "pure-method-allocates-through-an-unmarked-constructor-with-an-impure-initializer" => PureMethodAllocatesThroughAnUnmarkedConstructorWithAnImpureInitializer(),
            "pure-local-function-reads-a-captured-mutable-object" => PureLocalFunctionReadsACapturedMutableObject(),
            "local-function-reading-a-captured-mutable-object-to-pure-func" => LocalFunctionReadingACapturedMutableObjectToPureFunc(),
            "pure-lambda-captures-a-struct-assigned-after-the-capture" => PureLambdaCapturesAStruct(assignedBefore: false, assignedAfter: true),
            "pure-lambda-captures-a-struct-assigned-before-the-capture" => PureLambdaCapturesAStruct(assignedBefore: true, assignedAfter: false),
            "pure-lambda-captures-a-struct-nothing-assigns" => PureLambdaCapturesAStruct(assignedBefore: false, assignedAfter: false),
            "pure-method-sets-an-extension-property-whose-setter-writes-a-static" => PureMethodSetsAnExtensionPropertyWhoseSetterWritesAStatic(),
            "pure-caller-of-an-unmarked-method-allocating-through-an-implicit-constructor" => PureCallerOfAnUnmarkedMethodAllocatingThroughAnImplicitConstructor(Plain()),
            "pure-method-allocates-through-an-implicit-constructor-with-pure-initializers" => PureMethodAllocatesThroughAnImplicitConstructor(Seeded("Calm", Lit(3))),
            "pure-caller-of-an-unmarked-method-allocating-through-an-implicit-constructor-with-pure-initializers" => PureCallerOfAnUnmarkedMethodAllocatingThroughAnImplicitConstructor(Seeded("Calm", Lit(3))),
            "pure-caller-of-an-unmarked-method-allocating-through-an-implicit-constructor-with-an-impure-initializer" => PureCallerOfAnUnmarkedMethodAllocatingThroughAnImplicitConstructor(Seeded("Bare", StaticRead(ticks))),
            "readonly-block-inside-a-pure-body" => ReadonlyBlockInsideAPureBody(new PurityAnalyzer()),
            "readonly-block-inside-a-pure-body-under-the-readonly-rules" => ReadonlyBlockInsideAPureBody(new ReadonlyAnalyzer()),
            "static-property-initializer-lambda-converting-to-a-pure-function-type" => StaticPropertyInitializerLambda(WritingLambda),
            "instance-field-initializer-lambda-converting-to-a-pure-function-type" => InstanceFieldInitializerLambda(WritingLambda),
            "static-field-initializer-pure-lambda" => StaticFieldInitializerLambda(QuietLambda),
            "instance-field-initializer-pure-lambda" => InstanceFieldInitializerLambda(QuietLambda),
            "settable-property-initializer-pure-lambda" => SettablePropertyInitializerLambda(QuietLambda),
            "pure-lambda-argument-to-a-params-parameter-writes-a-static" => LambdasPassedThroughAParamsParameter(WritingLambda),
            "pure-lambda-arguments-to-a-params-parameter" => LambdasPassedThroughAParamsParameter(QuietLambda),
            "pure-method-iterates-its-own-params-array" => PureMethodIteratesAnArrayParameter(flattened: true),
            "pure-method-iterates-an-ordinary-array-parameter" => PureMethodIteratesAnArrayParameter(flattened: false),
            "implicit-clone-of-an-external-object" => ImplicitCloneOfAnExternalObject(),
            "implicit-clone-of-a-frozen-input-through-a-clone-reading-only-its-source" => ImplicitCloneOfAFrozenInputThroughACloneReadingOnlyItsSource(),
            "struct-local-readonly-member-reads-through-an-owned-field-of-a-copy" => StructLocalReadonlyMemberReadsThroughAnOwnedFieldOfACopy(),
            "pure-method-calls-an-unmarked-struct-member-writing-through-an-owned-field" => CallsAnUnmarkedStructMemberWritingThroughAnOwnedField(new PurityAnalyzer(), PureStatic),
            "readonly-method-calls-an-unmarked-struct-member-writing-through-an-owned-field" => CallsAnUnmarkedStructMemberWritingThroughAnOwnedField(new ReadonlyAnalyzer(), ReadonlyStatic),
            "struct-local-member-writes-the-copys-own-value-field" => Judge(new PurityAnalyzer(), CountingCell().Bump),
            "struct-local-readonly-member-reads-the-copys-own-value-field" => Judge(new PurityAnalyzer(), CountingCell().Look),
            "pure-method-calls-a-structs-local-members-on-a-parameter" => PureMethodCallsAStructsLocalMembersOnAParameter(),
            "readonly-method-reads-an-abstract-auto-getter-answered-by-an-impure-implementation" => ReadonlyMethodReadsAnAbstractAutoGetterAnsweredByAnImpureImplementation(),
            "pure-method-sets-an-abstract-auto-setter-answered-by-an-impure-implementation" => PureMethodSetsAnAbstractAutoSetterAnsweredByAnImpureImplementation(),
            "pure-method-reads-an-abstract-auto-getter-answered-by-pure-implementations" => PureMethodReadsAnAbstractAutoGetterAnsweredByPureImplementations(),
            "pure-method-calls-an-abstract-default-body-no-conformer-replaces" => PureMethodCallsAnAbstractDefaultBody(replaced: false),
            "pure-method-calls-an-abstract-default-body-replaced-by-a-pure-implementation" => PureMethodCallsAnAbstractDefaultBody(replaced: true),
            "owned-slot-answered-by-an-owned-return" => OwnedSlotAnsweredByAnOwnedReturn(),
            "pure-method-writes-the-owned-result-of-an-owned-slot" => PureMethodWritesTheOwnedResultOfAnOwnedSlot(),
            "generic-as-instantiated-with-a-pure-function-type" => GenericClosing(asExpression: true, Function(pure: true, integer)),
            "generic-type-test-instantiated-with-int" => GenericClosing(asExpression: false, integer),
            "generic-as-instantiated-with-string" => GenericClosing(asExpression: true, text),
            "readonly-type-field-backed-settable-property-read-through-an-unrestricted-reference" => ReadonlyTypeFieldBackedSettablePropertyReadThroughAnUnrestrictedReference(),
            "readonly-type-settable-property-read-through-a-frozen-reference" => ReadonlyTypePropertyRead(new PropertyAccessor { IsAuto = true }, OwnershipKind.Frozen),
            "readonly-type-init-only-property-read-through-an-unrestricted-reference" => ReadonlyTypePropertyRead(new PropertyAccessor { IsAuto = true, IsInit = true }, OwnershipKind.Unrestricted),
            "abstract-local-indexer-implemented-with-an-impure-setter" => AbstractLocalIndexerImplemented(HandBuiltModel.Block(Set(StaticRead(ticks), Lit(1)))),
            "abstract-local-indexer-implemented-locally" => AbstractLocalIndexerImplemented(null),
            "abstract-pure-indexer-implemented-purely" => AbstractPureIndexerImplementedPurely(),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private IReadOnlyList<Diagnostic> PureMethodWritesThroughAnOwnedFieldOfAStructParameter()
    {
        var wrap = HandBuiltModel.Struct("Wrap");
        var inner = Field(wrap, "Inner", bag, Public, OwnershipKind.Owned);
        var w = Input("w", wrap);
        var m = Method(probe, "M", integer, Pure, HandBuiltModel.Block(Set(Access(Access(Ref(w), inner), count), Lit(1)), Return(Lit(1))), w);
        return Judge(new PurityAnalyzer(), m);
    }

    private IReadOnlyList<Diagnostic> PureMethodWritesTheResultOfACloneReturningThis()
    {
        var twin = HandBuiltModel.Class("Twin");
        var stored = Field(twin, "N", integer, Public);
        var clone = Method(twin, "Clone", twin, Public, Returning(new ThisExpression(Node(), twin)));
        var t = Input("t", twin);
        var c = new LocalVariable("c", twin);
        var m = Method(probe, "M", integer, Pure, HandBuiltModel.Block(Declare(c, Call(clone, Ref(t))), Set(Access(Ref(c), stored), Lit(1)), Return(Lit(1))), t);
        return Judge(new PurityAnalyzer(), m);
    }

    private IReadOnlyList<Diagnostic> FieldInitializerJudgedUnderAPureConstructor()
    {
        var holder = Seeded("Box", StaticRead(ticks));
        var constructor = Constructor(holder, Pure, null, Input("k", integer));
        return model.Analyze(new PurityAnalyzer(), constructor.Body, [holder], holder, constructor);
    }

    private IReadOnlyList<Diagnostic> PureMethodAllocatesThroughAnUnmarkedConstructorWithAnImpureInitializer()
    {
        var holder = Seeded("Box", StaticRead(ticks));
        var constructor = Constructor(holder, Public, null, Input("k", integer));
        var b = new LocalVariable("b", holder);
        var make = Method(probe, "Make", integer, PureStatic, HandBuiltModel.Block(Declare(b, New(holder, constructor, Lit(1))), Return(Access(Ref(b), holder.FieldsList[0]))));
        return Judge(new PurityAnalyzer(), make);
    }

    private IReadOnlyList<Diagnostic> PureLocalFunctionReadsACapturedMutableObject()
    {
        var b = new LocalVariable("b", bag);
        var function = new Method("F", integer, [], Pure) { Body = Returning(Access(Ref(b), count)) };
        var m = Method(probe, "M", integer, Public, HandBuiltModel.Block(FreshBag(b), new LocalFunctionStatement(function, []), Return(Call(function, null))));
        return Judge(new PurityAnalyzer(), m);
    }

    private IReadOnlyList<Diagnostic> LocalFunctionReadingACapturedMutableObjectToPureFunc()
    {
        var b = new LocalVariable("b", bag);
        var function = new Method("F", integer, [], Public) { Body = Returning(Access(Ref(b), count)) };
        var g = new LocalVariable("g", Function(pure: true, integer));
        var m = Method(probe, "M", integer, Public, HandBuiltModel.Block(
            FreshBag(b),
            new LocalFunctionStatement(function, []),
            Declare(g, new FunctionReferenceExpression(Node(), null, function, g.Type)),
            Return(Invoke(g))));
        return Judge(new PurityAnalyzer(), m);
    }

    private IReadOnlyList<Diagnostic> PureLambdaCapturesAStruct(bool assignedBefore, bool assignedAfter)
    {
        var p = new LocalVariable("p", pt);
        var reader = Function(pure: true, integer);
        var f = new LocalVariable("f", reader);
        var statements = new List<Statement> { Declare(p, New(pt, ptConstructor, Lit(1))) };

        if(assignedBefore)
        {
            statements.Add(Set(Access(Ref(p), x), Lit(5)));
        }

        statements.Add(Declare(f, new LambdaExpression(Node(), [], Access(Ref(p), x), null, false, reader)));

        if(assignedAfter)
        {
            statements.Add(Set(Access(Ref(p), x), Lit(5)));
        }

        var run = Method(probe, "Run", unit, Static, HandBuiltModel.Block([.. statements]));
        return Judge(new PurityAnalyzer(), run);
    }

    private IReadOnlyList<Diagnostic> PureMethodSetsAnExtensionPropertyWhoseSetterWritesAStatic()
    {
        var self = new LocalVariable("self", bag);
        var level = new Property("Level", integer, Public)
        {
            Getter = new PropertyAccessor { Body = Returning(Access(Ref(self), count)) },
            Setter = new PropertyAccessor { Body = HandBuiltModel.Block(Set(StaticRead(ticks), Lit(1))) },
        };
        var b = new LocalVariable("b", bag);
        var m = Method(probe, "M", integer, Pure, HandBuiltModel.Block(
            FreshBag(b),
            Set(new ExtensionPropertyAccessExpression(Node(), Ref(b), level, "BagExtensions", integer), Lit(1)),
            Return(Lit(1))));
        return Judge(new PurityAnalyzer(), m);
    }

    private IReadOnlyList<Diagnostic> PureMethodAllocatesThroughAnImplicitConstructor(ClassType allocated)
    {
        var b = new LocalVariable("b", allocated);
        var direct = Method(probe, "Direct", integer, PureStatic, HandBuiltModel.Block(Declare(b, New(allocated, null)), Return(Access(Ref(b), allocated.FieldsList[0]))));
        return Judge(new PurityAnalyzer(), direct);
    }

    private IReadOnlyList<Diagnostic> PureCallerOfAnUnmarkedMethodAllocatingThroughAnImplicitConstructor(ClassType allocated)
    {
        var b = new LocalVariable("b", allocated);
        var viaImplicit = Method(probe, "ViaImplicit", integer, Static, HandBuiltModel.Block(Declare(b, New(allocated, null)), Return(Access(Ref(b), allocated.FieldsList[0]))));
        var use = Method(probe, "Use", integer, PureStatic, Returning(Call(viaImplicit, null)));
        return Judge(new PurityAnalyzer(), use);
    }

    private IReadOnlyList<Diagnostic> ReadonlyBlockInsideAPureBody(IProgramAnalyzer analyzer)
    {
        var nested = HandBuiltModel.Block(Set(StaticRead(ticks), Lit(1)));
        nested.IsReadonly = true;
        var m = Method(probe, "M", integer, Pure, HandBuiltModel.Block(nested, Return(Lit(0))));
        return Judge(analyzer, m);
    }

    private IReadOnlyList<Diagnostic> StaticPropertyInitializerLambda(Func<FunctionType, LambdaExpression> lambda)
    {
        var holder = HandBuiltModel.Class("Holder");
        var reader = Function(pure: true, integer);
        var initializer = lambda(reader);
        var stored = new Property("R", reader, Static) { DeclaringType = holder, Getter = new PropertyAccessor { IsAuto = true }, Initializer = initializer };
        holder.PropertiesList.Add(stored);
        return model.Analyze(new PurityAnalyzer(), initializer, [holder], holder, stored);
    }

    private IReadOnlyList<Diagnostic> SettablePropertyInitializerLambda(Func<FunctionType, LambdaExpression> lambda)
    {
        var holder = HandBuiltModel.Class("Holder");
        var reader = Function(pure: true, integer);
        var initializer = lambda(reader);
        var stored = new Property("R", reader, Public)
        {
            DeclaringType = holder,
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
            Initializer = initializer,
        };
        holder.PropertiesList.Add(stored);
        _ = Constructor(holder, Public, null, Input("x", integer));
        return model.Analyze(new PurityAnalyzer(), initializer, [holder], holder, stored);
    }

    private IReadOnlyList<Diagnostic> StaticFieldInitializerLambda(Func<FunctionType, LambdaExpression> lambda)
    {
        var holder = HandBuiltModel.Class("Holder");
        var reader = Function(pure: true, integer);
        var initializer = lambda(reader);
        var stored = Field(holder, "R", reader, Static with { IsReadonly = true });
        stored.Initializer = initializer;
        return model.Analyze(new PurityAnalyzer(), initializer, [holder], holder, stored);
    }

    private IReadOnlyList<Diagnostic> InstanceFieldInitializerLambda(Func<FunctionType, LambdaExpression> lambda)
    {
        var holder = HandBuiltModel.Class("Holder");
        var reader = Function(pure: true, integer);
        var initializer = lambda(reader);
        var stored = Field(holder, "R", reader, Readonly);
        stored.Initializer = initializer;
        _ = Constructor(holder, Public, null, Input("x", integer));
        return model.Analyze(new PurityAnalyzer(), initializer, [holder], holder, stored);
    }

    private IReadOnlyList<Diagnostic> LambdasPassedThroughAParamsParameter(Func<FunctionType, LambdaExpression> first)
    {
        var reader = Function(pure: true, integer);
        var rs = Input("rs", Array(reader));
        rs.Parameter!.IsFlattened = true;
        var pick = Method(probe, "Pick", reader, Static, Returning(HandBuiltModel.Literal(reader)), rs);
        var r = new LocalVariable("r", reader);
        var run = Method(probe, "Run", unit, Static, HandBuiltModel.Block(Declare(r, Call(pick, null, first(reader), QuietLambda(reader)))));
        return Judge(new PurityAnalyzer(), run);
    }

    private IReadOnlyList<Diagnostic> PureMethodIteratesAnArrayParameter(bool flattened)
    {
        var reader = Function(pure: true, integer);
        var rs = Input("rs", Array(reader));
        rs.Parameter!.IsFlattened = flattened;
        var total = new LocalVariable("total", integer);
        var r = new LocalVariable("r", reader);
        var sum = Method(probe, "Sum", integer, PureStatic, HandBuiltModel.Block(
            Declare(total, Lit(0)),
            new ForEachStatement(r, Ref(rs), HandBuiltModel.Block(Set(Ref(total), Sum(Ref(total), Invoke(r))))),
            Return(Ref(total))), rs);
        return Judge(new PurityAnalyzer(), sum);
    }

    private IReadOnlyList<Diagnostic> ImplicitCloneOfAnExternalObject()
    {
        var (quiet, stored) = Quiet();
        var pool = HandBuiltModel.Class("Pool");
        var sharedQuiet = Field(pool, "Shared", quiet, Static with { IsReadonly = true });
        var c = new LocalVariable("c", quiet) { Ownership = OwnershipKind.Owned };
        var peek = Method(probe, "Peek", integer, PureStatic, HandBuiltModel.Block(Declare(c, new CloneExpression(Node(), StaticRead(sharedQuiet))), Return(Access(Ref(c), stored))));
        return Judge(new PurityAnalyzer(), peek);
    }

    private IReadOnlyList<Diagnostic> ImplicitCloneOfAFrozenInputThroughACloneReadingOnlyItsSource()
    {
        var (quiet, stored) = Quiet();
        var b = Input("b", quiet, OwnershipKind.Frozen);
        var c = new LocalVariable("c", quiet) { Ownership = OwnershipKind.Owned };
        var take = Method(probe, "Take", integer, PureStatic, HandBuiltModel.Block(
            Declare(c, new CloneExpression(Node(), Ref(b))),
            Set(Access(Ref(c), stored), Lit(5)),
            Return(Access(Ref(c), stored))), b);
        return Judge(new PurityAnalyzer(), take);
    }

    private LambdaExpression WritingLambda(FunctionType type)
    {
        return new LambdaExpression(Node(), [], null, HandBuiltModel.Block(Set(StaticRead(ticks), Lit(1)), Return(Lit(1))), false, type);
    }

    private LambdaExpression QuietLambda(FunctionType type)
    {
        return new LambdaExpression(Node(), [], Lit(1), null, false, type);
    }

    private ClassType Plain()
    {
        var plain = HandBuiltModel.Class("Plain");
        _ = Field(plain, "V", integer, Public);
        return plain;
    }

    private ClassType Seeded(string name, Expression initializer)
    {
        var seeded = HandBuiltModel.Class(name);
        var stored = Field(seeded, "N", integer, Public);
        stored.Initializer = initializer;
        _ = Field(seeded, "P", pt, Public);
        return seeded;
    }

    private (ClassType Type, Field N) Quiet()
    {
        var quiet = HandBuiltModel.Class("Quiet");
        var stored = Field(quiet, "N", integer, Public);
        _ = Constructor(quiet, Pure);
        var seed = Input("n", integer);
        var seeding = Constructor(quiet, Pure, HandBuiltModel.Block(Set(Bare(stored), Ref(seed))), seed);
        quiet.MethodsList.Add(new Method("Clone", quiet, [], Public)
        {
            DeclaringType = quiet,
            ReturnOwnership = OwnershipKind.Owned,
            Body = Returning(New(quiet, seeding, Bare(stored))),
        });
        return (quiet, stored);
    }

    private ConstructedType Array(IMistType element)
    {
        return model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [element, model.Interner.GetOrCreateUnknownValue(integer)]);
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

    private static Field Field(ConcreteType owner, string name, IMistType type, MemberModifiers modifiers, OwnershipKind ownership = OwnershipKind.Unrestricted)
    {
        var field = new Field(name, type, ownership, modifiers) { DeclaringType = owner };
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

    private static InvocationExpression Call(Method method, Expression? receiver, params Expression[] arguments)
    {
        Expression callee = receiver is null
            ? new MethodReferenceExpression(Node(), method)
            : new MemberAccessExpression(Node(), receiver, method.Name, method.ReturnType);
        return new InvocationExpression(Node(), callee, arguments, method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    private InvocationExpression Invoke(LocalVariable function)
    {
        return new InvocationExpression(Node(), Ref(function), [], integer, null, []);
    }
}
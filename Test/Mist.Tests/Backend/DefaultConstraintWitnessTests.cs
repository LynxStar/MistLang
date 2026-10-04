using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Backend;

public sealed class DefaultConstraintWitnessTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly GenericDefaultShapes shapes = new();

    [Fact]
    [Trait("Tier", "1")]
    public void WitnessParameters_KeepConstraintDeclarationOrder()
    {
        var owner = shapes.Type("Factory");
        _ = Parameter(owner, "A", defaultConstrained: true);
        _ = Parameter(owner, "Ignored");
        _ = Parameter(owner, "B", defaultConstrained: true);

        Assert.Equal(["System.Func<A> __default_A", "System.Func<B> __default_B"],
            DefaultConstraintWitness.Parameters(owner.TypeParametersList));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void StoredWitnesses_KeepConstraintsEvenWithoutGenericStorage()
    {
        var owner = shapes.Type("Factory");
        var first = Parameter(owner, "A", defaultConstrained: true);
        _ = Parameter(owner, "Ignored");
        var last = Parameter(owner, "B", defaultConstrained: true);

        Assert.Equal([first, last], DefaultWitnesses.Stored(owner));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ConstrainedMethod_DeclaresAFactoryBeforeDeclaredParameters()
    {
        var method = GenericMethod("Tag", [new Parameter("label", new HandBuiltModel().Type("String"))]);

        Assert.Equal("T Tag<T>(System.Func<T> __default_T, string label)", MethodEmitter.EmitSignature(method, new DiagnosticBag()));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void UnconstrainedMethod_CarriesNoWitness()
    {
        var method = GenericMethod("Plain", constrained: false);
        method.ParametersList.Add(new Parameter("value", method.ReturnType));

        Assert.Equal("T Plain<T>(T value)", MethodEmitter.EmitSignature(method, new DiagnosticBag()));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void DefaultOfAConstrainedParameter_CallsTheWitness()
    {
        Assert.Equal("__default_T()", Emit(new DefaultExpression(HandBuiltModel.Node(), GenericMethod("Create").ReturnType)));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("integer", "() => default(int)")]
    [InlineData("string", "() => \"\"")]
    [InlineData("enum", "() => Mode.__CreateDefault()")]
    [InlineData("class", "() => Widget.__CreateDefault()")]
    public void ConcreteCall_FillsTheWitnessWithTheSemanticDefault(string kind, string factory)
    {
        var argument = Argument(kind);

        Assert.Equal($"Create<{TypeNameEmitter.Emit(argument)}>({factory})", Emit(Call(GenericMethod("Create"), argument)));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ForwardingCall_PassesItsOwnWitnessThrough()
    {
        Assert.Equal("Create<T>(() => __default_T())", Emit(Call(GenericMethod("Create"), GenericMethod("Forward").ReturnType)));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void CallWithDeclaredArguments_SeparatesTheWitnessFromThem()
    {
        var method = GenericMethod("Tag", [new Parameter("label", new HandBuiltModel().Type("String"))]);

        Assert.Equal("Tag<int>(() => default(int), \"x\")", Emit(Call(method, shapes.Integer,
            HandBuiltModel.Literal(method.ParametersList[0].Type, "x"))));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ConstraintWithoutGenericStorage_PersistsItsWitnessForInstanceBodies()
    {
        var box = ConstrainedBox();
        _ = Get(box);

        var emitted = EmitType(box);

        Assert.Contains("System.Func<T> __default_T;", emitted, StringComparison.Ordinal);
        Assert.Contains("this.__default_T = __default_T;", emitted, StringComparison.Ordinal);
        Assert.Contains("public Box(System.Func<T> __default_T)", emitted, StringComparison.Ordinal);
        Assert.Contains("return __default_T();", emitted, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("integer", "() => default(int)")]
    [InlineData("string", "() => \"\"")]
    [InlineData("enum", "() => Mode.__CreateDefault()")]
    [InlineData("class", "() => Widget.__CreateDefault()")]
    public void ConstructionOfAConstrainedClass_ProvidesTheConcreteWitness(string kind, string factory)
    {
        var argument = Argument(kind);

        Assert.Equal($"new Probe.Box<{TypeNameEmitter.Emit(argument)}>({factory})", Emit(New(Construct(ConstrainedBox(), argument))));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ExplicitConstructor_ReceivesTheWitnessBeforeAFinalParamsArray()
    {
        var box = ConstrainedBox();
        var array = Construct(new HandBuiltModel().Type("Array<,>"), shapes.Integer);
        var constructor = new Constructor("Box", box, [new Parameter("values", array) { IsFlattened = true }], Public)
        {
            DeclaringType = box,
        };
        box.ConstructorsList.Add(constructor);

        var emitted = EmitConstructor(constructor, box);

        Assert.Contains("public Box(System.Func<T> __default_T, params int[] values)", emitted, StringComparison.Ordinal);
        Assert.Contains("this.__default_T = __default_T;", emitted, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ExplicitConstructorCall_PrependsTheWitnessAndRetainsDeclaredArguments()
    {
        var box = ConstrainedBox();
        var constructor = Constructor(box, shapes.Integer);

        Assert.Equal("new Probe.Box<Widget>(() => Widget.__CreateDefault(), 9)",
            Emit(New(Construct(box, Argument("class")), constructor, HandBuiltModel.Literal(shapes.Integer, 9L))));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void WitnessDependentInitializer_RunsAfterStorageAndOnlyAtTheRootConstructor()
    {
        var box = ConstrainedBox();
        var parameter = box.TypeParametersList[0];
        _ = Slot(box, "Value", parameter, new DefaultExpression(HandBuiltModel.Node(), parameter));
        var root = Constructor(box, shapes.Integer);
        var delegated = new Constructor("Box", box, [], Public)
        {
            DeclaringType = box,
            ChainKind = ConstructorChainKind.This,
            ChainedConstructor = root,
            ChainParameterTypes = [shapes.Integer],
        };
        delegated.ChainArgumentsList.Add(HandBuiltModel.Literal(shapes.Integer, 9L));
        box.ConstructorsList.Add(delegated);

        var emitted = EmitType(box);
        var rootBody = EmitConstructor(root, box);

        Assert.Contains("public T Value;", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("public T Value =", emitted, StringComparison.Ordinal);
        Assert.Equal(1, emitted.Split("this.Value = __default_T();", StringSplitOptions.None).Length - 1);
        Assert.True(rootBody.IndexOf("this.__default_T = __default_T;", StringComparison.Ordinal)
            < rootBody.IndexOf("this.Value = __default_T();", StringComparison.Ordinal));
        Assert.DoesNotContain("this.Value =", EmitConstructor(delegated, box), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false, "this(__default_T, 9)")]
    [InlineData(true, "base(__default_U, 9)")]
    public void ConstructorChain_ForwardsTheFactoryBeforeItsArguments(bool toBase, string chain)
    {
        var box = ConstrainedBox();
        var target = Constructor(box, shapes.Integer);
        var owner = toBase ? shapes.Type("Derived") : box;

        if(toBase)
        {
            owner.BaseType = Construct(box, Parameter(owner, "U", defaultConstrained: true));
        }

        var constructor = new Constructor(owner.Name, owner, [], Public)
        {
            DeclaringType = owner,
            ChainKind = toBase ? ConstructorChainKind.Base : ConstructorChainKind.This,
            ChainedConstructor = target,
            ChainParameterTypes = [shapes.Integer],
        };
        constructor.ChainArgumentsList.Add(HandBuiltModel.Literal(shapes.Integer, 9L));

        Assert.Contains($" : {chain}", EmitConstructor(constructor, owner), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false, 0, "default(System.ValueTuple)")]
    [InlineData(false, 1, "System.ValueTuple.Create(1)")]
    [InlineData(false, 2, "(1, 2)")]
    [InlineData(true, 0, "default(System.ValueTuple)")]
    [InlineData(true, 1, "System.ValueTuple.Create(1)")]
    [InlineData(true, 2, "(1, 2)")]
    public void ConstructorChain_PacksFlatArgumentsAfterTheDefaultWitness(bool toBase, int width, string packed)
    {
        var box = ConstrainedBox();
        var tuple = new TupleType(Enumerable.Repeat<IMistType>(shapes.Integer, width).ToArray());
        var target = Constructor(box, tuple);
        target.ParametersList[0].IsFlattened = true;
        var owner = toBase ? shapes.Type("Derived") : box;

        if(toBase)
        {
            owner.BaseType = Construct(box, Parameter(owner, "U", defaultConstrained: true));
        }

        var constructor = new Constructor(owner.Name, owner, [], Public)
        {
            DeclaringType = owner,
            ChainKind = toBase ? ConstructorChainKind.Base : ConstructorChainKind.This,
            ChainedConstructor = target,
            ChainParameterTypes = [tuple],
        };
        constructor.ChainArgumentsList.AddRange(Enumerable.Range(1, width)
            .Select(value => HandBuiltModel.Literal(shapes.Integer, (long)value)));
        var chain = toBase ? "base" : "this";
        var witness = toBase ? "__default_U" : "__default_T";

        Assert.Contains($" : {chain}({witness}, {packed})", EmitConstructor(constructor, owner), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ConstructorCall_SeparatesTheDefaultWitnessFromAnEmptyValuePack()
    {
        var box = ConstrainedBox();
        var constructor = Constructor(box, new TupleType([]));
        constructor.ParametersList[0].IsFlattened = true;

        Assert.Equal("new Probe.Box<int>(() => default(int), default(System.ValueTuple))",
            Emit(New(Construct(box, shapes.Integer), constructor)));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void MethodCall_SeparatesTheDefaultWitnessFromAnEmptyValuePack()
    {
        var method = GenericMethod("Create", [new Parameter("args", new TupleType([])) { IsFlattened = true }]);

        Assert.Equal("Create<int>(() => default(int), default(System.ValueTuple))", Emit(Call(method, shapes.Integer)));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void NestedType_PersistsTheEnclosingConstraintWitness()
    {
        var outer = shapes.Type("Outer");
        var parameter = Parameter(outer, defaultConstrained: true);
        var nested = new ClassType("Nested", "Probe", outer.Scope, outer, null) { Modifiers = Public };
        _ = Get(nested, parameter);

        var emitted = EmitType(nested);

        Assert.Contains("public Nested(System.Func<T> __default_T)", emitted, StringComparison.Ordinal);
        Assert.Contains("this.__default_T = __default_T;", emitted, StringComparison.Ordinal);
        Assert.Contains("return __default_T();", emitted, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void StaticMethodOnAConstrainedClass_ReceivesTheClassWitnessAtTheCall()
    {
        var box = ConstrainedBox();
        var method = Get(box, isStatic: true);
        var closed = Construct(box, Argument("class"));
        var reference = new MemberAccessExpression(HandBuiltModel.Node(),
            new TypeReferenceExpression(HandBuiltModel.Node(), closed), "Get", Argument("class"));
        var call = new InvocationExpression(HandBuiltModel.Node(), reference, [], Argument("class"), method, []);

        Assert.Contains("public static T Get(System.Func<T> __default_T)", EmitType(box), StringComparison.Ordinal);
        Assert.Equal("Probe.Box<Widget>.Get(() => Widget.__CreateDefault())", Emit(call));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void InheritedStaticMethod_ClosesTheConstraintFromTheBaseType()
    {
        var box = ConstrainedBox();
        var method = Get(box, isStatic: true);
        var derived = shapes.Type("Derived", baseType: Construct(box, shapes.String));
        var reference = new MemberAccessExpression(HandBuiltModel.Node(),
            new TypeReferenceExpression(HandBuiltModel.Node(), derived), "Get", shapes.String);
        var call = new InvocationExpression(HandBuiltModel.Node(), reference, [], shapes.String, method, []);

        Assert.Equal("Derived.Get(() => \"\")", Emit(call));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ConstrainedMethodGroup_BindsTheWitnessAndCapturesItsReceiverOnce()
    {
        var method = GenericMethod("Create", isStatic: false);
        var acquire = new Method("Acquire", method.DeclaringType!, [], Public with { IsStatic = true });
        var receiver = new InvocationExpression(HandBuiltModel.Node(),
            new MethodReferenceExpression(HandBuiltModel.Node(), acquire), [], method.DeclaringType!, acquire, []);
        var reference = new FunctionReferenceExpression(HandBuiltModel.Node(), receiver, method,
            new FunctionType([], shapes.Integer))
        { TypeArguments = [shapes.Integer] };

        var emitted = Emit(reference);

        Assert.Contains("__receiver => () => __receiver.Create<int>(() => default(int))", emitted, StringComparison.Ordinal);
        Assert.Equal(1, emitted.Split("Acquire()", StringSplitOptions.None).Length - 1);
        Assert.EndsWith(")(Acquire())", emitted, StringComparison.Ordinal);
    }

    private ConcreteType ConstrainedBox()
    {
        var box = shapes.Type("Box");
        _ = Parameter(box, defaultConstrained: true);
        return box;
    }

    private static Method Get(ConcreteType owner, TypeParameter? parameter = null, bool isStatic = false)
    {
        parameter ??= owner.TypeParametersList[0];
        var method = new Method("Get", parameter, [], Public with { IsStatic = isStatic })
        {
            DeclaringType = owner,
            Scope = new Scope(owner.Scope),
            Body = HandBuiltModel.Block(new ReturnStatement(new DefaultExpression(HandBuiltModel.Node(), parameter), parameter)),
        };
        owner.MethodsList.Add(method);
        return method;
    }

    private IMistType Argument(string kind)
    {
        if(kind == "integer")
        {
            return shapes.Integer;
        }

        if(kind == "string")
        {
            return shapes.String;
        }

        if(kind == "enum")
        {
            var mode = HandBuiltModel.Enum("Mode", shapes.Integer);
            mode.MembersList.Add(new EnumMember("Ready", true, HandBuiltModel.Literal(shapes.Integer, 7L)));
            return mode;
        }

        var widget = shapes.Type("Widget");
        _ = Slot(widget, "Value", shapes.Integer);
        Block(widget, new DefaultValueEntry("Value", HandBuiltModel.Literal(shapes.Integer, 7L)));
        return widget;
    }

    private Method GenericMethod(string name, List<Parameter>? parameters = null, bool constrained = true, bool isStatic = true)
    {
        var owner = shapes.Type($"Factory{name}");
        var scope = new Scope(owner.Scope);
        var parameter = new TypeParameter("T", 0, scope, owner);

        if(constrained)
        {
            parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Default));
        }

        var method = new Method(name, parameter, parameters ?? [], Public with { IsStatic = isStatic })
        {
            DeclaringType = owner,
            Scope = scope,
        };
        method.TypeParametersList.Add(parameter);
        return method;
    }

    private static InvocationExpression Call(Method method, IMistType argument, params Expression[] declaredArguments)
    {
        return new InvocationExpression(HandBuiltModel.Node(),
            new MethodReferenceExpression(HandBuiltModel.Node(), method, [argument]), declaredArguments, argument, method,
            method.ParametersList.Select(parameter => parameter.Type).ToArray());
    }

    private static NewExpression New(IMistType type, Constructor? constructor = null, params Expression[] arguments)
    {
        return new NewExpression(HandBuiltModel.Node(), type, arguments, constructor, [],
            constructor?.ParametersList.Select(parameter => parameter.Type).ToArray());
    }

    private static string Emit(Expression expression)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.EmitExpression(expression, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString();
    }

    private static string EmitConstructor(Constructor constructor, ConcreteType owner)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        ConstructorEmitter.Emit(constructor, owner, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString();
    }

    private static string EmitType(ConcreteType type)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var model = new HandBuiltModel().Model;
        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(model, diagnostics), MonomorphSet.Discover(model).Methods,
            writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString();
    }
}
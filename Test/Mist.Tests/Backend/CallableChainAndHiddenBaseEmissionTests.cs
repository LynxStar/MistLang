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

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class CallableChainAndHiddenBaseEmissionTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData(ConstructorChainKind.Base)]
    [InlineData(ConstructorChainKind.This)]
    public void ClassConstructorChainPinsSelectedCallable(ConstructorChainKind kind)
    {
        var function = Function();
        var baseType = HandBuiltModel.Class("Base");
        var derived = HandBuiltModel.Class("Derived", baseType);
        var target = kind == ConstructorChainKind.Base ? baseType : derived;
        var selected = AddConstructor(target, function);
        _ = AddConstructor(target, Nominal());
        var source = new Constructor("Derived", derived, [], new MemberModifiers())
        {
            DeclaringType = derived,
            ChainKind = kind,
            ChainedConstructor = selected,
            ChainParameterTypes = [function],
            Body = HandBuiltModel.Block(),
        };
        source.ChainArgumentsList.Add(Lambda(function));

        var writer = new CSharpWriter();
        ConstructorEmitter.Emit(source, derived, writer, new DiagnosticBag());

        Assert.Contains($": {kind.ToString().ToLowerInvariant()}(((System.Func<int, bool>)", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LoweredStructChainPinsSelectedCallable()
    {
        var function = Function();
        var owner = HandBuiltModel.Struct("Value");
        var selected = new Constructor("Value", owner, [new Parameter("f", function)], new MemberModifiers())
        { DeclaringType = owner };
        var sibling = new Constructor("Value", owner, [new Parameter("f", Nominal())], new MemberModifiers())
        { DeclaringType = owner };
        owner.ConstructorsList.Add(selected);
        owner.ConstructorsList.Add(sibling);
        var source = new Constructor("Value", owner, [], new MemberModifiers())
        {
            DeclaringType = owner,
            ChainKind = ConstructorChainKind.This,
            ChainedConstructor = selected,
            ChainParameterTypes = [function],
        };
        source.ChainArgumentsList.Add(Lambda(function));

        var writer = new CSharpWriter();
        StructInitEmitter.EmitChainCall(source, owner, new Dictionary<StructType, string> { [owner] = "__Value_ctor" },
            writer, new DiagnosticBag());

        Assert.Contains("__Value_ctor(((System.Func<int, bool>)", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClassChainKeepsReferenceArgumentKeyword()
    {
        var integer = model.Type("Int32");
        var owner = HandBuiltModel.Class("Value");
        var modifier = new ParameterModifier(false, true, OwnershipKind.Borrowed, ByRefDirection.Ref);
        var target = new Constructor("Value", owner, [new Parameter("value", integer, modifier)], new MemberModifiers())
        { DeclaringType = owner };
        var sourceParameter = new Parameter("value", integer);
        var source = new Constructor("Value", owner, [sourceParameter], new MemberModifiers())
        {
            DeclaringType = owner,
            ChainKind = ConstructorChainKind.This,
            ChainedConstructor = target,
            ChainParameterTypes = [integer],
            Body = HandBuiltModel.Block(),
        };
        source.ChainArgumentsList.Add(new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("value", integer)));

        var writer = new CSharpWriter();
        ConstructorEmitter.Emit(source, owner, writer, new DiagnosticBag());

        Assert.Contains(": this(ref value)", writer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConstructorChainKind.Base, 2, ": base((4, true))")]
    [InlineData(ConstructorChainKind.This, 1, ": this(System.ValueTuple.Create(4))")]
    [InlineData(ConstructorChainKind.Base, 0, ": base(default(System.ValueTuple))")]
    public void AClassChainIntoAValuePack_PacksTheFlatArgumentsAsANewWould(ConstructorChainKind kind, int width, string expected)
    {
        var elements = new IMistType[] { model.Type("Int32"), model.Type("Boolean") }.Take(width).ToList();
        var pack = new TupleType(elements);
        var baseType = HandBuiltModel.Class("Base");
        var derived = HandBuiltModel.Class("Derived", baseType);
        var owner = kind == ConstructorChainKind.Base ? baseType : derived;
        var target = new Constructor(owner.Name, owner, [new Parameter("values", pack) { IsFlattened = true }], new MemberModifiers())
        { DeclaringType = owner };
        var source = new Constructor("Derived", derived, [], new MemberModifiers())
        {
            DeclaringType = derived,
            ChainKind = kind,
            ChainedConstructor = target,
            ChainParameterTypes = [pack],
            Body = HandBuiltModel.Block(),
        };
        source.ChainArgumentsList.AddRange(elements.Select((element, i) =>
            HandBuiltModel.Literal(element, i == 0 ? 4L : true)));

        var writer = new CSharpWriter();
        ConstructorEmitter.Emit(source, derived, writer, new DiagnosticBag());

        Assert.Contains(expected, writer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitHiddenBaseCallCastsReceiverAndPinsCallable(bool conditional)
    {
        var function = Function();
        var baseType = HandBuiltModel.Class("Base");
        var selected = AddMethod(baseType, function);
        _ = AddMethod(baseType, Nominal());
        var derived = HandBuiltModel.Class("Derived", baseType);
        _ = AddMethod(derived, Nominal());
        var receiver = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("value", derived));
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "N", model.Type("Int32"), isConditional: conditional);
        var call = new InvocationExpression(HandBuiltModel.Node(), access, [Lambda(function)], model.Type("Int32"), selected, [function]);

        Assert.Contains(conditional
            ? "((Base)value)?.N(((System.Func<int, bool>)"
            : "((Base)value).N(((System.Func<int, bool>)",
            BodyEmitter.Render(call, new DiagnosticBag()), StringComparison.Ordinal);
    }

    [Fact]
    public void ImplicitHiddenBaseCallUsesEmittingOwner()
    {
        var function = Function();
        var baseType = HandBuiltModel.Class("Base");
        var selected = AddMethod(baseType, function);
        var derived = HandBuiltModel.Class("Derived", baseType);
        _ = AddMethod(derived, Nominal());
        var call = new InvocationExpression(HandBuiltModel.Node(),
            new MethodReferenceExpression(HandBuiltModel.Node(), selected), [Lambda(function)], model.Type("Int32"), selected, [function]);
        var caller = new Method("Go", model.Type("Int32"), [], new MemberModifiers())
        {
            DeclaringType = derived,
            Body = HandBuiltModel.Block(new ReturnStatement(call, model.Type("Int32"))),
        };

        var writer = new CSharpWriter();
        MethodEmitter.Emit(caller, emitReadonly: false, writer, new DiagnosticBag(), derived);

        Assert.Contains("((Base)this).N((int n) => true)", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GenericBaseReceiverKeepsItsClosedType()
    {
        var baseType = HandBuiltModel.Class("Base<>");
        var parameter = new TypeParameter("T", 0, baseType.Scope, baseType);
        baseType.TypeParametersList.Add(parameter);
        var selected = AddMethod(baseType, Function());
        var constructed = model.Interner.GetOrCreateConstructedType(baseType, [model.Type("Int32")]);
        var derived = HandBuiltModel.Class("Derived", constructed);
        _ = AddMethod(derived, Nominal());

        Assert.Equal("Probe.Base<int>", TypeNameEmitter.Emit(HiddenBaseCall.ReceiverType(derived, selected)!));
    }

    [Fact]
    public void HiddenStaticBaseCallNamesTheSelectedType()
    {
        var function = Function();
        var baseType = HandBuiltModel.Class("Base");
        var selected = new Method("N", model.Type("Int32"), [new Parameter("f", function)],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = baseType };
        baseType.MethodsList.Add(selected);
        var derived = HandBuiltModel.Class("Derived", baseType);
        derived.MethodsList.Add(new Method("N", model.Type("Int32"), [new Parameter("f", Nominal())],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = derived });
        var access = new MemberAccessExpression(HandBuiltModel.Node(),
            new TypeReferenceExpression(HandBuiltModel.Node(), derived), "N", model.Type("Int32"));
        var call = new InvocationExpression(HandBuiltModel.Node(), access,
            [Lambda(function)], model.Type("Int32"), selected, [function]);

        Assert.Equal("Base.N((int n) => true)", BodyEmitter.Render(call, new DiagnosticBag()));
    }

    private FunctionType Function()
    {
        return new([model.Type("Int32")], model.Type("Boolean"));
    }

    private LambdaExpression Lambda(FunctionType function)
    {
        return new LambdaExpression(HandBuiltModel.Node(), [new LocalVariable("n", model.Type("Int32"))],
            HandBuiltModel.Literal(model.Type("Boolean"), true), null, false, function);
    }

    private ClassType Nominal()
    {
        var type = HandBuiltModel.Class("Predicate");
        type.BaseType = new ClassType("MulticastDelegate", "System", new Scope(null), null, null);
        type.MethodsList.Add(new Method("Invoke", model.Type("Boolean"), [new Parameter("n", model.Type("Int32"))], new MemberModifiers()));
        return type;
    }

    private static Constructor AddConstructor(ClassType owner, IMistType parameter)
    {
        var constructor = new Constructor(owner.Name, owner, [new Parameter("f", parameter)], new MemberModifiers())
        { DeclaringType = owner };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private Method AddMethod(ClassType owner, IMistType parameter)
    {
        var method = new Method("N", model.Type("Int32"), [new Parameter("f", parameter)], new MemberModifiers())
        { DeclaringType = owner };
        owner.MethodsList.Add(method);
        return method;
    }
}
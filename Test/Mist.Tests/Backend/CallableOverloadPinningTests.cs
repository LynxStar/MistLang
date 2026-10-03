using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class CallableOverloadPinningDecisionTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedStructuralTargetPinsEitherLambdaBody(bool block)
    {
        var function = Function();
        var lambda = Lambda(function, block);

        Assert.Equal("System.Func<int, bool>", OverloadPinning.CastFor(lambda, function, overloaded: true));
        Assert.Null(OverloadPinning.CastFor(lambda, function, overloaded: false));
    }

    [Fact]
    public void SettledGroupAndPureFunctionUseTheSelectedTarget()
    {
        var function = Function(pure: true);
        var group = Group(function, isStatic: true);

        Assert.Equal("System.Func<int, bool>", OverloadPinning.CastFor(group, function, overloaded: true));
        Assert.Equal("System.Func<int, bool>", OverloadPinning.CastFor(Lambda(function, false), function, overloaded: true));
    }

    [Fact]
    public void ExistingDelegateConstructionAndUnionCastAreNotDuplicated()
    {
        var function = Function();
        var nominal = Nominal();
        var lambda = Lambda(function, false);
        var unionLambda = new LambdaExpression(HandBuiltModel.Node(), lambda.Parameters, lambda.Result, null, false, function)
        {
            UnionCarrierDelegateType = function,
        };

        Assert.Null(OverloadPinning.CastFor(lambda, nominal, overloaded: true));
        Assert.Null(OverloadPinning.CastFor(unionLambda, function, overloaded: true));
        Assert.Null(OverloadPinning.CastFor(lambda, model.Type("String"), overloaded: true));
    }

    [Fact]
    public void AlreadyTypedDelegateValueIsNotWrapped()
    {
        var function = Function();
        var value = new LocalVariable("f", function);

        Assert.Null(OverloadPinning.CastFor(new LocalReferenceExpression(HandBuiltModel.Node(), value), function, overloaded: true));
    }

    private FunctionType Function(bool pure = false)
    {
        return new FunctionType([model.Type("Int32")], model.Type("Boolean"), pure);
    }

    private LambdaExpression Lambda(FunctionType function, bool block)
    {
        var parameter = new LocalVariable("n", model.Type("Int32"));
        var result = HandBuiltModel.Literal(model.Type("Boolean"), true);
        return new LambdaExpression(HandBuiltModel.Node(), [parameter], block ? null : result,
            block ? HandBuiltModel.Block(new ReturnStatement(result, model.Type("Boolean"))) : null, false, function);
    }

    private static FunctionReferenceExpression Group(FunctionType function, bool isStatic)
    {
        var owner = HandBuiltModel.Class("Source");
        var method = new Method("Check", function.ReturnType, [new Parameter("n", function.ParameterTypes[0])],
            new MemberModifiers { IsStatic = isStatic })
        { DeclaringType = owner };
        return new FunctionReferenceExpression(HandBuiltModel.Node(), null, method, function);
    }

    private ClassType Nominal()
    {
        var type = HandBuiltModel.Class("Predicate");
        type.BaseType = new ClassType("MulticastDelegate", "System", new Scope(null), null, null);
        type.MethodsList.Add(new Method("Invoke", model.Type("Boolean"), [new Parameter("n", model.Type("Int32"))], new MemberModifiers()));
        return type;
    }
}

[Trait("Tier", "2")]
public sealed class CallableOverloadArgumentEmissionTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SelectedMethodGetsClosedStructuralLambdaTarget(bool block, bool pure)
    {
        var function = Function(pure);
        var owner = HandBuiltModel.Class("Calls");
        var selected = Add(owner, "N", function);
        _ = Add(owner, "N", Nominal());
        var call = Call(selected, Lambda(function, block), function);

        var emitted = Emit(call).ReplaceLineEndings("\n");

        Assert.Contains("N(((System.Func<int, bool>)", emitted, StringComparison.Ordinal);
        Assert.Contains(block ? "=> \n{" : "=> true", emitted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedMethodPinsStaticAndInstanceGroups(bool isStatic)
    {
        var function = Function();
        var owner = HandBuiltModel.Class("Calls");
        var selected = Add(owner, "N", function);
        _ = Add(owner, "N", Nominal());
        var source = HandBuiltModel.Class("Source");
        var check = new Method("Check", model.Type("Boolean"), [new Parameter("n", model.Type("Int32"))],
            new MemberModifiers { IsStatic = isStatic })
        { DeclaringType = source };
        var receiver = isStatic ? null : new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("source", source));
        var group = new FunctionReferenceExpression(HandBuiltModel.Node(), receiver, check, function);

        var emitted = Emit(Call(selected, group, function));

        Assert.Contains("N(((System.Func<int, bool>)", emitted, StringComparison.Ordinal);
        Assert.Contains(isStatic ? "Check" : "source.Check", emitted, StringComparison.Ordinal);
        Assert.Equal(isStatic ? 0 : 1, emitted.Split("source.Check", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ClosedMethodParameterSuppliesTargetInsteadOfItsOpenDeclaration()
    {
        var function = Function();
        var owner = HandBuiltModel.Class("Calls");
        var parameter = new TypeParameter("T", 0, owner.Scope, owner);
        var selected = Add(owner, "N", parameter);
        _ = Add(owner, "N", Nominal());

        Assert.Contains("N(((System.Func<int, bool>)", Emit(Call(selected, Lambda(function, false), function)), StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructedReceiverClosesTheSelectedTarget()
    {
        var function = Function();
        var owner = HandBuiltModel.Class("Container<>");
        var parameter = new TypeParameter("T", 0, owner.Scope, owner);
        owner.TypeParametersList.Add(parameter);
        var selected = Add(owner, "N", parameter);
        _ = Add(owner, "N", Nominal());
        var constructed = model.Interner.GetOrCreateConstructedType(owner, [function]);
        var receiver = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("box", constructed));
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "N", selected.ReturnType);
        var call = new InvocationExpression(HandBuiltModel.Node(), access, [Lambda(function, false)], selected.ReturnType, selected, [function]);

        Assert.Contains("box.N(((System.Func<int, bool>)", Emit(call), StringComparison.Ordinal);
    }

    [Fact]
    public void InheritedAndConditionalReceiverFindsCompetingOverload()
    {
        var function = Function();
        var owner = HandBuiltModel.Class("Base");
        var selected = Add(owner, "N", function);
        _ = Add(owner, "N", Nominal());
        var derived = HandBuiltModel.Class("Derived", owner);
        var receiver = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("value", derived));
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "N", selected.ReturnType, isConditional: true);
        var call = new InvocationExpression(HandBuiltModel.Node(), access, [Lambda(function, false)], selected.ReturnType, selected, [function]);

        Assert.Contains("value?.N(((System.Func<int, bool>)", Emit(call), StringComparison.Ordinal);
    }

    [Fact]
    public void ASettledGenericGroupKeepsItsExplicitTypeArguments()
    {
        var function = Function();
        var owner = HandBuiltModel.Class("Calls");
        var selected = Add(owner, "N", function);
        _ = Add(owner, "N", Nominal());
        var source = HandBuiltModel.Class("Source");
        var typeParameter = new TypeParameter("T", 0, source.Scope, source);
        var check = new Method("Check", model.Type("Boolean"), [new Parameter("n", typeParameter)],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = source };
        check.TypeParametersList.Add(typeParameter);
        var group = new FunctionReferenceExpression(HandBuiltModel.Node(), null, check, function)
        {
            TypeArguments = [model.Type("Int32")],
        };

        Assert.Contains("Check<int>", Emit(Call(selected, group, function)), StringComparison.Ordinal);
    }

    [Fact]
    public void FlattenedTupleArgumentsRetainTheCallableElementTarget()
    {
        var function = Function();
        var owner = HandBuiltModel.Class("Calls");
        var tuple = new TupleType([function, model.Type("Int32")]);
        var selected = Add(owner, "N", tuple);
        selected.ParametersList[0].IsFlattened = true;
        var call = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), selected),
            [Lambda(function, false), HandBuiltModel.Literal(model.Type("Int32"), 4L)], selected.ReturnType, selected, [tuple]);

        Assert.Contains("N((((System.Func<int, bool>)", Emit(call), StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedConstructorAndNominalCrossingKeepTheirTargets()
    {
        var function = Function();
        var nominal = Nominal();
        var owner = HandBuiltModel.Class("Box");
        var selected = new Constructor("Box", owner, [new Parameter("f", nominal)], new MemberModifiers()) { DeclaringType = owner };
        var sibling = new Constructor("Box", owner, [new Parameter("f", function)], new MemberModifiers()) { DeclaringType = owner };
        owner.ConstructorsList.Add(selected);
        owner.ConstructorsList.Add(sibling);
        var creation = new NewExpression(HandBuiltModel.Node(), owner, [Lambda(function, false)], selected, [], [nominal]);

        Assert.Contains("new Box(new Predicate(", Emit(creation), StringComparison.Ordinal);
    }

    [Fact]
    public void TypedFunctionCrossingToNominalDelegateUsesItsInvokeGroup()
    {
        var function = Function();
        var nominal = Nominal();
        var owner = HandBuiltModel.Class("Calls");
        var selected = Add(owner, "N", nominal);
        _ = Add(owner, "N", function);
        var value = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("f", function));

        Assert.Contains("N(new Predicate((f).Invoke))", Emit(Call(selected, value, nominal)), StringComparison.Ordinal);
    }

    [Fact]
    public void ClrProjectedCallableArgumentKeepsClrContextualConversion()
    {
        var function = Function();
        var owner = HandBuiltModel.Class("Calls");
        var parameter = new TypeParameter("T", 0, owner.Scope, owner);
        var declared = new FunctionType([parameter], model.Type("Boolean"));
        var selected = new Method("N", owner, [new Parameter("f", declared)],
            new MemberModifiers { IsStatic = true, IsClrProjected = true })
        { DeclaringType = owner };
        owner.MethodsList.Add(selected);
        _ = Add(owner, "N", Nominal());
        var value = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("predicate", Nominal()));

        Assert.Equal("N((int n) => true)", Emit(Call(selected, Lambda(function, false), function)));
        Assert.Equal("N(predicate)", Emit(Call(selected, value, function)));
    }

    [Fact]
    public void ANominalValueCallKeepsItsCallableTargetWithoutBecomingAnOverloadedMethodCall()
    {
        var function = Function();
        var nominal = Nominal();
        var invoke = new Method("Invoke", model.Type("Boolean"), [new Parameter("f", nominal)],
            new MemberModifiers { IsClrProjected = true });
        var callback = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("callback", nominal));
        var value = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("f", function));
        var call = new InvocationExpression(HandBuiltModel.Node(), callback, [value], model.Type("Boolean"), null, [nominal])
        {
            InvokeMethod = invoke,
        };

        Assert.Null(call.Method);
        Assert.Equal("callback(new Predicate((f).Invoke))", Emit(call));
    }

    [Fact]
    public void OrdinaryCallAndTypedValueStayBare()
    {
        var function = Function();
        var owner = HandBuiltModel.Class("Calls");
        var selected = Add(owner, "N", function);
        var value = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("f", function));

        Assert.Contains("N((int n) => true)", Emit(Call(selected, Lambda(function, false), function)), StringComparison.Ordinal);
        _ = Add(owner, "N", Nominal());
        Assert.Contains("N(f)", Emit(Call(selected, value, function)), StringComparison.Ordinal);
    }

    private FunctionType Function(bool pure = false)
    {
        return new FunctionType([model.Type("Int32")], model.Type("Boolean"), pure);
    }

    private LambdaExpression Lambda(FunctionType function, bool block)
    {
        var parameter = new LocalVariable("n", model.Type("Int32"));
        var result = HandBuiltModel.Literal(model.Type("Boolean"), true);
        return new LambdaExpression(HandBuiltModel.Node(), [parameter], block ? null : result,
            block ? HandBuiltModel.Block(new ReturnStatement(result, model.Type("Boolean"))) : null, false, function);
    }

    private ClassType Nominal()
    {
        var type = HandBuiltModel.Class("Predicate");
        type.BaseType = new ClassType("MulticastDelegate", "System", new Scope(null), null, null);
        type.MethodsList.Add(new Method("Invoke", model.Type("Boolean"), [new Parameter("n", model.Type("Int32"))], new MemberModifiers()));
        return type;
    }

    private static Method Add(ClassType owner, string name, IMistType parameter)
    {
        var method = new Method(name, owner, [new Parameter("f", parameter)], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
        owner.MethodsList.Add(method);
        return method;
    }

    private static InvocationExpression Call(Method selected, Expression argument, IMistType closedParameter)
    {
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), selected),
            [argument], selected.ReturnType, selected, [closedParameter]);
    }

    private static string Emit(Expression expression)
    {
        return BodyEmitter.Render(expression, new DiagnosticBag());
    }
}
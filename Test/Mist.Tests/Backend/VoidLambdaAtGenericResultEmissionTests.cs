using Mist.Backends.CSharpTranspiler.Emitter;
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

[Trait("Tier", "2")]
public sealed class VoidLambdaAtGenericResultEmissionTests
{
    private readonly HandBuiltModel model = new();

    private readonly ClassType owner = HandBuiltModel.Class("Program");

    private IMistType Void => model.Type("Void");

    [Fact]
    public void AVoidCallBodyAtAResultInferredVoid_YieldsTheUnitValue()
    {
        var emitted = Emit(Call(Generic(), Lambda()));

        Assert.Contains("Hello();", emitted, StringComparison.Ordinal);
        Assert.Contains("return Standard.Void.Instance;", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("=> Hello()", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockBodyAtAResultInferredVoid_ReturnsTheUnitValueOnEveryPath()
    {
        var early = new IfStatement(HandBuiltModel.Literal(model.Type("Boolean"), true), HandBuiltModel.Block(new ReturnStatement(null, Void)), null);
        var block = HandBuiltModel.Block(early, new ExpressionStatement(Hello()));

        var emitted = Emit(Call(Generic(), new LambdaExpression(HandBuiltModel.Node(), [], null, block, false, new FunctionType([], Void))));

        Assert.Equal(2, emitted.Split("return Standard.Void.Instance;").Length - 1);
        Assert.DoesNotContain("return;", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void ANestedVoidLambda_KeepsItsBareReturn()
    {
        var inner = new LambdaExpression(HandBuiltModel.Node(), [], null, HandBuiltModel.Block(new ReturnStatement(null, Void)), false, new FunctionType([], Void));
        var local = new LocalVariable("inner", inner.Type);
        var block = HandBuiltModel.Block(new LocalDeclarationStatement(local, inner));

        var emitted = Emit(Call(Generic(), new LambdaExpression(HandBuiltModel.Node(), [], null, block, false, new FunctionType([], Void))));

        Assert.Contains("return;", emitted, StringComparison.Ordinal);
        Assert.Equal(1, emitted.Split("return Standard.Void.Instance;").Length - 1);
    }

    [Fact]
    public void AVoidCallBodyAtAVoidResult_StaysAnExpressionBody()
    {
        var run = new Method("Run", Void, [new Parameter("f", new FunctionType([], Void))], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };

        Assert.Equal("Run(() => Hello())", Emit(Call(run, Lambda())));
    }

    private Method Generic()
    {
        var result = new TypeParameter("R", 0, new Scope(owner.Scope), declaringType: null);
        var call = new Method("Call", result, [new Parameter("f", new FunctionType([], result))], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
        call.TypeParametersList.Add(result);
        return call;
    }

    private LambdaExpression Lambda()
    {
        return new LambdaExpression(HandBuiltModel.Node(), [], Hello(), null, false, new FunctionType([], Void));
    }

    private InvocationExpression Hello()
    {
        var hello = new Method("Hello", Void, [], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), hello), [], Void, hello, []);
    }

    private InvocationExpression Call(Method callee, Expression argument)
    {
        var typeArguments = callee.TypeParametersList.Count == 0 ? null : new[] { Void };
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), callee, typeArguments), [argument],
            Void, callee, [new FunctionType([], Void)]);
    }

    private static string Emit(Expression expression)
    {
        return BodyEmitter.Render(expression, new DiagnosticBag()).ReplaceLineEndings("\n");
    }
}
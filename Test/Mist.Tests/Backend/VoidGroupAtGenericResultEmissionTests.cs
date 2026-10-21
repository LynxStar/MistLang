using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class VoidGroupAtGenericResultEmissionTests
{
    private readonly HandBuiltModel model = new();

    private readonly ClassType owner = HandBuiltModel.Class("Program");

    private IMistType Int => model.Type("Int32");

    private IMistType Void => model.Type("Void");

    [Fact]
    public void AVoidMethodGroupAtAResultInferredVoid_IsWrappedToYieldTheUnitValue()
    {
        var emitted = Emit(Call(Generic([]), Group([]), new FunctionType([], Void)));

        Assert.Contains("System.Func<Void>", emitted, StringComparison.Ordinal);
        Assert.Contains("Hello();", emitted, StringComparison.Ordinal);
        Assert.Contains("return Standard.Void.Instance;", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AGroupWithParameters_ForwardsThemThroughTheWrapper()
    {
        var emitted = Emit(Call(Generic([Int]), Group([Int]), new FunctionType([Int], Void)));

        Assert.Contains("System.Func<int, Void>", emitted, StringComparison.Ordinal);
        Assert.Contains("Hello(__arg0);", emitted, StringComparison.Ordinal);
        Assert.Contains("return Standard.Void.Instance;", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AVoidMethodGroupAtAVoidResult_StaysBare()
    {
        var function = new FunctionType([], Void);
        var run = new Method("Run", Void, [new Parameter("f", function)], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };

        Assert.Equal("Run(Hello)", Emit(Call(run, Group([]), function)));
    }

    private Method Generic(IReadOnlyList<IMistType> inputs)
    {
        var result = new TypeParameter("R", 0, new Scope(owner.Scope), declaringType: null);
        var call = new Method("Call", result, [new Parameter("f", new FunctionType(inputs, result))], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
        call.TypeParametersList.Add(result);
        return call;
    }

    private FunctionReferenceExpression Group(IReadOnlyList<IMistType> inputs)
    {
        var hello = new Method("Hello", Void, [.. inputs.Select((input, i) => new Parameter($"p{i}", input))], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = owner,
        };
        return new FunctionReferenceExpression(HandBuiltModel.Node(), null, hello, new FunctionType(inputs, Void));
    }

    private InvocationExpression Call(Method callee, Expression argument, IMistType closed)
    {
        var typeArguments = callee.TypeParametersList.Count == 0 ? null : new[] { Void };
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), callee, typeArguments), [argument],
            Void, callee, [closed]);
    }

    private static string Emit(Expression expression)
    {
        return BodyEmitter.Render(expression, new DiagnosticBag()).ReplaceLineEndings("\n");
    }
}
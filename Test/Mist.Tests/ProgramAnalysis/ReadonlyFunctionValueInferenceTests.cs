using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ReadonlyFunctionValueInferenceTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void AnUnknownFunctionTargetNeedsThePureTypePromise(bool pure, bool expectedReadonly)
    {
        var integer = new HandBuiltModel().Type("Int32");
        var function = new FunctionType([], integer, isPure: pure);
        var parameter = new Parameter("callback", function);
        var callback = new LocalVariable(parameter.Name, parameter.Type) { Parameter = parameter };
        var caller = new Method("Run", integer, [parameter], default)
        {
            DeclaringType = HandBuiltModel.Class("Runner"),
            Body = HandBuiltModel.Block(Invoke(callback, function)),
        };

        Assert.Equal(expectedReadonly, new ReadonlyInference().IsReadonly(caller));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void CallingAKnownPlainLambdaUsesItsBodyEffect(bool writesCapturedArray, bool invokesCallback, bool expectedReadonly)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var function = new FunctionType([], integer);
        var arrayType = model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [integer, new LiftedType(integer, 1L)]);
        var buffer = new LocalVariable("buffer", arrayType);
        var zero = HandBuiltModel.Literal(integer, 0L);
        var array = new ArrayLiteralExpression(HandBuiltModel.Node(), integer, [zero], arrayType);
        var element = new IndexExpression(HandBuiltModel.Node(), Reference(buffer), zero, integer);
        var read = new ReturnStatement(element, integer);
        var write = new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), element, HandBuiltModel.Literal(integer, 1L), integer));
        var lambdaBody = writesCapturedArray ? HandBuiltModel.Block(write, read) : HandBuiltModel.Block(read);
        var lambda = new LambdaExpression(HandBuiltModel.Node(), [], null, lambdaBody, false, function);
        var callback = new LocalVariable("callback", function);
        var caller = new Method("Run", integer, [], default)
        {
            DeclaringType = HandBuiltModel.Class("Runner"),
            Body = HandBuiltModel.Block(
                new LocalDeclarationStatement(buffer, array),
                new LocalDeclarationStatement(callback, lambda),
                invokesCallback ? Invoke(callback, function) : new ReturnStatement(zero, integer)),
        };

        Assert.Equal(expectedReadonly, new ReadonlyInference().IsReadonly(caller));
    }

    private static ReturnStatement Invoke(LocalVariable callback, FunctionType type)
    {
        var call = new InvocationExpression(HandBuiltModel.Node(), Reference(callback), [], type.ReturnType, method: null, type.ParameterTypes);
        return new ReturnStatement(call, type.ReturnType);
    }

    private static LocalReferenceExpression Reference(LocalVariable local)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), local);
    }
}
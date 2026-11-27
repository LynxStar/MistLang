using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class UnitResultBlockLambdaEmissionTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType owner = HandBuiltModel.Class("Program");

    private IMistType Void => model.Type("Void");

    [Fact]
    public void ATerminalBareReturnAtAGenericUnitResult_ReturnsTheUnitValueOnce()
    {
        var result = new TypeParameter("R", 0, owner.Scope, declaringType: null);
        var apply = CallbackMethod("Apply", result, new FunctionType([], result));
        result.DeclaringMethod = apply;
        apply.TypeParametersList.Add(result);
        var body = HandBuiltModel.Block(Record(), new ReturnStatement(null, Void));

        Assert.Equal("""
            Apply<Void>(() =>
            {
                Record();
                return Standard.Void.Instance;
            }
            )
            """, EmitCall(apply, body, [Void]));
    }

    [Fact]
    public void ATerminalBareReturnAtADeclaredVoidResult_StaysBare()
    {
        var run = CallbackMethod("Run", Void, new FunctionType([], Void));
        var body = HandBuiltModel.Block(Record(), new ReturnStatement(null, Void));

        Assert.Equal("""
            Run(() =>
            {
                Record();
                return;
            }
            )
            """, EmitCall(run, body));
    }

    [Fact]
    public void AFallthroughAtADeclaredVoidResult_EndsAfterTheLastStatement()
    {
        var run = CallbackMethod("Run", Void, new FunctionType([], Void));
        var body = HandBuiltModel.Block(Record());

        Assert.Equal("""
            Run(() =>
            {
                Record();
            }
            )
            """, EmitCall(run, body));
    }

    private Method CallbackMethod(string name, IMistType result, FunctionType slot)
    {
        return new Method(name, result, [new Parameter("callback", slot)], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
    }

    private ExpressionStatement Record()
    {
        var node = HandBuiltModel.Node();
        var record = new Method("Record", Void, [], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
        return new ExpressionStatement(new InvocationExpression(node, new MethodReferenceExpression(node, record), [], Void, record, []));
    }

    private string EmitCall(Method callee, Block body, IMistType[]? typeArguments = null)
    {
        var node = HandBuiltModel.Node();
        var closedSlot = new FunctionType([], Void);
        var lambda = new LambdaExpression(node, [], null, body, false, closedSlot);
        var call = new InvocationExpression(node, new MethodReferenceExpression(node, callee, typeArguments), [lambda], Void, callee, [closedSlot]);
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        var emitted = BodyEmitter.Render(call, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        return string.Join('\n', emitted.ReplaceLineEndings("\n").Split('\n').Select(line => line.TrimEnd()));
    }
}
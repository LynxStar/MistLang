using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class UnitResultMethodGroupEmissionTests
{
    [Fact]
    public void AnInstanceGroupAtAGenericUnitResult_BindsTheReceiverAndForwardsArgumentsOnce()
    {
        var model = new HandBuiltModel();
        var owner = HandBuiltModel.Class("Recorder");
        var unit = model.Type("Void");
        IMistType[] inputs = [model.Type("Int32"), model.Type("String")];
        var closedSlot = new FunctionType(inputs, unit);
        var record = new Method("Record", unit, [new Parameter("code", inputs[0]), new Parameter("message", inputs[1])], default)
        {
            DeclaringType = owner,
        };
        var create = new Method("Create", owner, [], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
        var node = HandBuiltModel.Node();
        var factory = new MemberAccessExpression(node, new TypeReferenceExpression(node, owner), create.Name, owner);
        var receiver = new InvocationExpression(node, factory, [], owner, create, []);
        var group = new FunctionReferenceExpression(node, receiver, record, closedSlot);
        var result = new TypeParameter("R", 0, owner.Scope, declaringType: null);
        var apply = new Method("Apply", result, [new Parameter("callback", new FunctionType(inputs, result))],
            new MemberModifiers { IsStatic = true })
        {
            DeclaringType = owner
        };
        result.DeclaringMethod = apply;
        apply.TypeParametersList.Add(result);
        var call = new InvocationExpression(node, new MethodReferenceExpression(node, apply, [unit]), [group], unit, apply, [closedSlot]);
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        var emitted = BodyEmitter.Render(call, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal(
            "Apply<Void>(((System.Func<Recorder, System.Func<int, string, Void>>)(__receiver => " +
            "(__arg0, __arg1) => { __receiver.Record(__arg0, __arg1); return Standard.Void.Instance; }))(Recorder.Create()))",
            emitted);
    }
}
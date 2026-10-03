using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class DelegateInvocationEmissionTests
{
    [Theory]
    [InlineData(ByRefDirection.None, "callback(value)")]
    [InlineData(ByRefDirection.Ref, "callback(ref value)")]
    [InlineData(ByRefDirection.In, "callback(in value)")]
    [InlineData(ByRefDirection.Out, "callback(out value)")]
    public void AValueCallSpellsTheProjectedInvokePassingDirection(ByRefDirection direction, string expected)
    {
        var integer = Type("Int32", "Standard");
        var nominal = Type("Callback", "Fixture");
        var parameter = new Parameter("value", integer,
            new ParameterModifier(direction == ByRefDirection.In, direction != ByRefDirection.None, OwnershipKind.Unrestricted, direction));
        var invoke = new Method("Invoke", integer, [parameter], new MemberModifiers { IsClrProjected = true });
        var call = new InvocationExpression(null!, Local("callback", nominal), [Local("value", integer)], integer, null, [integer])
        {
            InvokeMethod = invoke,
        };

        Assert.Null(call.Method);
        Assert.Equal(expected, BodyEmitter.Render(call, new DiagnosticBag()));
        Assert.Equal("Fixture.Callback", TypeNameEmitter.Emit(call.Receiver.Type));
    }

    [Fact]
    public void GenericInvokeCoercionUsesTheClosedArgumentTarget()
    {
        var integer = Type("Int32", "Standard");
        var predicate = Type("Predicate", "Fixture");
        predicate.BaseType = Type("MulticastDelegate", "System");
        var parameter = new TypeParameter("T", 0, new Scope(null), predicate);
        predicate.TypeParametersList.Add(parameter);
        predicate.MethodsList.Add(new Method("Invoke", integer, [new Parameter("input", parameter)], default));
        var open = new ConstructedType(predicate, [parameter]);
        var closed = new ConstructedType(predicate, [integer]);
        var invoke = new Method("Invoke", integer, [new Parameter("handler", open)], new MemberModifiers { IsClrProjected = true });
        var function = new FunctionType([integer], integer);
        var call = new InvocationExpression(null!, Local("callback", Type("Callback", "Fixture")), [Local("handler", function)], integer, null, [closed])
        {
            InvokeMethod = invoke,
        };

        Assert.Equal("callback(new Fixture.Predicate<int>((handler).Invoke))", BodyEmitter.Render(call, new DiagnosticBag()));
    }

    private static ClassType Type(string name, string namespaceName)
    {
        return new ClassType(name, namespaceName, null, null, null) { Assembly = MistAssembly.Ambient };
    }

    private static LocalReferenceExpression Local(string name, IMistType type)
    {
        return new LocalReferenceExpression(null!, new LocalVariable(name, type));
    }
}
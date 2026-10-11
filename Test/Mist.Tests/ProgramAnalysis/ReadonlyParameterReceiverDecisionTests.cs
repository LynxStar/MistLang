using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ReadonlyParameterReceiverDecisionTests
{
    private static readonly ClassType Outer = HandBuiltModel.Class("Outer");
    private static readonly Field Nested = new("Nested", Outer, default, default) { DeclaringType = Outer };
    private static readonly Field Shared = new("Shared", Outer, default, new MemberModifiers { IsStatic = true }) { DeclaringType = Outer };
    private static readonly Property View = new("View", Outer, default) { DeclaringType = Outer };

    [Theory]
    [InlineData("parameter")]
    [InlineData("readonly-ref-parameter")]
    [InlineData("field")]
    [InlineData("field.field")]
    [InlineData("field.field.field")]
    [InlineData("property.field")]
    [InlineData("field.property")]
    public void AnInstanceMemberChain_IsRootedInTheReadonlyParameterItStartsFrom(string chain)
    {
        var item = Parameter("item", isReadonly: true, isRef: chain == "readonly-ref-parameter");

        Assert.Same(item, ReadonlyParameterReceivers.RootOf(Chain(item, chain)));
    }

    [Theory]
    [InlineData("parameter")]
    [InlineData("field.field")]
    [InlineData("property.field")]
    public void AChainFromAPlainParameter_HasNoReadonlyRoot(string chain)
    {
        Assert.Null(ReadonlyParameterReceivers.RootOf(Chain(Parameter("item", isReadonly: false, isRef: false), chain)));
    }

    [Fact]
    public void AChainFromALocal_HasNoReadonlyRoot()
    {
        var copy = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("copy", Outer));

        Assert.Null(ReadonlyParameterReceivers.RootOf(Chain(copy, "field.field")));
    }

    [Fact]
    public void AChainThroughAStaticMember_HasNoReadonlyRoot()
    {
        var through = Access(Parameter("item", isReadonly: true, isRef: false), new FieldResult(Shared, Outer, []), "Shared");

        Assert.Null(ReadonlyParameterReceivers.RootOf(Access(through, new FieldResult(Nested, Outer, []), "Nested")));
    }

    [Fact]
    public void AChainThroughACallsResult_HasNoReadonlyRoot()
    {
        var item = Parameter("item", isReadonly: true, isRef: false);
        var copied = new Method("Copy", Outer, [], default) { DeclaringType = Outer };
        var call = new InvocationExpression(
            HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), item, "Copy", Outer), [], Outer, copied, []);

        Assert.Null(ReadonlyParameterReceivers.RootOf(Chain(call, "field")));
    }

    [Theory]
    [InlineData("parameter", "readonly parameter 'item'")]
    [InlineData("field", "'item.Nested', reached through readonly parameter 'item'")]
    [InlineData("property.field", "'item.View.Nested', reached through readonly parameter 'item'")]
    public void TheReceiverIsDescribedByItsPathFromTheParameter(string chain, string expected)
    {
        var item = Parameter("item", isReadonly: true, isRef: false);
        var receiver = Chain(item, chain);

        Assert.Equal(expected, ReadonlyParameterReceivers.Describe(receiver, ReadonlyParameterReceivers.RootOf(receiver)!));
    }

    private static LocalReferenceExpression Parameter(string name, bool isReadonly, bool isRef)
    {
        var direction = isRef ? ByRefDirection.In : ByRefDirection.None;
        var parameter = new Parameter(name, Outer, new ParameterModifier(isReadonly, isRef, OwnershipKind.Unrestricted, direction));

        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, Outer) { IsRef = isRef, Parameter = parameter });
    }

    private static Expression Chain(Expression root, string chain)
    {
        var current = root;

        foreach(var link in chain.Split('.'))
        {
            current = link switch
            {
                "field" => Access(current, new FieldResult(Nested, Outer, []), "Nested"),
                "property" => Access(current, new PropertyResult(View, Outer, []), "View"),
                _ => current,
            };
        }

        return current;
    }

    private static MemberAccessExpression Access(Expression receiver, LookupResult member, string name)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(), receiver, name, Outer, member: member);
    }
}
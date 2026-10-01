using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ReadonlyReferenceDecisionTests
{
    [Theory]
    [InlineData("readonly-ref-local", "view")]
    [InlineData("readonly-ref-parameter", "view")]
    [InlineData("readonly-ref-call", "ItemRef")]
    [InlineData("readonly-ref-property", "Current")]
    [InlineData("readonly-ref-member-property", "Current")]
    [InlineData("readonly-ref-indexer", "this[]")]
    public void AReadonlyReferenceIsNamedForTheDiagnostic(string shape, string expected)
    {
        Assert.Equal(expected, PositionDecision.ReadonlyReference(Shaped(shape)));
    }

    [Theory]
    [InlineData("ref-local")]
    [InlineData("ref-parameter")]
    [InlineData("value-local")]
    [InlineData("ref-call")]
    [InlineData("value-call")]
    [InlineData("ref-property")]
    public void AWritableReferenceOrAValueIsNotOne(string shape)
    {
        Assert.Null(PositionDecision.ReadonlyReference(Shaped(shape)));
    }

    [Theory]
    [InlineData(ByRefDirection.Ref, "passed as a ref argument")]
    [InlineData(ByRefDirection.Out, "passed as an out argument")]
    [InlineData(ByRefDirection.In, null)]
    [InlineData(ByRefDirection.None, null)]
    public void OnlyARefOrOutParameterTakesAWritableReference(ByRefDirection direction, string? expected)
    {
        var modifier = new ParameterModifier(direction == ByRefDirection.In, direction != ByRefDirection.None, OwnershipKind.Unrestricted, direction);

        Assert.Equal(expected, PositionDecision.WritableArgument(modifier));
    }

    private static Expression Shaped(string shape)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var receiver = HandBuiltModel.Literal(integer);
        var readonlyRef = new MemberModifiers { IsRef = true, ReturnsReadonlyRef = true };
        var current = new Property("Current", integer, readonlyRef);

        return shape switch
        {
            "readonly-ref-local" => Local(new LocalVariable("view", integer) { IsRef = true, IsReadonly = true }),
            "readonly-ref-parameter" => Local(Parameter("view", integer, isReadonly: true)),
            "readonly-ref-call" => Call(receiver, new Method("ItemRef", integer, [], readonlyRef)),
            "readonly-ref-property" => new PropertyReferenceExpression(HandBuiltModel.Node(), current),
            "readonly-ref-member-property" => new MemberAccessExpression(
                HandBuiltModel.Node(), receiver, "Current", integer, member: new PropertyResult(current, integer, [])),
            "readonly-ref-indexer" => new IndexExpression(HandBuiltModel.Node(), receiver, HandBuiltModel.Literal(integer, 1L), integer)
            {
                Indexer = new Indexer(integer, [new Parameter("index", integer)], readonlyRef),
            },
            "ref-local" => Local(new LocalVariable("alias", integer) { IsRef = true }),
            "ref-parameter" => Local(Parameter("alias", integer, isReadonly: false)),
            "value-local" => Local(new LocalVariable("copy", integer) { IsReadonly = true }),
            "ref-call" => Call(receiver, new Method("GetValueRefOrNullRef", integer, [], new MemberModifiers { IsRef = true })),
            "value-call" => Call(receiver, new Method("Abs", integer, [], default)),
            _ => new PropertyReferenceExpression(HandBuiltModel.Node(), new Property("Slot", integer, new MemberModifiers { IsRef = true })),
        };
    }

    private static LocalReferenceExpression Local(LocalVariable local)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), local);
    }

    private static LocalVariable Parameter(string name, IMistType type, bool isReadonly)
    {
        var direction = isReadonly ? ByRefDirection.In : ByRefDirection.Ref;
        var parameter = new Parameter(name, type, new ParameterModifier(isReadonly, true, OwnershipKind.Unrestricted, direction));

        return new LocalVariable(name, type) { IsRef = true, Parameter = parameter };
    }

    private static InvocationExpression Call(Expression receiver, Method method)
    {
        return new InvocationExpression(HandBuiltModel.Node(), receiver, [], method.ReturnType, method, null);
    }
}
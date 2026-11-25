using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ThisTierDecisionTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType cell = HandBuiltModel.Class("Cell");

    [Theory]
    [InlineData(ThisTier.None, "unrestricted")]
    [InlineData(ThisTier.ReadonlyRef, "readonly ref")]
    [InlineData(ThisTier.Frozen, "frozen")]
    [InlineData(ThisTier.Borrowed, "borrowed")]
    [InlineData(ThisTier.Owned, "owned")]
    public void ATierIsNamedAsItIsWritten(ThisTier tier, string named)
    {
        Assert.Equal(named, ThisTiers.Display(tier));
    }

    [Theory]
    [InlineData("readonly-ref-local", ThisTier.ReadonlyRef)]
    [InlineData("readonly-ref-parameter", ThisTier.ReadonlyRef)]
    [InlineData("ref-local", ThisTier.None)]
    [InlineData("readonly-local", ThisTier.None)]
    [InlineData("readonly-parameter", ThisTier.None)]
    public void AReadonlyRefBindingProvidesReadonlyRef_AndNoOtherRefOrReadonlyBindingDoes(string binding, ThisTier expected)
    {
        var isRef = binding.Contains("ref", StringComparison.Ordinal);
        var local = binding.EndsWith("parameter", StringComparison.Ordinal)
            ? new LocalVariable("p", cell) { IsRef = isRef, Parameter = new Parameter("p", cell, new ParameterModifier(true, isRef, OwnershipKind.Unrestricted)) }
            : new LocalVariable("l", cell) { IsRef = isRef, IsReadonly = binding.StartsWith("readonly", StringComparison.Ordinal) };

        Assert.Equal(expected, ThisTiers.Declared(local));
    }

    [Theory]
    [InlineData("owned-property-access", "owned property 'Item'")]
    [InlineData("owned-property-reference", "owned property 'Item'")]
    [InlineData("owned-field-access", "owned field 'Item'")]
    [InlineData("owned-field-reference", "owned field 'Item'")]
    [InlineData("static-owned-property-access", null)]
    [InlineData("static-owned-property-reference", null)]
    [InlineData("plain-property-access", null)]
    [InlineData("plain-field-access", null)]
    [InlineData("plain-field-reference", null)]
    public void OnlyAnOwnedInstanceFieldOrProperty_IsAnOwnedSlot_NamedByItsKind(string target, string? slot)
    {
        var receiver = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("c", cell));
        var owned = target.Contains("owned", StringComparison.Ordinal);
        Expression written;
        if(target.Contains("field", StringComparison.Ordinal))
        {
            var field = new Field("Item", cell, owned ? OwnershipKind.Owned : OwnershipKind.Unrestricted, default) { DeclaringType = cell };
            written = target.EndsWith("reference", StringComparison.Ordinal)
                ? new FieldReferenceExpression(HandBuiltModel.Node(), field)
                : new MemberAccessExpression(HandBuiltModel.Node(), receiver, field.Name, field.Type, member: new FieldResult(field, cell, []));
        }
        else
        {
            var property = new Property("Item", cell, new MemberModifiers { IsOwned = owned, IsStatic = target.StartsWith("static", StringComparison.Ordinal) });
            written = target.EndsWith("reference", StringComparison.Ordinal)
                ? new PropertyReferenceExpression(HandBuiltModel.Node(), property)
                : new MemberAccessExpression(HandBuiltModel.Node(), receiver, property.Name, property.Type, member: new PropertyResult(property, cell, []));
        }

        Assert.Equal(slot, ThisTiers.OwnedSlot(written));
    }

    [Theory]
    [InlineData("member-access")]
    [InlineData("function-reference")]
    [InlineData("method-group")]
    public void ACallsReceiver_IsWhatItsCalleeWasReachedThrough(string callee)
    {
        var receiver = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("c", cell));
        var method = new Method("Run", model.Type("Void"), [], default) { DeclaringType = cell };
        Expression reached = callee switch
        {
            "member-access" => new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType),
            "function-reference" => new FunctionReferenceExpression(HandBuiltModel.Node(), receiver, method, new FunctionType([], method.ReturnType)),
            _ => new MethodGroupExpression(HandBuiltModel.Node(), receiver, method.Name, [method], null),
        };

        Assert.Same(receiver, ThisTiers.ReceiverOf(new InvocationExpression(HandBuiltModel.Node(), reached, [], method.ReturnType, method, [])));
    }

    [Fact]
    public void ACallByBareName_HasNoReceiver()
    {
        var method = new Method("Run", model.Type("Void"), [], default) { DeclaringType = cell };
        var call = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), [], method.ReturnType, method, []);

        Assert.Null(ThisTiers.ReceiverOf(call));
    }
}
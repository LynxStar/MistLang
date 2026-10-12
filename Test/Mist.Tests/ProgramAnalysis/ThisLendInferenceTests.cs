// Tier 2 (Functional): what a method body demands of `this` when it lends `this` to a parameter, and
// which fields of `this` it touches, read off hand-built bodies by ThisTiers.

using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ThisLendInferenceTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType cell = HandBuiltModel.Class("Cell");

    [Theory]
    [InlineData(OwnershipKind.Borrowed, ThisTier.Borrowed)]
    [InlineData(OwnershipKind.Frozen, ThisTier.Frozen)]
    [InlineData(OwnershipKind.Unrestricted, ThisTier.None)]
    public void LendingThisToAParameter_NeedsThatParametersTier(OwnershipKind lent, ThisTier expected)
    {
        var method = Lending(cell, lent);

        Assert.Equal(expected, new ThisTiers().Inferred(method));
    }

    [Fact]
    public void LendingAStructThis_NeedsNothing()
    {
        Assert.Equal(ThisTier.None, new ThisTiers().Inferred(Lending(HandBuiltModel.Struct("Vec"), OwnershipKind.Borrowed)));
    }

    [Fact]
    public void TheFieldsAMethodTouches_AreCollectedThroughItsHelpers()
    {
        var left = new Field("Left", cell, OwnershipKind.Owned, default) { DeclaringType = cell };
        var right = new Field("Right", cell, OwnershipKind.Owned, default) { DeclaringType = cell };
        var self = new ThisExpression(HandBuiltModel.Node(), cell);
        var helper = new Method("Helper", model.Type("Void"), [], default)
        {
            DeclaringType = cell,
            Body = HandBuiltModel.Block(new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), Access(self, right), Access(self, left), cell))),
        };
        var reader = new Method("Read", cell, [], default)
        {
            DeclaringType = cell,
            Body = HandBuiltModel.Block(
                new ExpressionStatement(new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), helper), [], helper.ReturnType, helper, [])),
                new ReturnStatement(new FieldReferenceExpression(HandBuiltModel.Node(), left), cell)),
        };
        cell.MethodsList.AddRange([helper, reader]);

        var uses = new ThisTiers().FieldUses(reader);

        Assert.Equal(FieldUse.Reads, uses[left]);
        Assert.Equal(FieldUse.Writes, uses[right]);
    }

    private Method Lending(ConcreteType type, OwnershipKind lent)
    {
        var take = new Method("Take", model.Type("Void"), [new Parameter("x", type, new ParameterModifier(false, false, lent))], new MemberModifiers { IsStatic = true });
        var call = new InvocationExpression(
            HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), take), [new ThisExpression(HandBuiltModel.Node(), type)], take.ReturnType, take, [type]);

        return new Method("Lend", model.Type("Void"), [], default) { DeclaringType = type, Body = HandBuiltModel.Block(new ExpressionStatement(call)) };
    }

    private static MemberAccessExpression Access(Expression receiver, Field field)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(), receiver, field.Name, field.Type, member: new FieldResult(field, receiver.Type, []));
    }
}
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ThisTierInferenceTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType cell = HandBuiltModel.Class("Cell");

    [Theory]
    [InlineData("compound", true, ThisTier.Borrowed)]
    [InlineData("compound", false, ThisTier.None)]
    [InlineData("compound-through-this", true, ThisTier.Borrowed)]
    [InlineData("compound-through-this", false, ThisTier.None)]
    [InlineData("assign-through-this", true, ThisTier.Borrowed)]
    [InlineData("assign-through-this", false, ThisTier.None)]
    [InlineData("deconstruct", true, ThisTier.Borrowed)]
    [InlineData("deconstruct", false, ThisTier.None)]
    [InlineData("deconstruct-through-this", true, ThisTier.Borrowed)]
    [InlineData("deconstruct-through-this", false, ThisTier.None)]
    public void WritingAField_NeedsBorrowedOnlyWhenTheFieldIsOwned(string shape, bool owned, ThisTier expected)
    {
        var field = CellField("N", owned ? OwnershipKind.Owned : OwnershipKind.Unrestricted);
        var spelled = shape.EndsWith("through-this", StringComparison.Ordinal) ? Access(This(), field) : (Expression)Read(field);
        var one = HandBuiltModel.Literal(field.Type, 1L);
        Expression write = shape.StartsWith("compound", StringComparison.Ordinal)
            ? new CompoundAssignmentExpression(HandBuiltModel.Node(), spelled, BinaryOperatorKind.Add, one, field.Type, null, IncrementForm.None)
            : shape.StartsWith("deconstruct", StringComparison.Ordinal)
                ? new AssignmentExpression(HandBuiltModel.Node(), Tuple(spelled, Local("spare")), Tuple(one, HandBuiltModel.Literal(field.Type, 2L)), cell)
                : new AssignmentExpression(HandBuiltModel.Node(), spelled, one, field.Type);

        Assert.Equal(expected, new ThisTiers().Inferred(Body("Touch", write)));
    }

    [Theory]
    [InlineData(ThisTier.ReadonlyRef)]
    [InlineData(ThisTier.Frozen)]
    [InlineData(ThisTier.Borrowed)]
    [InlineData(ThisTier.Owned)]
    public void CallingAMethodOnThis_NeedsTheCalleesDeclaredTier(ThisTier declared)
    {
        var callee = new Method("Close", Void, [], default) { DeclaringType = cell, DeclaredThisTier = declared };

        Assert.Equal(declared, new ThisTiers().Inferred(Body("Finish", CallOn(This(), callee))));
    }

    [Fact]
    public void CallingAMethodOnAnotherReceiver_NeedsNothingOfThis()
    {
        var callee = new Method("Close", Void, [], default) { DeclaringType = cell, DeclaredThisTier = ThisTier.Owned };

        Assert.Equal(ThisTier.None, new ThisTiers().Inferred(Body("Finish", CallOn(Local("other"), callee))));
    }

    [Fact]
    public void ACallOnThisThatAlsoLendsThis_NeedsTheGreaterOfTheTwo()
    {
        var parameter = new Parameter("x", cell, new ParameterModifier(false, false, OwnershipKind.Borrowed));
        var callee = new Method("Merge", Void, [parameter], default) { DeclaringType = cell, DeclaredThisTier = ThisTier.ReadonlyRef };

        Assert.Equal(ThisTier.Borrowed, new ThisTiers().Inferred(Body("Fold", CallOn(This(), callee, This()))));
    }

    [Theory]
    [InlineData(true, ThisTier.Borrowed)]
    [InlineData(false, ThisTier.None)]
    public void AMethodCallingItselfOnThis_NeedsWhatItsOwnBodyDoes(bool writesOwnedField, ThisTier expected)
    {
        var field = CellField("N", writesOwnedField ? OwnershipKind.Owned : OwnershipKind.Unrestricted);
        var method = new Method("Spin", Void, [], default) { DeclaringType = cell, Body = HandBuiltModel.Block() };
        method.Body.Statements =
        [
            new ExpressionStatement(CallOn(This(), method)),
            new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), Read(field), HandBuiltModel.Literal(field.Type, 1L), field.Type)),
        ];

        Assert.Equal(expected, new ThisTiers().Inferred(method));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACompoundWriteToAField_BothReadsAndWritesIt(bool throughThis)
    {
        var field = CellField("N", OwnershipKind.Owned);
        var target = throughThis ? Access(This(), field) : (Expression)Read(field);
        var bump = new CompoundAssignmentExpression(HandBuiltModel.Node(), target, BinaryOperatorKind.Add, HandBuiltModel.Literal(field.Type, 1L), field.Type, null, IncrementForm.None);

        Assert.Equal(FieldUse.Reads | FieldUse.Writes, new ThisTiers().FieldUses(Body("Bump", bump))[field]);
    }

    [Fact]
    public void AFieldAMethodReadsAndItsHelperWrites_IsBothReadAndWritten()
    {
        var field = CellField("N", OwnershipKind.Owned);
        var helper = Body("Reset", new AssignmentExpression(HandBuiltModel.Node(), Read(field), HandBuiltModel.Literal(field.Type, 0L), field.Type));
        var caller = new Method("Drain", Void, [], default)
        {
            DeclaringType = cell,
            Body = HandBuiltModel.Block(
                new LocalDeclarationStatement(new LocalVariable("was", field.Type), Read(field)),
                new ExpressionStatement(new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), helper), [], Void, helper, []))),
        };

        Assert.Equal(FieldUse.Reads | FieldUse.Writes, new ThisTiers().FieldUses(caller)[field]);
    }

    [Fact]
    public void AHelperCalledOnAnotherReceiver_TouchesNoFieldOfThis()
    {
        var field = CellField("N", OwnershipKind.Owned);
        var helper = Body("Reset", new AssignmentExpression(HandBuiltModel.Node(), Read(field), HandBuiltModel.Literal(field.Type, 0L), field.Type));

        Assert.Empty(new ThisTiers().FieldUses(Body("Clear", CallOn(Local("other"), helper))));
    }

    [Fact]
    public void AStaticField_IsNotAFieldOfThis()
    {
        var shared = new Field("Count", model.Type("Int32"), OwnershipKind.Owned, new MemberModifiers { IsStatic = true }) { DeclaringType = cell };
        var bump = new CompoundAssignmentExpression(HandBuiltModel.Node(), Read(shared), BinaryOperatorKind.Add, HandBuiltModel.Literal(shared.Type, 1L), shared.Type, null, IncrementForm.None);

        Assert.Empty(new ThisTiers().FieldUses(Body("Count", bump)));
    }

    private ClassType Void => model.Type("Void");

    private Field CellField(string name, OwnershipKind ownership)
    {
        return new Field(name, model.Type("Int32"), ownership, default) { DeclaringType = cell };
    }

    private Method Body(string name, Expression statement)
    {
        return new Method(name, Void, [], default) { DeclaringType = cell, Body = HandBuiltModel.Block(new ExpressionStatement(statement)) };
    }

    private ThisExpression This()
    {
        return new ThisExpression(HandBuiltModel.Node(), cell);
    }

    private LocalReferenceExpression Local(string name)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, cell));
    }

    private static FieldReferenceExpression Read(Field field)
    {
        return new FieldReferenceExpression(HandBuiltModel.Node(), field);
    }

    private static MemberAccessExpression Access(Expression receiver, Field field)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(), receiver, field.Name, field.Type, member: new FieldResult(field, receiver.Type, []));
    }

    private TupleLiteralExpression Tuple(params Expression[] elements)
    {
        return new TupleLiteralExpression(HandBuiltModel.Node(), elements, [.. elements.Select(_ => (string?)null)], cell);
    }

    private static InvocationExpression CallOn(Expression receiver, Method method, params Expression[] arguments)
    {
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType);
        return new InvocationExpression(HandBuiltModel.Node(), callee, arguments, method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }
}
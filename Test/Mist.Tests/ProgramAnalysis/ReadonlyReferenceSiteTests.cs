using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ReadonlyReferenceSiteTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData("ref-local", "'ItemRef' is a readonly reference, so it cannot be bound to ref local 'slot'.")]
    [InlineData("assignment", "'ItemRef' is a readonly reference, so it cannot be assigned to.")]
    [InlineData("ref-return", "'view' is a readonly reference, so it cannot be returned by ref.")]
    [InlineData("ref-argument", "'ItemRef' is a readonly reference, so it cannot be passed as a ref argument.")]
    [InlineData("out-argument", "'ItemRef' is a readonly reference, so it cannot be passed as an out argument.")]
    [InlineData("constructor-argument", "'ItemRef' is a readonly reference, so it cannot be passed as a ref argument.")]
    public void AWritableReferenceTakenThroughAReadonlyOneIsRefusedWhereItIsTaken(string shape, string message)
    {
        var integer = Integer;
        object root = shape switch
        {
            "ref-local" => new LocalDeclarationStatement(new LocalVariable("slot", integer) { IsRef = true }, ItemRef(37)),
            "assignment" => new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(37), ItemRef(), HandBuiltModel.Literal(integer, 5L), integer)),
            "ref-return" => new ReturnStatement(View(37), integer, isRef: true),
            "ref-argument" => Passing(ItemRef(37), ByRefDirection.Ref),
            "out-argument" => Passing(ItemRef(37), ByRefDirection.Out),
            _ => new NewExpression(HandBuiltModel.Node(), integer, [ItemRef(37)],
                new Constructor("Holder", integer, [RefParameter(ByRefDirection.Ref)], default), [], null),
        };

        var diagnostic = Assert.Single(model.Analyze(new PositionAnalyzer(), root));
        Assert.Equal("MIST_PA_READONLY_REF_WRITTEN", diagnostic.Id.Value);
        Assert.StartsWith(message, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(38, diagnostic.Location.Start.Column);
    }

    [Theory]
    [InlineData("readonly-ref-local")]
    [InlineData("value-local")]
    [InlineData("in-argument")]
    [InlineData("writable-ref-call")]
    public void ReadingOrHoldingItReadonlyIsAdmitted(string shape)
    {
        var integer = Integer;
        object root = shape switch
        {
            "readonly-ref-local" => new LocalDeclarationStatement(new LocalVariable("view", integer) { IsRef = true, IsReadonly = true }, ItemRef()),
            "value-local" => new LocalDeclarationStatement(new LocalVariable("copy", integer), ItemRef()),
            "in-argument" => Passing(ItemRef(), ByRefDirection.In),
            _ => new LocalDeclarationStatement(
                new LocalVariable("slot", integer) { IsRef = true },
                Call(new Method("GetValueRefOrNullRef", integer, [], new MemberModifiers { IsRef = true }), [])),
        };

        Assert.Empty(model.Analyze(new PositionAnalyzer(), root));
    }

    [Fact]
    public void AssigningThroughAReadonlyRefLocalIsTheReassignmentRulesComplaintAlone()
    {
        var assignment = new AssignmentExpression(HandBuiltModel.Node(37), View(), HandBuiltModel.Literal(Integer, 5L), Integer);

        var diagnostic = Assert.Single(model.Analyze(new PositionAnalyzer(), new ExpressionStatement(assignment)));
        Assert.Equal(ProgramAnalysisDiagnostics.ReadonlyLocalReassigned.Id, diagnostic.Id);
    }

    private IMistType Integer => model.Type("Int32");

    private InvocationExpression ItemRef(int offset = 0)
    {
        return Call(new Method("ItemRef", Integer, [], new MemberModifiers { IsRef = true, ReturnsReadonlyRef = true }), [], offset);
    }

    private LocalReferenceExpression View(int offset = 0)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(offset), new LocalVariable("view", Integer) { IsRef = true, IsReadonly = true });
    }

    private InvocationExpression Passing(Expression argument, ByRefDirection direction)
    {
        return Call(new Method("Exchange", Integer, [RefParameter(direction)], default), [argument]);
    }

    private Parameter RefParameter(ByRefDirection direction)
    {
        return new Parameter("location", Integer, new ParameterModifier(direction == ByRefDirection.In, true, OwnershipKind.Unrestricted, direction));
    }

    private InvocationExpression Call(Method method, IReadOnlyList<Expression> arguments, int offset = 0)
    {
        return new InvocationExpression(HandBuiltModel.Node(offset), HandBuiltModel.Literal(Integer), arguments, method.ReturnType, method, null);
    }
}
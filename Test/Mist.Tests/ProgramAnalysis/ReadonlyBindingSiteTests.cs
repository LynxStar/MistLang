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
public sealed class ReadonlyBindingSiteTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData("parameter-assigned", "Readonly parameter 'count' cannot be reassigned.")]
    [InlineData("parameter-incremented", "Readonly parameter 'count' cannot be reassigned.")]
    [InlineData("in-parameter-assigned", "Readonly parameter 'view' cannot be reassigned.")]
    [InlineData("parameter-ref-argument", "Readonly parameter 'count' cannot be passed as a ref argument.")]
    [InlineData("parameter-out-argument", "Readonly parameter 'count' cannot be passed as an out argument.")]
    [InlineData("local-assigned", "Readonly local variable 'total' cannot be reassigned.")]
    [InlineData("local-ref-argument", "Readonly local variable 'total' cannot be passed as a ref argument.")]
    public void AReadonlyBindingIsRefusedWhereItIsWritten(string shape, string message)
    {
        var count = Parameter("count", isReadonly: true, ByRefDirection.None);
        var total = new LocalVariable("total", Integer) { IsReadonly = true };
        object root = shape switch
        {
            "parameter-assigned" => Assign(count),
            "parameter-incremented" => new ExpressionStatement(new CompoundAssignmentExpression(
                HandBuiltModel.Node(37), Read(count), BinaryOperatorKind.Add, HandBuiltModel.Literal(Integer, 1L), Integer, null, IncrementForm.Postfix)),
            "in-parameter-assigned" => Assign(Parameter("view", isReadonly: true, ByRefDirection.In)),
            "parameter-ref-argument" => Passing(Read(count, 37), ByRefDirection.Ref),
            "parameter-out-argument" => Passing(Read(count, 37), ByRefDirection.Out),
            "local-assigned" => Assign(total),
            _ => Passing(Read(total, 37), ByRefDirection.Ref),
        };

        var diagnostic = Assert.Single(model.Analyze(new PositionAnalyzer(), root));
        Assert.Equal("MIST0207", diagnostic.Number.Value);
        Assert.StartsWith(message, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(38, diagnostic.Position().Start.Column);
    }

    [Theory]
    [InlineData("parameter-read")]
    [InlineData("parameter-in-argument")]
    [InlineData("parameter-by-value")]
    [InlineData("plain-parameter-assigned")]
    [InlineData("plain-parameter-ref-argument")]
    public void ReadingOrLendingItReadonlyIsAdmitted(string shape)
    {
        var count = Parameter("count", isReadonly: true, ByRefDirection.None);
        var plain = Parameter("count", isReadonly: false, ByRefDirection.None);
        object root = shape switch
        {
            "parameter-read" => new ExpressionStatement(Read(count)),
            "parameter-in-argument" => Passing(Read(count), ByRefDirection.In),
            "parameter-by-value" => Call(new Method("Use", Integer, [new Parameter("value", Integer)], default), [Read(count)]),
            "plain-parameter-assigned" => Assign(plain),
            _ => Passing(Read(plain), ByRefDirection.Ref),
        };

        Assert.Empty(model.Analyze(new PositionAnalyzer(), root));
    }

    private IMistType Integer => model.Type("Int32");

    private LocalVariable Parameter(string name, bool isReadonly, ByRefDirection direction)
    {
        var isRef = direction != ByRefDirection.None;
        var modifier = new ParameterModifier(isReadonly, isRef, OwnershipKind.Unrestricted, direction);
        return new LocalVariable(name, Integer) { IsRef = isRef, Parameter = new Parameter(name, Integer, modifier) };
    }

    private static LocalReferenceExpression Read(LocalVariable local, int offset = 0)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(offset), local);
    }

    private ExpressionStatement Assign(LocalVariable local)
    {
        return new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(37), Read(local), HandBuiltModel.Literal(Integer, 5L), Integer));
    }

    private InvocationExpression Passing(Expression argument, ByRefDirection direction)
    {
        var modifier = new ParameterModifier(direction == ByRefDirection.In, true, OwnershipKind.Unrestricted, direction);
        return Call(new Method("Exchange", Integer, [new Parameter("location", Integer, modifier)], default), [argument]);
    }

    private InvocationExpression Call(Method method, IReadOnlyList<Expression> arguments)
    {
        return new InvocationExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(Integer), arguments, method.ReturnType, method, null);
    }
}
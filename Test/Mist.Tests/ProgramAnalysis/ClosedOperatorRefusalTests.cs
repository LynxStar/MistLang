using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ClosedOperatorRefusalTests
{
    private readonly HandBuiltModel model = new();

    [Fact]
    public void ABinaryTie_IsRefusedOnceAtTheExpressionNamingEachOwner()
    {
        var (left, right, tied) = Tie(OperatorKind.Plus);
        var binary = new BinaryExpression(
            HandBuiltModel.Node(37), HandBuiltModel.Literal(left), BinaryOperatorKind.Add, HandBuiltModel.Literal(right), ErrorType.Instance)
        {
            AmbiguousOperators = tied,
        };

        var finding = Assert.Single(model.Analyze(new OperatorAnalyzer(), binary));

        Assert.Equal("MIST0386", finding.Number.Value);
        Assert.Equal(38, finding.Position().Start.Column);
        var callee = Assert.IsType<SymbolValue>(finding.Arguments[0]);
        Assert.Equal("operator '+'", callee.Spelling);
        Assert.Contains("Left", callee.Identity, StringComparison.Ordinal);
        Assert.Contains("Right", callee.Identity, StringComparison.Ordinal);
        var candidates = Assert.IsType<SequenceValue>(finding.Arguments[1]);
        Assert.Equal(SlotKind.Symbol, candidates.ElementKind);
        Assert.Equal(2, candidates.Items.Length);
        Assert.NotEqual(Assert.IsType<SymbolValue>(candidates.Items[0]).Identity, Assert.IsType<SymbolValue>(candidates.Items[1]).Identity);
        Assert.Equal(
            "The call to operator '+' is ambiguous. These overloads are equally applicable: "
            + "Left.operator +(Left, Right), Right.operator +(Left, Right). Cast an argument to choose one.",
            finding.Message);
    }

    [Fact]
    public void AUnaryTie_IsRefusedOnceAtTheExpression()
    {
        var operand = HandBuiltModel.Class("Shape");
        var tied = new[] { Closed(HandBuiltModel.Class("Base"), OperatorKind.Minus, operand), Closed(operand, OperatorKind.Minus, operand) };
        var unary = new UnaryExpression(HandBuiltModel.Node(37), UnaryOperatorKind.Negate, HandBuiltModel.Literal(operand), ErrorType.Instance)
        {
            AmbiguousOperators = tied,
        };

        var finding = Assert.Single(model.Analyze(new OperatorAnalyzer(), unary));

        Assert.Equal("MIST0386", finding.Number.Value);
        Assert.Equal(38, finding.Position().Start.Column);
        var callee = Assert.IsType<SymbolValue>(finding.Arguments[0]);
        Assert.Equal("operator '-'", callee.Spelling);
        Assert.Contains("Base", callee.Identity, StringComparison.Ordinal);
        Assert.Contains("Shape", callee.Identity, StringComparison.Ordinal);
        var candidates = Assert.IsType<SequenceValue>(finding.Arguments[1]);
        Assert.Equal(SlotKind.Symbol, candidates.ElementKind);
        Assert.Equal(2, candidates.Items.Length);
        Assert.NotEqual(Assert.IsType<SymbolValue>(candidates.Items[0]).Identity, Assert.IsType<SymbolValue>(candidates.Items[1]).Identity);
        Assert.Contains("operator '-'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATieOverAFailedOperand_KeepsTheOperandsComplaint()
    {
        var (_, right, tied) = Tie(OperatorKind.Plus);
        var binary = new BinaryExpression(
            HandBuiltModel.Node(37), HandBuiltModel.Literal(ErrorType.Instance), BinaryOperatorKind.Add, HandBuiltModel.Literal(right), ErrorType.Instance)
        {
            AmbiguousOperators = tied,
        };

        Assert.Empty(model.Analyze(new OperatorAnalyzer(), binary));
    }

    [Fact]
    public void ASelectedClosedOperator_IsNotRefused()
    {
        var (left, right, tied) = Tie(OperatorKind.Plus);
        var text = model.Type("String");
        var binary = new BinaryExpression(
            HandBuiltModel.Node(37), HandBuiltModel.Literal(left), BinaryOperatorKind.Add, HandBuiltModel.Literal(right), text)
        {
            Operator = tied[0].Declaration,
            ParameterTypes = tied[0].ParameterTypes,
        };

        Assert.Empty(model.Analyze(new OperatorAnalyzer(), binary));
    }

    private (ClassType Left, ClassType Right, ClosedOperator[] Tied) Tie(OperatorKind kind)
    {
        var left = HandBuiltModel.Class("Left");
        var right = HandBuiltModel.Class("Right");

        return (left, right, [Closed(left, kind, left, right), Closed(right, kind, left, right)]);
    }

    private ClosedOperator Closed(ClassType owner, OperatorKind kind, params IMistType[] parameters)
    {
        var declared = new Operator(
            kind, model.Type("String"), [.. parameters.Select((type, i) => new Parameter($"p{i}", type))], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = owner,
        };

        return new ClosedOperator(declared, owner, TypeSubstitution.Empty, parameters, declared.ReturnType);
    }
}
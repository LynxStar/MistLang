using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ClosedOperatorBindingTests
{
    private readonly BindingFragments f = new();

    private ClassType Integer => f.Built.Type("Int32");

    [Fact]
    public void AGenericOwnersOperator_BindsItsDeclarationOverClosedOperandsAndResult()
    {
        var (tag, held, plus, _) = Tag();
        var closed = Of(tag, Integer);
        _ = f.Local("t", closed);

        var bound = Assert.IsType<BinaryExpression>(Bind(MistAstGenerator.MakeAdditive(Name("t"), Name("t"))));

        Assert.Same(plus, bound.Operator);
        Assert.Same(Integer, bound.Type);
        Assert.Equal(new IMistType[] { closed, closed }, bound.ParameterTypes);
        Assert.Same(Integer, bound.OperatorClosing.Apply(held, f.Built.Interner));
        Assert.Empty(bound.AmbiguousOperators);
        _ = Assert.IsType<LocalReferenceExpression>(bound.Left);
        _ = Assert.IsType<LocalReferenceExpression>(bound.Right);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AGenericOwnersUnaryOperator_BindsItsClosedReturn()
    {
        var (tag, held, _, negate) = Tag();
        var closed = Of(tag, Integer);
        _ = f.Local("t", closed);

        var bound = Assert.IsType<UnaryExpression>(Bind(MistAstGenerator.MakeNegate(Name("t"))));

        Assert.Same(negate, bound.Operator);
        Assert.Same(closed, bound.Type);
        Assert.Same(Integer, bound.OperatorClosing.Apply(held, f.Built.Interner));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheBetterOverload_TypesAnInferredLocal(bool exactFirst)
    {
        var money = HandBuiltModel.Struct("Money");
        var text = f.Built.Type("String");
        Operator exact;
        if(exactFirst)
        {
            exact = Declare(money, OperatorKind.Plus, Integer, money, Integer);
            _ = Declare(money, OperatorKind.Plus, text, money, f.Built.Type("Int64"));
        }
        else
        {
            _ = Declare(money, OperatorKind.Plus, text, money, f.Built.Type("Int64"));
            exact = Declare(money, OperatorKind.Plus, Integer, money, Integer);
        }

        _ = f.Local("m", money);
        var one = MistAstGenerator.WrapPrimary(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("1") });

        var statement = Assert.IsType<LocalDeclarationStatement>(StatementBinder.BindStatement(
            new VariableDeclarationStatementStatementNode
            {
                VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
                {
                    Name = BindingFragments.Token("result"),
                    Initializer = MistAstGenerator.MakeAdditive(Name("m"), one),
                },
            }, f.Binding, f.Builder));

        Assert.Same(exact, Assert.IsType<BinaryExpression>(statement.Initializer).Operator);
        Assert.Same(Integer, statement.Local.Type);
    }

    [Fact]
    public void ADeclaredConversion_ConvertsTheOperandToTheClosedParameter()
    {
        var (tag, _, plus, _) = Tag();
        var closed = Of(tag, Integer);
        var meters = HandBuiltModel.Class("Meters");
        var toTag = HandBuiltModel.Conversion(meters, meters, closed);
        _ = f.Local("t", closed);
        _ = f.Local("m", meters);

        var bound = Assert.IsType<BinaryExpression>(Bind(MistAstGenerator.MakeAdditive(Name("t"), Name("m"))));

        Assert.Same(plus, bound.Operator);
        _ = Assert.IsType<LocalReferenceExpression>(bound.Left);
        var converted = Assert.IsType<ImplicitConversionExpression>(bound.Right);
        Assert.Same(toTag, converted.Operator);
        Assert.Same(closed, converted.Type);
    }

    [Fact]
    public void EquallyApplicableOperators_LeaveTheirTieToAnalysis()
    {
        var left = HandBuiltModel.Class("Left");
        var right = HandBuiltModel.Class("Right");
        var text = f.Built.Type("String");
        var fromLeft = Declare(left, OperatorKind.Plus, text, left, right);
        var fromRight = Declare(right, OperatorKind.Plus, text, left, right);
        _ = f.Local("l", left);
        _ = f.Local("r", right);

        var bound = Assert.IsType<BinaryExpression>(Bind(MistAstGenerator.MakeAdditive(Name("l"), Name("r"))));

        _ = Assert.IsType<ErrorType>(bound.Type);
        Assert.Null(bound.Operator);
        Assert.Equal(new[] { fromLeft, fromRight }, bound.AmbiguousOperators.Select(candidate => candidate.Declaration));
        Assert.True(FailureDependency.NeedsAnalysis(bound));
        Assert.False(PriorFailure.Of(bound));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AFailedOperand_KeepsItsCauseAndSelectsNothing()
    {
        var (tag, _, _, _) = Tag();
        _ = f.Local("broken", ErrorType.Instance, failed: true);
        _ = f.Local("t", Of(tag, Integer));

        var bound = Assert.IsType<BinaryExpression>(Bind(MistAstGenerator.MakeAdditive(Name("broken"), Name("t"))));

        _ = Assert.IsType<ErrorType>(bound.Type);
        Assert.Null(bound.Operator);
        Assert.Empty(bound.AmbiguousOperators);
        Assert.True(PriorFailure.Of(bound));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private Expression Bind(ExpressionNode expression)
    {
        return ExpressionBinder.BindExpression(expression, f.Binding, f.Builder);
    }

    private static ExpressionNode Name(string name)
    {
        return BindingFragments.Expression(BindingFragments.Identifier(name));
    }

    private ConstructedType Of(ClassType definition, params IMistType[] arguments)
    {
        return f.Built.Interner.GetOrCreateConstructedType(definition, arguments);
    }

    private (ClassType Tag, TypeParameter Held, Operator Plus, Operator Negate) Tag()
    {
        var tag = HandBuiltModel.Class("Tag");
        var held = new TypeParameter("T", 0, tag.Scope, tag);
        tag.TypeParametersList.Add(held);
        var open = Of(tag, held);

        return (tag, held, Declare(tag, OperatorKind.Plus, held, open, open), Declare(tag, OperatorKind.Minus, open, open));
    }

    private static Operator Declare(MemberType owner, OperatorKind kind, IMistType returns, params IMistType[] parameters)
    {
        var declared = new Operator(
            kind, returns, [.. parameters.Select((type, i) => new Parameter($"p{i}", type))], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = owner,
        };
        owner.OperatorsList.Add(declared);
        return declared;
    }
}
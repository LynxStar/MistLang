using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class NumericLiteralBindingTests
{
    private readonly BindingFragments fragments = new();

    public NumericLiteralBindingTests()
    {
        foreach(var name in new[] { "UInt64", "Single", "Decimal" })
        {
            fragments.Register(fragments.Built.Type(name));
        }

        foreach(var name in new[] { "Int32", "Int64" })
        {
            var signed = fragments.Built.Type(name);
            signed.OperatorsList.Add(new Operator(OperatorKind.Minus, signed, [new Parameter("a", signed)], new MemberModifiers { IsStatic = true }));
        }
    }

    [Theory]
    [InlineData("1_000_000", 1000000)]
    [InlineData("3000000000", 3000000000U)]
    [InlineData("18446744073709551615UL", 18446744073709551615UL)]
    public void ADecimalLiteral_BindsToItsExactValueAndType(string lexeme, object expected)
    {
        var literal = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        literal.IntegerLiteral = BindingFragments.Token(lexeme);

        AssertLiteral(expected, fragments.Bind(literal));
    }

    [Theory]
    [InlineData("0xFFu", 255U)]
    [InlineData("0xFF_FF", 65535)]
    [InlineData("0xFFFFFFFF", 4294967295U)]
    public void AHexLiteral_BindsToItsExactValueAndType(string lexeme, object expected)
    {
        var literal = BindingFragments.At<HexLiteralExpressionPrimaryNode>();
        literal.HexLiteral = BindingFragments.Token(lexeme);

        AssertLiteral(expected, fragments.Bind(literal));
    }

    [Theory]
    [InlineData("0b1010", 10)]
    [InlineData("0B1111_0000L", 240L)]
    public void ABinaryLiteral_BindsToItsExactValueAndType(string lexeme, object expected)
    {
        var literal = BindingFragments.At<BinaryLiteralExpressionPrimaryNode>();
        literal.BinaryLiteral = BindingFragments.Token(lexeme);

        AssertLiteral(expected, fragments.Bind(literal));
    }

    [Theory]
    [InlineData("1_000.5", 1000.5)]
    [InlineData("3.14_15f", 3.1415f)]
    [InlineData("2d", 2.0)]
    public void AFloatLiteral_BindsWithItsSeparatorsIgnored(string lexeme, object expected)
    {
        AssertLiteral(expected, fragments.Bind(Float(lexeme)));
    }

    [Fact]
    public void ADecimalFloatLiteral_BindsWithItsSeparatorsIgnored()
    {
        AssertLiteral(1000.25m, fragments.Bind(Float("1_000.25m")));
    }

    [Theory]
    [InlineData("18446744073709551616")]
    [InlineData("0x1_0000_0000_0000_0000")]
    public void AnIntegerPastULongMaxValue_IsRefusedRatherThanBoundAsZero(string lexeme)
    {
        var literal = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        literal.IntegerLiteral = BindingFragments.Token(lexeme);

        _ = Assert.IsType<ErrorExpression>(fragments.Bind(literal));
        Assert.Equal("MIST_PM_NUMERIC_LITERAL_OUT_OF_RANGE", Assert.Single(fragments.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void ADecimalPastItsRange_IsRefusedRatherThanBoundAsZero()
    {
        _ = Assert.IsType<ErrorExpression>(fragments.Bind(Float("99999999999999999999999999999.5m")));
        Assert.Equal("MIST_PM_NUMERIC_LITERAL_OUT_OF_RANGE", Assert.Single(fragments.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Theory]
    [InlineData("2147483648", int.MinValue)]
    [InlineData("9223372036854775808", long.MinValue)]
    public void ANegatedBoundary_BindsAsTheMinimumOfTheTypeBelow(string lexeme, object expected)
    {
        AssertLiteral(expected, Bind(Negated(Postfix(Integer(lexeme)))));
    }

    [Fact]
    public void AnOrdinaryNegatedLiteral_StaysANegation()
    {
        var negation = Assert.IsType<UnaryExpression>(Bind(Negated(Postfix(Integer("5")))));

        AssertLiteral(5, negation.Operand);
        Assert.Equal("Int32", negation.Type.Name);
    }

    [Theory]
    [InlineData("3000000000", 3000000000U)]
    [InlineData("4294967296", 4294967296L)]
    [InlineData("5L", 5L)]
    public void ANegatedLiteral_IsALongWhereItsMagnitudeOutgrowsAnInt(string lexeme, object magnitude)
    {
        var negation = Assert.IsType<UnaryExpression>(Bind(Negated(Postfix(Integer(lexeme)))));

        AssertLiteral(magnitude, negation.Operand);
        Assert.Equal("Int64", negation.Type.Name);
    }

    [Theory]
    [InlineData("5u")]
    [InlineData("5UL")]
    [InlineData("9223372036854775809")]
    public void ANegatedUnsignedLiteral_HasNoNegation(string lexeme)
    {
        var negation = Assert.IsType<UnaryExpression>(Bind(Negated(Postfix(Integer(lexeme)))));

        _ = Assert.IsType<ErrorType>(negation.Type);
    }

    [Fact]
    public void AParenthesizedBoundary_IsNoBoundary()
    {
        var parenthesized = BindingFragments.At<ParenthesizedExpressionPrimaryNode>();
        parenthesized.Inner = BindingFragments.Expression(Postfix(Integer("2147483648")));

        var negation = Assert.IsType<UnaryExpression>(Bind(Negated(Postfix(parenthesized))));

        AssertLiteral(2147483648U, negation.Operand);
        Assert.Equal("Int64", negation.Type.Name);
    }

    private Expression Bind(UnaryNode unary)
    {
        return ExpressionBinder.BindExpression(BindingFragments.Expression(unary), fragments.Binding, fragments.Builder);
    }

    private static IntegerLiteralExpressionPrimaryNode Integer(string lexeme)
    {
        var literal = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        literal.IntegerLiteral = BindingFragments.Token(lexeme);
        return literal;
    }

    private static FloatLiteralExpressionPrimaryNode Float(string lexeme)
    {
        var literal = BindingFragments.At<FloatLiteralExpressionPrimaryNode>();
        literal.FloatLiteral = BindingFragments.Token(lexeme);
        return literal;
    }

    private static PostfixExpressionUnaryNode Postfix(PrimaryNode primary)
    {
        return new PostfixExpressionUnaryNode { Primary = new PostfixNode { Target = primary } };
    }

    private static NegateUnaryNode Negated(UnaryNode operand)
    {
        var negate = BindingFragments.At<NegateUnaryNode>();
        negate.Operand = operand;
        return negate;
    }

    private static void AssertLiteral(object expected, Expression bound)
    {
        var literal = Assert.IsType<LiteralExpression>(bound);

        Assert.Equal(expected, literal.Value);
        Assert.Equal(expected.GetType().Name, literal.Type.Name);
    }
}
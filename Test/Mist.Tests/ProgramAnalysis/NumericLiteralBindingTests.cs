// Tier 2: numeric literals read through LiteralBinder where they are bound, the unary operator binder over
// hand-built negation syntax and the scope resolver over hand-built type argument lists.

using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class NumericLiteralBindingTests
{
    private const string OutOfRange = "MIST_PM_NUMERIC_LITERAL_OUT_OF_RANGE";

    [Theory]
    [InlineData("9223372036854775808L")]
    [InlineData("9223372036854775808l")]
    [InlineData("9223372036854775808")]
    [InlineData("9_223_372_036_854_775_808L")]
    public void TheMagnitudeOfLongsMinimumUnderAMinus_BindsAsThatConstant(string lexeme)
    {
        var f = new BindingFragments();
        var bound = ExpressionBinder.BindExpression(BindingFragments.Expression(Negated(Literal(lexeme))), f.Binding, f.Builder);

        var literal = Assert.IsType<LiteralExpression>(bound);
        Assert.Equal(long.MinValue, literal.Value);
        Assert.Equal("Int64", literal.Type.Name);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("bare", "9223372036854775808L")]
    [InlineData("negated", "9223372036854775809L")]
    [InlineData("negated", "9223372036854775808u")]
    [InlineData("negated-in-parentheses", "9223372036854775808L")]
    public void AMagnitudeLongCannotHoldAnywhereElse_IsRefused(string form, string lexeme)
    {
        var f = new BindingFragments();
        var operand = form == "negated-in-parentheses" ? Parenthesized(Literal(lexeme)) : Literal(lexeme);
        var syntax = form == "bare" ? BindingFragments.Expression(operand) : BindingFragments.Expression(Negated(operand));

        _ = ExpressionBinder.BindExpression(syntax, f.Binding, f.Builder);

        Assert.Equal(OutOfRange, Assert.Single(f.Diagnostics.ToImmutableArray()).Id.Value);
    }

    [Theory]
    [InlineData("integer", "5L", 5L)]
    [InlineData("integer", "7u", 7L)]
    [InlineData("integer", "1_000", 1000L)]
    [InlineData("negated", "5L", -5L)]
    [InlineData("negated", "5_000", -5000L)]
    [InlineData("negated", "9223372036854775808L", long.MinValue)]
    [InlineData("hex", "0x1_0", 16L)]
    [InlineData("hex", "0xFu", 15L)]
    [InlineData("binary", "0b1_01", 5L)]
    [InlineData("binary", "0B11L", 3L)]
    public void ADependentValueTypeArgument_LiftsTheValueItsLiteralSpells(string form, string lexeme, long expected)
    {
        var f = new BindingFragments();
        var lifted = Assert.IsType<LiftedType>(Assert.Single(f.Builder.Resolver.ResolveTypeArguments(Arguments(form, lexeme), f.Scope)));

        Assert.Equal(expected, lifted.Value);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("integer", "9223372036854775808")]
    [InlineData("integer", "18446744073709551615UL")]
    [InlineData("negated", "9223372036854775809")]
    [InlineData("hex", "0x1_0000_0000_0000_0000")]
    [InlineData("binary", "0b10000000000000000000000000000000000000000000000000000000000000000")]
    public void ADependentValueTypeArgumentNoLongHolds_IsRefused(string form, string lexeme)
    {
        var f = new BindingFragments();

        _ = Assert.IsType<ErrorType>(Assert.Single(f.Builder.Resolver.ResolveTypeArguments(Arguments(form, lexeme), f.Scope)));
        Assert.Equal(OutOfRange, Assert.Single(f.Diagnostics.ToImmutableArray()).Id.Value);
    }

    private static PostfixExpressionUnaryNode Literal(string lexeme)
    {
        var literal = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        literal.IntegerLiteral = BindingFragments.Token(lexeme);
        return Postfix(literal);
    }

    private static PostfixExpressionUnaryNode Parenthesized(UnaryNode inner)
    {
        var parenthesized = BindingFragments.At<ParenthesizedExpressionPrimaryNode>();
        parenthesized.Inner = BindingFragments.Expression(inner);
        return Postfix(parenthesized);
    }

    private static PostfixExpressionUnaryNode Postfix(PrimaryNode primary)
    {
        var postfix = BindingFragments.At<PostfixExpressionUnaryNode>();
        postfix.Primary = new PostfixNode { Target = primary, Suffix = [] };
        return postfix;
    }

    private static NegateUnaryNode Negated(UnaryNode operand)
    {
        var negated = BindingFragments.At<NegateUnaryNode>();
        negated.Operand = operand;
        return negated;
    }

    private static TypeArgumentListNode Arguments(string form, string lexeme)
    {
        var literal = BindingFragments.Token(lexeme);
        TypeArgumentNode argument = form switch
        {
            "integer" => With(BindingFragments.At<IntegerLiteralTypeArgumentTypeArgumentNode>(), node => node.Literal = literal),
            "negated" => With(BindingFragments.At<NegatedIntegerTypeArgumentTypeArgumentNode>(), node => node.Literal = literal),
            "hex" => With(BindingFragments.At<HexLiteralTypeArgumentTypeArgumentNode>(), node => node.Literal = literal),
            _ => With(BindingFragments.At<BinaryLiteralTypeArgumentTypeArgumentNode>(), node => node.Literal = literal),
        };

        return new TypeArgumentListNode { TypeArgument = argument, TypeArguments = [] };
    }

    private static T With<T>(T node, Action<T> set)
    {
        set(node);
        return node;
    }
}
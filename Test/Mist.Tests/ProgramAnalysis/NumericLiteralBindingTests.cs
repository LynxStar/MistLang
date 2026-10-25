// Tier 2: numeric literals read through LiteralBinder where they are bound, the unary operator binder over
// hand-built negation syntax and the scope resolver over hand-built type argument lists.

using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
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

    [Fact]
    public void AnLSuffixedMagnitudeLongCannotHold_IsAULong()
    {
        var f = new BindingFragments();
        f.Register(f.Built.Type("UInt64"));

        var literal = Assert.IsType<LiteralExpression>(
            ExpressionBinder.BindExpression(BindingFragments.Expression(Literal("9223372036854775808L")), f.Binding, f.Builder));

        Assert.Equal(9223372036854775808UL, literal.Value);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("negated", "9223372036854775809L")]
    [InlineData("negated", "9223372036854775808u")]
    [InlineData("negated-in-parentheses", "9223372036854775808L")]
    public void AMagnitudeLongCannotHoldUnderAMinus_IsANegatedULong(string form, string lexeme)
    {
        var f = new BindingFragments();
        f.Register(f.Built.Type("UInt64"));
        var operand = form == "negated-in-parentheses" ? Parenthesized(Literal(lexeme)) : Literal(lexeme);

        var negation = Assert.IsType<UnaryExpression>(
            ExpressionBinder.BindExpression(BindingFragments.Expression(Negated(operand)), f.Binding, f.Builder));

        _ = Assert.IsType<ulong>(Assert.IsType<LiteralExpression>(negation.Operand).Value);
        _ = Assert.IsType<ErrorType>(negation.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
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
    [InlineData("integer", "9223372036854775808", 9223372036854775808UL)]
    [InlineData("integer", "18446744073709551615UL", ulong.MaxValue)]
    [InlineData("hex", "0xFFFF_FFFF_FFFF_FFFF", ulong.MaxValue)]
    [InlineData("binary", "0b1000000000000000000000000000000000000000000000000000000000000000", 9223372036854775808UL)]
    public void ADependentValueTypeArgumentOnlyAULongHolds_LiftsAsThatULong(string form, string lexeme, ulong expected)
    {
        var f = new BindingFragments();
        var lifted = Assert.IsType<LiftedType>(Assert.Single(f.Builder.Resolver.ResolveTypeArguments(Arguments(form, lexeme), f.Scope)));

        Assert.Equal(expected, Assert.IsType<ulong>(lifted.Value));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("integer", "18446744073709551616", "ulong")]
    [InlineData("negated", "9223372036854775809", "long")]
    [InlineData("hex", "0x1_0000_0000_0000_0000", "ulong")]
    [InlineData("binary", "0b10000000000000000000000000000000000000000000000000000000000000000", "ulong")]
    public void ADependentValueTypeArgumentNoIntegralTypeHolds_IsRefusedNamingTheWidestOnItsSide(string form, string lexeme, string widest)
    {
        var f = new BindingFragments();

        _ = Assert.IsType<ErrorType>(Assert.Single(f.Builder.Resolver.ResolveTypeArguments(Arguments(form, lexeme), f.Scope)));
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(OutOfRange, diagnostic.Number.Value);
        Assert.EndsWith($"'{widest}'.", diagnostic.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A type argument's identity is its value, so every spelling of one value is the one interned type
    /// -- the suffix and the radix say how it was written, never which construction it closes.
    /// </summary>
    [Theory]
    [InlineData("integer", "5", "integer", "5UL")]
    [InlineData("integer", "5", "hex", "0x5")]
    [InlineData("integer", "5", "binary", "0b101")]
    [InlineData("integer", "5L", "integer", "5u")]
    [InlineData("integer", "9223372036854775807", "hex", "0x7FFF_FFFF_FFFF_FFFF")]
    [InlineData("integer", "18446744073709551615", "hex", "0xFFFF_FFFF_FFFF_FFFFUL")]
    [InlineData("negated", "1", "negated", "1L")]
    public void EquivalentSpellingsOfOneValue_InternAsOneTypeArgument(string form, string lexeme, string otherForm, string otherLexeme)
    {
        var f = new BindingFragments();

        var first = Assert.Single(f.Builder.Resolver.ResolveTypeArguments(Arguments(form, lexeme), f.Scope));
        var second = Assert.Single(f.Builder.Resolver.ResolveTypeArguments(Arguments(otherForm, otherLexeme), f.Scope));

        Assert.Same(first, second);
    }

    [Fact]
    public void ULongsUpperHalfAndItsWrappedLong_AreDistinctTypeArguments()
    {
        var f = new BindingFragments();

        var top = Assert.Single(f.Builder.Resolver.ResolveTypeArguments(Arguments("integer", "18446744073709551615"), f.Scope));
        var minusOne = Assert.Single(f.Builder.Resolver.ResolveTypeArguments(Arguments("negated", "1"), f.Scope));

        Assert.NotSame(top, minusOne);
    }

    [Theory]
    [InlineData("2147483648")]
    [InlineData("2_147_483_648")]
    public void TheMagnitudeOfIntsMinimumUnderAMinus_BindsAsThatConstant(string lexeme)
    {
        var f = new BindingFragments();
        var bound = ExpressionBinder.BindExpression(BindingFragments.Expression(Negated(Literal(lexeme))), f.Binding, f.Builder);

        var literal = Assert.IsType<LiteralExpression>(bound);
        Assert.Equal(int.MinValue, literal.Value);
        Assert.Equal("Int32", literal.Type.Name);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("2147483649")]
    [InlineData("4294967295")]
    public void AnUnsuffixedMagnitudeBetweenIntAndUIntMax_NegatesToLong(string lexeme)
    {
        var f = new BindingFragments();
        var bound = ExpressionBinder.BindExpression(BindingFragments.Expression(Negated(Literal(lexeme))), f.Binding, f.Builder);

        var negation = Assert.IsType<UnaryExpression>(bound);
        _ = Assert.IsType<uint>(Assert.IsType<LiteralExpression>(negation.Operand).Value);
        Assert.Equal("Int64", negation.Type.Name);
        Assert.Null(negation.Operator);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(true, "class")]
    [InlineData(false, "class")]
    [InlineData(false, "Int32")]
    [InlineData(true, "unknown")]
    [InlineData(false, "unknown")]
    [InlineData(true, "Byte")]
    [InlineData(true, "UInt16")]
    [InlineData(true, "UInt32")]
    [InlineData(true, "UInt64")]
    [InlineData(true, "Char")]
    public void AnUnsupportedUnaryLocal_BindsAnErrorResultForAnalysis(bool negate, string type)
    {
        var f = new BindingFragments();
        IMistType operandType = type switch
        {
            "class" => HandBuiltModel.Class("Thing"),
            "unknown" => UnknownType.Instance,
            _ => f.Built.Type(type),
        };
        _ = f.Local("value", operandType);
        var bound = ExpressionBinder.BindExpression(
            BindingFragments.Expression(Unary(negate, Postfix(BindingFragments.Identifier("value")))), f.Binding, f.Builder);

        var unary = Assert.IsType<UnaryExpression>(bound);
        Assert.Same(operandType, unary.Operand.Type);
        Assert.Same(ErrorType.Instance, unary.Type);
        Assert.Null(unary.Operator);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(true, "named")]
    [InlineData(true, "anonymous")]
    [InlineData(true, "alias")]
    [InlineData(true, "lifted")]
    [InlineData(false, "named")]
    [InlineData(false, "anonymous")]
    [InlineData(false, "alias")]
    [InlineData(false, "lifted")]
    public void AUnaryLocal_BindsTheBaseResultWithoutItsInputPromise(bool negate, string wrapper)
    {
        var f = new BindingFragments();
        var baseType = f.Built.Type(negate ? "Int32" : "Boolean");
        IMistType operandType = wrapper switch
        {
            "named" => HandBuiltModel.Refined(baseType, "Promise"),
            "anonymous" => HandBuiltModel.Refined(baseType),
            "alias" => new DeclarationFragments().Alias("Promise", HandBuiltModel.Refined(baseType, "Refinement")),
            _ => new LiftedType(baseType, negate ? 2L : true),
        };
        _ = f.Local("value", operandType);
        var bound = ExpressionBinder.BindExpression(
            BindingFragments.Expression(Unary(negate, Postfix(BindingFragments.Identifier("value")))), f.Binding, f.Builder);

        var unary = Assert.IsType<UnaryExpression>(bound);
        Assert.Same(operandType, unary.Operand.Type);
        Assert.Same(baseType, unary.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ADeclaredUnaryOperator_BindsExactlyItsReturnContract(bool negate, bool refinedReturn)
    {
        var f = new BindingFragments();
        var operandType = HandBuiltModel.Class("Thing");
        var baseReturn = f.Built.Type(negate ? "Int32" : "Boolean");
        IMistType returnType = refinedReturn ? HandBuiltModel.Refined(baseReturn, "ResultPromise") : baseReturn;
        var declared = new Operator(negate ? OperatorKind.Minus : OperatorKind.Exclamation, returnType,
            [new Parameter("value", operandType)], new MemberModifiers { IsStatic = true });
        operandType.OperatorsList.Add(declared);
        _ = f.Local("value", operandType);
        var bound = ExpressionBinder.BindExpression(
            BindingFragments.Expression(Unary(negate, Postfix(BindingFragments.Identifier("value")))), f.Binding, f.Builder);

        var unary = Assert.IsType<UnaryExpression>(bound);
        Assert.Same(declared, unary.Operator);
        Assert.Same(returnType, unary.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static UnaryNode Unary(bool negate, UnaryNode operand)
    {
        if(negate)
        {
            return Negated(operand);
        }

        var logicalNot = BindingFragments.At<LogicalNotUnaryNode>();
        logicalNot.Operand = operand;
        return logicalNot;
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
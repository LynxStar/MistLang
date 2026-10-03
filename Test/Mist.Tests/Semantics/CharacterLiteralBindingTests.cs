using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class CharacterLiteralBindingTests
{
    private readonly BindingFragments fragments = new();

    public CharacterLiteralBindingTests()
    {
        fragments.Register(fragments.Built.Type("Char"));
    }

    [Theory]
    [InlineData("'\\n'", '\n')]
    [InlineData("'\\\\'", '\\')]
    [InlineData("'\\''", '\'')]
    public void Expression_BindsTheDecodedValue(string lexeme, char expected)
    {
        var bound = Assert.IsType<LiteralExpression>(fragments.Bind(Literal(lexeme)));

        Assert.Equal(expected, bound.Value);
        Assert.Equal("Standard.Char", bound.Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("'\\n'", '\n')]
    [InlineData("'\\\\'", '\\')]
    public void DependentTypeArgument_LiftsTheDecodedValue(string lexeme, char expected)
    {
        var argument = BindingFragments.At<CharLiteralTypeArgumentTypeArgumentNode>();
        argument.Literal = BindingFragments.Token(lexeme);
        var arguments = new TypeArgumentListNode { TypeArgument = argument, TypeArguments = [] };

        var lifted = Assert.IsType<LiftedType>(Assert.Single(fragments.Builder.Resolver.ResolveTypeArguments(arguments, fragments.Scope)));

        Assert.Equal(expected, lifted.Value);
        Assert.Equal("Standard.Char", lifted.Type.FullyQualifiedName);
    }

    [Fact]
    public void ConstantPatterns_KeepNewlineAndBackslashDistinct()
    {
        var newline = Constant("'\\n'");
        var backslash = Constant("'\\\\'");

        Assert.Equal('\n', newline.Value);
        Assert.Equal('\\', backslash.Value);
        Assert.NotEqual(newline.Value, backslash.Value);
    }

    [Fact]
    public void RefinementBound_UsesTheDecodedTab()
    {
        var expression = (AssignmentFormExpressionNode)BindingFragments.Expression(Literal("'\\t'"));
        var comparison = new GreaterThanRelationalPatternPrimaryPatternNode { Constant = expression.Assignment.Target };
        var bound = Assert.IsType<ComparisonPattern>(PatternBinder.BindPredicate(Predicate(comparison), Character, fragments.Binding, fragments.Builder));

        Assert.Equal(ComparisonKind.GreaterThan, bound.Kind);
        Assert.Equal('\t', Assert.IsType<LiteralExpression>(bound.Bound).Value);
    }

    [Fact]
    public void EnumValue_BindsTheDecodedCharacter()
    {
        var enumType = new EnumType("Separators", "Probe", null, null, BindingFragments.At<EnumDeclarationNode>())
        {
            UnderlyingType = Character,
        };
        enumType.MembersList.Add(new EnumMember("Line", false,
            new UnresolvedExpression(BindingFragments.Expression(Literal("'\\n'"))), BindingFragments.At<EnumMemberNode>()));

        EnumMemberBinder.BindAll(enumType, fragments.Builder);

        Assert.Equal('\n', Assert.IsType<LiteralExpression>(Assert.Single(enumType.MembersList).Value).Value);
    }

    private IMistType Character => fragments.Built.Type("Char");

    private LiteralExpression Constant(string lexeme)
    {
        var primary = new ConstantPatternPrimaryPatternNode
        {
            Literal = new CharConstantConstantLiteralNode { Value = BindingFragments.Token(lexeme) },
        };
        var comparison = Assert.IsType<ComparisonPattern>(PatternBinder.BindPredicate(Predicate(primary), Character, fragments.Binding, fragments.Builder));
        return Assert.IsType<LiteralExpression>(comparison.Bound);
    }

    private static AndPatternNode Predicate(PrimaryPatternNode primary)
    {
        return new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } };
    }

    private static CharLiteralExpressionPrimaryNode Literal(string lexeme)
    {
        var literal = BindingFragments.At<CharLiteralExpressionPrimaryNode>();
        literal.CharLiteral = BindingFragments.Token(lexeme);
        return literal;
    }
}
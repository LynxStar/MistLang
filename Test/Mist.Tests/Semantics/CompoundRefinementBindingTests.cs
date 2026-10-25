using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class CompoundRefinementBindingTests
{
    private readonly BindingFragments fragments = new();

    public CompoundRefinementBindingTests()
    {
        fragments.Register(Int);
    }

    private IMistType Int => fragments.Built.Type("Int32");

    [Fact]
    public void ALeadingAndPredicateBindsItsOperandsInWrittenOrder()
    {
        var compound = new CompoundRefinementPredicateRefinementPredicateNode
        {
            First = Direct(GreaterThan("0")),
            UnaryPatterns = [Direct(LessThan("10"))],
        };

        var bound = Assert.IsType<ConjunctionPattern>(Bind(compound));

        Assert.Same(compound, bound.Node);
        Assert.Same(Int, bound.NarrowedType);
        Assert.False(bound.IsFailed);
        Assert.Collection(bound.Operands,
            operand => AssertComparison(operand, ComparisonKind.GreaterThan, 0),
            operand => AssertComparison(operand, ComparisonKind.LessThan, 10));
    }

    [Fact]
    public void ASingleLeadingAndOperandStillBindsAsAConjunction()
    {
        var compound = new CompoundRefinementPredicateRefinementPredicateNode
        {
            First = Direct(GreaterThan("0")),
            UnaryPatterns = [],
        };

        var bound = Assert.IsType<ConjunctionPattern>(Bind(compound));

        AssertComparison(Assert.Single(bound.Operands), ComparisonKind.GreaterThan, 0);
    }

    [Fact]
    public void ALeadingAndPredicateEstablishesNoBinding()
    {
        var binding = BindingFragments.At<VarPatternPrimaryPatternNode>();
        binding.Name = BindingFragments.Token("n");
        var compound = new CompoundRefinementPredicateRefinementPredicateNode
        {
            First = Direct(GreaterThan("0")),
            UnaryPatterns = [Direct(binding)],
        };

        _ = Bind(compound);

        Assert.Contains(fragments.Diagnostics.ToImmutableArray(),
            diagnostic => diagnostic.Number == ProgramModelDiagnostics.PatternBindingNotEstablished.Number);
    }

    private Pattern? Bind(CompoundRefinementPredicateRefinementPredicateNode compound)
    {
        return PatternBinder.BindRefinement(new RefinedType(Int, compound, "Small"), fragments.Binding, fragments.Builder);
    }

    private static void AssertComparison(Pattern operand, ComparisonKind kind, long value)
    {
        var comparison = Assert.IsType<ComparisonPattern>(operand);
        Assert.Equal(kind, comparison.Kind);
        Assert.Equal(value, Convert.ToInt64(Assert.IsType<LiteralExpression>(comparison.Bound).Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static DirectPatternUnaryPatternNode Direct(PrimaryPatternNode primary)
    {
        return new DirectPatternUnaryPatternNode { Primary = primary };
    }

    private static GreaterThanRelationalPatternPrimaryPatternNode GreaterThan(string lexeme)
    {
        var relational = BindingFragments.At<GreaterThanRelationalPatternPrimaryPatternNode>();
        relational.Constant = Bound(lexeme);
        return relational;
    }

    private static LessThanRelationalPatternPrimaryPatternNode LessThan(string lexeme)
    {
        var relational = BindingFragments.At<LessThanRelationalPatternPrimaryPatternNode>();
        relational.Constant = Bound(lexeme);
        return relational;
    }

    private static VoidCoalescingNode Bound(string lexeme)
    {
        var literal = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        literal.IntegerLiteral = BindingFragments.Token(lexeme);
        return ((AssignmentFormExpressionNode)BindingFragments.Expression(literal)).Assignment.Target;
    }
}
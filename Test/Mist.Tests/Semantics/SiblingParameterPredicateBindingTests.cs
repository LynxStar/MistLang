using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 2: PredicateBinder.BindParameters over hand-built `Take(int lo, int hi) where lo < hi`, whose
// bound names the parameter beside the one it constrains.
[Trait("Tier", "2")]
public sealed class SiblingParameterPredicateBindingTests
{
    private readonly BindingFragments fragments = new();

    [Fact]
    public void ABoundNamingAnotherParameter_ReadsThatParameter()
    {
        var (lower, upper) = Take("hi");

        PredicateBinder.BindParameters([lower, upper], fragments.Scope, fragments.Builder);

        var bound = Assert.IsType<LocalReferenceExpression>(Assert.IsType<ComparisonPattern>(((RefinedType)lower.Type).Predicate).Bound);
        Assert.Same(upper, bound.Local.Parameter);
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AParameterShadowsALocalOfTheSameNameAroundTheDeclaration()
    {
        fragments.Scope.DeclareLocal(new LocalVariable("hi", fragments.Built.Type("Int32")), HandBuiltModel.Node(), fragments.Diagnostics);
        var (lower, upper) = Take("hi");

        PredicateBinder.BindParameters([lower, upper], fragments.Scope, fragments.Builder);

        var bound = Assert.IsType<LocalReferenceExpression>(Assert.IsType<ComparisonPattern>(((RefinedType)lower.Type).Predicate).Bound);
        Assert.Same(upper, bound.Local.Parameter);
    }

    [Fact]
    public void ARepeatedParameterName_IsNotReportedByThePredicatesScope()
    {
        var (lower, upper) = Take("hi");

        PredicateBinder.BindParameters([lower, upper, new Parameter("hi", fragments.Built.Type("Int32"))], fragments.Scope, fragments.Builder);

        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    private (Parameter Lower, Parameter Upper) Take(string named)
    {
        var integer = fragments.Built.Type("Int32");
        var expression = (AssignmentFormExpressionNode)BindingFragments.Expression(BindingFragments.Identifier(named));
        var predicate = new AndPatternNode
        {
            Left = new DirectPatternUnaryPatternNode { Primary = new LessThanRelationalPatternPrimaryPatternNode { Constant = expression.Assignment.Target } },
        };

        return (new Parameter("lo", new RefinedType(integer, predicate)), new Parameter("hi", integer));
    }
}
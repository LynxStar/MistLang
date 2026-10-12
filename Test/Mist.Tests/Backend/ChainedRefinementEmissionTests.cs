using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ChainedRefinementEmissionTests
{
    private readonly HandBuiltRefinements h = new();

    [Fact]
    public void ATestOfARefinementOverARefinement_EmitsEveryPredicateInTheChain()
    {
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
        var smallPositive = h.Refine(positive, ComparisonKind.LessThan, 10L, "SmallPositive");
        var value = HandBuiltRefinements.Local(h.Int);

        Assert.Equal("value is int and > 0 and < 10", Emit(new IsPatternExpression(HandBuiltModel.Node(), value, Tested(smallPositive), h.Bool)));
        Assert.Equal("value is int and > 0 and < 10 and var s",
            Emit(new IsPatternExpression(HandBuiltModel.Node(), value, Tested(smallPositive, new LocalVariable("s", smallPositive)), h.Bool)));
        Assert.Equal("value is not (int and > 0 and < 10)",
            Emit(new IsPatternExpression(HandBuiltModel.Node(), value, new NegatedPattern(HandBuiltModel.Node(), h.Int, Tested(smallPositive)), h.Bool)));
        Assert.Equal("value switch { int and > 0 and < 10 => 1, int and > 0 => 2, var _ => 3 }",
            Emit(new SwitchExpression(HandBuiltModel.Node(), value,
            [
                new SwitchArm(HandBuiltModel.Node(), Tested(smallPositive), null, h.Literal(1L)),
                new SwitchArm(HandBuiltModel.Node(), Tested(positive), null, h.Literal(2L)),
                new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), h.Int, null), null, h.Literal(3L)),
            ], h.Int)));
    }

    private static TypeTestPattern Tested(RefinedType type, LocalVariable? binding = null)
    {
        return new TypeTestPattern(HandBuiltModel.Node(), type, binding,
            refinement: PatternBinder.ComposeRefinement(type, HandBuiltModel.Node(), refined => refined.Predicate));
    }

    private static string Emit(Expression expression)
    {
        return PropertyPatternEmissionTests.Emit(expression);
    }
}
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class FiniteCoverageProductConjunctionTests
{
    [Fact]
    public void TupleConjunctionAndMixedArmsLeaveOnlyTheFalsePair()
    {
        var missing = MissingWithConjunction((false, true), (true, false));

        Assert.Equal(["(false, false)"], missing);
    }

    [Fact]
    public void TupleConjunctionLeavesPairsThatMatchOnlyOneOperand()
    {
        var missing = MissingWithConjunction((false, false));

        Assert.NotNull(missing);
        Assert.Equal(["(false, true)", "(true, false)"], missing.Order(StringComparer.Ordinal));
    }

    private static IReadOnlyList<string>? MissingWithConjunction(params (bool First, bool Second)[] coveredPairs)
    {
        var boolean = new StructType(WellKnownTypes.Boolean.Name, WellKnownTypes.Boolean.Namespace, null, null, null);
        var pair = new TupleType([boolean, boolean]);
        var discard = new CatchAllPattern(HandBuiltModel.Node(), boolean, null);
        var conjunction = new ConjunctionPattern(HandBuiltModel.Node(), pair,
            [tuple(constant(true), discard), tuple(discard, constant(true))]);

        return FiniteCoverage.Missing(pair,
            [conjunction, .. coveredPairs.Select(covered => tuple(constant(covered.First), constant(covered.Second)))]);

        ComparisonPattern constant(bool value)
        {
            return new ComparisonPattern(HandBuiltModel.Node(), boolean, ComparisonKind.Equal, HandBuiltModel.Literal(boolean, value));
        }

        TuplePattern tuple(Pattern first, Pattern second)
        {
            return new TuplePattern(HandBuiltModel.Node(), [first, second], pair, null, null);
        }
    }
}
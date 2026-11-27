using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class FiniteCoverageProductDisjunctionTests
{
    [Fact]
    public void OffDiagonalTupleAlternativesLeaveBothDiagonalPairsUncovered()
    {
        var node = HandBuiltModel.Node();
        var boolean = new StructType(WellKnownTypes.Boolean.Name, WellKnownTypes.Boolean.Namespace, null, null, null);
        var pair = new TupleType([boolean, boolean]);
        var disjunction = new DisjunctionPattern(node, pair, [tuple(false, true), tuple(true, false)]);

        var missing = FiniteCoverage.Missing(pair, [disjunction]);

        Assert.NotNull(missing);
        Assert.Equal(["(false, false)", "(true, true)"], missing.Order(StringComparer.Ordinal));

        TuplePattern tuple(bool first, bool second)
        {
            return new TuplePattern(node, [constant(first), constant(second)], pair, null, null);
        }

        ComparisonPattern constant(bool value)
        {
            return new ComparisonPattern(node, boolean, ComparisonKind.Equal, HandBuiltModel.Literal(boolean, value));
        }
    }
}
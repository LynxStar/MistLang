using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class FiniteCoverageNestedProductWitnessTests
{
    [Fact]
    public void MissingWitnessKeepsNestedTuplePositions()
    {
        var node = HandBuiltModel.Node();
        var boolean = new StructType(WellKnownTypes.Boolean.Name, WellKnownTypes.Boolean.Namespace, null, null, null);
        var pair = new TupleType([boolean, boolean]);
        var nested = new TupleType([pair, boolean]);
        var isTrue = new ComparisonPattern(node, boolean, ComparisonKind.Equal, HandBuiltModel.Literal(boolean, true));
        var isFalse = new ComparisonPattern(node, boolean, ComparisonKind.Equal, HandBuiltModel.Literal(boolean, false));
        var discard = new CatchAllPattern(node, boolean, null);

        var missing = FiniteCoverage.Missing(nested,
        [
            arm(isTrue, discard, discard),
            arm(isFalse, isFalse, discard),
            arm(isFalse, isTrue, isFalse),
        ]);

        Assert.Equal(["((false, true), true)"], missing);

        TuplePattern arm(Pattern innerFirst, Pattern innerSecond, Pattern outerSecond)
        {
            var inner = new TuplePattern(node, [innerFirst, innerSecond], pair, testedType: null, binding: null);
            return new TuplePattern(node, [inner, outerSecond], nested, testedType: null, binding: null);
        }
    }
}
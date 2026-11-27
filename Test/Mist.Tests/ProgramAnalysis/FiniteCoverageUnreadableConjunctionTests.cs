using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class FiniteCoverageUnreadableConjunctionTests
{
    [Fact]
    public void UnreadableOperandMakesTheSearchAbstainAfterAReadableArm()
    {
        var missing = MissingWithTextPattern(text => new RegexPattern(HandBuiltModel.Node(), text, "[a-z]+"));

        Assert.Null(missing);
    }

    [Fact]
    public void ReadableConjunctionLeavesTheFalseCoordinateUncovered()
    {
        var missing = MissingWithTextPattern(text => new CatchAllPattern(HandBuiltModel.Node(), text, null));

        Assert.Equal(["(false, _)"], missing);
    }

    private static IReadOnlyList<string>? MissingWithTextPattern(Func<IMistType, Pattern> textPattern)
    {
        var node = HandBuiltModel.Node();
        var boolean = new StructType(WellKnownTypes.Boolean.Name, WellKnownTypes.Boolean.Namespace, null, null, null);
        var text = new ClassType(WellKnownTypes.String.Name, WellKnownTypes.String.Namespace, null, null, null);
        var pair = new TupleType([boolean, text]);
        var isTrue = new ComparisonPattern(node, boolean, ComparisonKind.Equal, HandBuiltModel.Literal(boolean, true));
        var anyBoolean = new CatchAllPattern(node, boolean, null);
        var anyText = new CatchAllPattern(node, text, null);
        var truePair = new TuplePattern(node, [isTrue, anyText], pair, null, null);
        var textPair = new TuplePattern(node, [anyBoolean, textPattern(text)], pair, null, null);
        var conjunction = new ConjunctionPattern(node, pair, [truePair, textPair]);

        return FiniteCoverage.Missing(pair, [truePair, conjunction]);
    }
}
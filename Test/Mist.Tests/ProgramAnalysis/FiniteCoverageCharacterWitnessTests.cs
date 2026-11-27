using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class FiniteCoverageCharacterWitnessTests
{
    [Theory]
    [InlineData('\'', """'\''""")]
    [InlineData('\\', """'\\'""")]
    [InlineData('A', "'A'")]
    public void StrictRelationalArmsNameTheOnlyMissingCharacter(char excluded, string witness)
    {
        var missing = MissingAcrossBoundary(excluded, ComparisonKind.LessThan);

        Assert.Equal([witness], missing);
    }

    [Fact]
    public void IncludingTheBoundaryLeavesNoMissingCharacter()
    {
        var missing = MissingAcrossBoundary('\\', ComparisonKind.LessThanOrEqual);

        Assert.NotNull(missing);
        Assert.Empty(missing);
    }

    private static IReadOnlyList<string>? MissingAcrossBoundary(char boundary, ComparisonKind lowerComparison)
    {
        var character = new StructType(WellKnownTypes.Char.Name, WellKnownTypes.Char.Namespace, null, null, null);
        var node = HandBuiltModel.Node();
        var bound = HandBuiltModel.Literal(character, boundary);
        var lower = new ComparisonPattern(node, character, lowerComparison, bound);
        var upper = new ComparisonPattern(node, character, ComparisonKind.GreaterThan, bound);

        return FiniteCoverage.Missing(character, [lower, upper]);
    }
}
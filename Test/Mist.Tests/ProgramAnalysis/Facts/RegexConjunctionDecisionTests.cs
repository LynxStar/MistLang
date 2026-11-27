using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class RegexConjunctionDecisionTests
{
    private const string Lowercase = "[a-z]+";
    private const string EndsInZ = ".*z";

    [Fact]
    public void ATypeTestAndAnInlineRegexStateBothMatches()
    {
        var h = new HandBuiltRefinements();
        var node = HandBuiltModel.Node();
        var letters = new RegexPattern(node, h.String, Lowercase);
        var named = HandBuiltRefinements.RefinePattern(h.String, letters, "Lowercase");
        var typeTest = new TypeTestPattern(node, named, binding: null, refinement: letters);
        var inline = new RegexPattern(node, named, EndsInZ);
        var conjunction = new ConjunctionPattern(node, named, [typeTest, inline]);

        var stated = Assert.NotNull(Matching.Stated(conjunction));

        Assert.Equal([EndsInZ, Lowercase], stated.Distinct().Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void ARegexRefinementConjunctionRequiresBothMatches(bool hasNamedMatch, bool hasInlineMatch, bool expected)
    {
        var h = new HandBuiltRefinements();
        var node = HandBuiltModel.Node();
        var named = h.Regex(Lowercase, "Lowercase");
        var text = new Value(hasNamedMatch ? named : h.String, "text", node);
        var target = HandBuiltRefinements.RefinePattern(h.String, new ConjunctionPattern(node, h.String,
            [new RegexPattern(node, h.String, Lowercase), new RegexPattern(node, h.String, EndsInZ)]));

        Assert.Equal(expected, Matching.Proven(Bag.Empty, text, target, _ => null,
            value => hasInlineMatch && value == text ? [EndsInZ] : [], LiteralPredicates.MatchLimit));
    }
}